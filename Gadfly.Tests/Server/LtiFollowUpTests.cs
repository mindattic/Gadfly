using Gadfly.Server.Lti;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// GAD-T458, the LTI follow-ups: grade-passback backoff, when a scheduled roster refresh is due, the new-window restart of a
/// launch whose cookie a frame blocked, and the pieces of the email confirmation that links an existing account.
/// </summary>
[TestFixture]
public class LtiFollowUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void PassbackBackoff_DoublesFromTwoMinutes_AndIsCappedAtTwelveHours()
    {
        Assert.That(LtiGradePassbackService.Backoff(1), Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(LtiGradePassbackService.Backoff(2), Is.EqualTo(TimeSpan.FromMinutes(4)));
        Assert.That(LtiGradePassbackService.Backoff(3), Is.EqualTo(TimeSpan.FromMinutes(8)));
        Assert.That(LtiGradePassbackService.Backoff(7), Is.EqualTo(TimeSpan.FromMinutes(128)));
        Assert.That(LtiGradePassbackService.Backoff(30), Is.EqualTo(TimeSpan.FromHours(12)));
        Assert.That(LtiGradePassbackService.Backoff(0), Is.EqualTo(TimeSpan.FromMinutes(2)), "never shorter than the first wait");
    }

    [Test]
    public void PassbackTries_SpanHoursNotDays_BeforeTheyAreMarkedFailed()
    {
        var total = Enumerable.Range(1, LtiGradePassbackService.MaxAttempts - 1).Aggregate(TimeSpan.Zero, (t, n) => t + LtiGradePassbackService.Backoff(n));
        Assert.That(total, Is.GreaterThan(TimeSpan.FromHours(1)).And.LessThan(TimeSpan.FromHours(12)));
    }

    [Test]
    public void ARoster_IsDueOncePerInterval_AndNeverWhenTheScheduleIsOff()
    {
        Assert.That(LtiRosterRefreshJob.IsDue(null, 24, Now), Is.True, "never synced");
        Assert.That(LtiRosterRefreshJob.IsDue(Now.AddHours(-23), 24, Now), Is.False);
        Assert.That(LtiRosterRefreshJob.IsDue(Now.AddHours(-24), 24, Now), Is.True);
        Assert.That(LtiRosterRefreshJob.IsDue(null, 0, Now), Is.False, "0 means only when an admin asks");
        Assert.That(LtiRosterRefreshJob.IsDue(Now.AddHours(-3), 2, Now), Is.True);
    }

    [Test]
    public void ARosterInterval_OutsideZeroToThirtyDays_IsRefused()
    {
        LtiRegistrationInput Input(int hours) => new()
        {
            Name = "Canvas", Issuer = "https://canvas.instructure.com", ClientId = "1", DeploymentId = "d",
            AuthLoginUrl = "https://sso.canvaslms.com/api/lti/authorize_redirect", AuthTokenUrl = "https://sso.canvaslms.com/login/oauth2/token",
            KeySetUrl = "https://sso.canvaslms.com/api/lti/security/jwks", RosterRefreshHours = hours,
        };
        Assert.That(LtiAdminService.Problem(Input(24)), Is.Null);
        Assert.That(LtiAdminService.Problem(Input(0)), Is.Null);
        Assert.That(LtiAdminService.Problem(Input(-1)), Does.Contain("hours"));
        Assert.That(LtiAdminService.Problem(Input(LtiAdminService.MaxRosterRefreshHours + 1)), Does.Contain("hours"));
    }

    [Test]
    public void TheNewWindowRestart_CarriesWhatLoginNeeds_AndLeavesOutWhatThePlatformDidNotSend()
    {
        var full = new LtiRestart("https://canvas.instructure.com", "10000000000001", "dep-1", "hint", "message-hint", "https://gadfly.example.com/lti/launch")
            .Fields().ToDictionary(f => f.Name, f => f.Value);
        Assert.That(full.Keys, Is.EquivalentTo(new[] { "iss", "client_id", "lti_deployment_id", "login_hint", "lti_message_hint", "target_link_uri" }));
        Assert.That(full["login_hint"], Is.EqualTo("hint"));

        var bare = new LtiRestart("https://moodle.example.edu", "abc", "1", "hint", null, null).Fields().Select(f => f.Name);
        Assert.That(bare, Is.EquivalentTo(new[] { "iss", "client_id", "lti_deployment_id", "login_hint" }));
    }

    [Test]
    public void AnEmailAddress_IsMasked_SoTheOwnerRecognizesItButNobodyElseLearnsIt()
    {
        Assert.That(LtiAccountLinks.Mask("jordan@example.edu"), Is.EqualTo("j•••••@example.edu"));
        Assert.That(LtiAccountLinks.Mask("al@example.edu"), Is.EqualTo("a•@example.edu"));
        Assert.That(LtiAccountLinks.Mask("not-an-address"), Is.EqualTo("your account's email address"));
        Assert.That(LtiAccountLinks.Mask(null), Is.EqualTo("your account's email address"));
    }

    [Test]
    public void TokensAreStoredOnlyAsHashes()
    {
        var token = LtiLaunchService.RandomToken(40);
        var hash = LtiAccountLinks.Hash(token);
        Assert.That(hash, Has.Length.EqualTo(64).And.Not.Contain(token));
        Assert.That(LtiAccountLinks.Hash(token), Is.EqualTo(hash));
        Assert.That(LtiAccountLinks.Hash(token + "x"), Is.Not.EqualTo(hash));
    }

    [Test]
    public void EachLinkRequest_HasItsOwnBrowserCookie()
    {
        var a = LtiAccountLinks.BrowserCookieName(Guid.NewGuid());
        var b = LtiAccountLinks.BrowserCookieName(Guid.NewGuid());
        Assert.That(a, Does.StartWith("gadfly-lti-link-"));
        Assert.That(a, Is.Not.EqualTo(b));
    }
}
