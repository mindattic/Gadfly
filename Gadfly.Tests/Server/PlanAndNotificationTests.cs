using Gadfly.Server.Notifications;
using Gadfly.Server.Platform;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Plan numbers and the warning/blocked thresholds (GAD-T309). Pure, no database.</summary>
[TestFixture]
public class PlanCatalogTests
{
    [TestCase(PlanCatalog.Trial, 3, 25, 50)]
    [TestCase(PlanCatalog.Standard, 10, 250, 500)]
    [TestCase(PlanCatalog.Institution, 50, 2500, 5000)]
    public void DefaultsFor_matches_the_pricing_page(string plan, int staff, int students, int exams)
    {
        var d = PlanCatalog.DefaultsFor(plan);
        Assert.That(d.StaffSeats, Is.EqualTo(staff));
        Assert.That(d.StudentSeats, Is.EqualTo(students));
        Assert.That(d.MonthlyExams, Is.EqualTo(exams));
    }

    [Test]
    public void DefaultsFor_an_unknown_name_falls_back_to_Trial() =>
        Assert.That(PlanCatalog.DefaultsFor("NotAPlan"), Is.EqualTo(PlanCatalog.DefaultsFor(PlanCatalog.Trial)));

    [Test]
    public void IsValid_accepts_only_the_four_named_plans()
    {
        foreach (var p in PlanCatalog.All) Assert.That(PlanCatalog.IsValid(p), Is.True);
        Assert.That(PlanCatalog.IsValid("Gold"), Is.False);
        Assert.That(PlanCatalog.IsValid(null), Is.False);
    }

    private static PlanStatus Status(int staffUsed, int staffLimit, int studentUsed, int studentLimit, int examsUsed, int? examAllowance, bool trial = false, bool trialExpired = false) =>
        new(trial ? PlanCatalog.Trial : PlanCatalog.Standard, staffLimit, staffUsed, studentLimit, studentUsed, examAllowance, examsUsed,
            DateTimeOffset.UtcNow.AddDays(trial ? -(trialExpired ? 31 : 10) : -10), trial ? DateTimeOffset.UtcNow.AddDays(trialExpired ? -1 : 20) : null,
            trial ? DateTimeOffset.UtcNow.AddDays(trialExpired ? -31 : -10) : null, null, null);

    [Test]
    public void Seats_warn_at_80_percent_and_block_at_the_limit()
    {
        var under = Status(7, 10, 0, 1, 0, null);
        Assert.That(under.StaffSeatsWarning, Is.False);
        Assert.That(under.StaffSeatsBlocked, Is.False);

        var warning = Status(8, 10, 0, 1, 0, null);
        Assert.That(warning.StaffSeatsWarning, Is.True);
        Assert.That(warning.StaffSeatsBlocked, Is.False);

        var blocked = Status(10, 10, 0, 1, 0, null);
        Assert.That(blocked.StaffSeatsBlocked, Is.True);
        Assert.That(blocked.StaffSeatsWarning, Is.False);   // blocked, not merely a warning
    }

    [Test]
    public void A_zero_seat_limit_never_warns_or_blocks_on_its_own_used_count()
    {
        // Custom plans start at 0/0 until a platform admin sets real numbers; that must not read as "always blocked".
        var status = Status(0, 0, 0, 0, 0, null);
        Assert.That(status.StaffSeatsBlocked, Is.False);
        Assert.That(status.StudentSeatsBlocked, Is.False);
    }

    [Test]
    public void No_exam_allowance_means_never_blocked_or_warned() =>
        Assert.That(Status(0, 10, 0, 10, 1_000_000, null).ExamsBlocked, Is.False);

    [Test]
    public void An_expired_trial_blocks_exams_even_with_allowance_left() =>
        Assert.That(Status(0, 3, 0, 25, 1, 50, trial: true, trialExpired: true).ExamsBlocked, Is.True);

    [Test]
    public void A_live_trial_is_not_expired() =>
        Assert.That(Status(0, 3, 0, 25, 1, 50, trial: true, trialExpired: false).TrialExpired, Is.False);
}

/// <summary>Branded email templates (GAD-T310): rendered content, and that nothing from exam titles or notes injects HTML. Pure, no database.</summary>
[TestFixture]
public class EmailTemplatesTests
{
    [Test]
    public void Every_kind_renders_a_non_empty_subject_and_html()
    {
        foreach (var kind in EmailKinds.All)
        {
            var rendered = EmailTemplates.Render(kind, new EmailData("Acme University", "Alex"));
            Assert.That(rendered.Subject, Is.Not.Empty, kind);
            Assert.That(rendered.Html, Does.Contain("<!doctype html>"), kind);
            Assert.That(rendered.Text, Does.Contain("Acme University"), kind);
        }
    }

    [Test]
    public void An_exam_title_with_markup_is_escaped_in_the_html_body()
    {
        var rendered = EmailTemplates.Render(EmailKinds.ExamAssigned, new EmailData("Acme", "Alex", ExamTitle: "<script>alert(1)</script>"));
        Assert.That(rendered.Html, Does.Not.Contain("<script>"));
        Assert.That(rendered.Html, Does.Contain("&lt;script&gt;"));
    }

    [Test]
    public void ResultReleased_includes_the_score_when_given()
    {
        var rendered = EmailTemplates.Render(EmailKinds.ResultReleased, new EmailData("Acme", "Alex", ExamTitle: "Midterm", Score: "91% (A)"));
        Assert.That(rendered.Html, Does.Contain("91% (A)"));
    }

    [Test]
    public void AppealDecided_includes_the_resolution_text()
    {
        var rendered = EmailTemplates.Render(EmailKinds.AppealDecided, new EmailData("Acme", "Alex", ExamTitle: "Midterm", AppealOutcome: "Upheld", AppealResolution: "The transcript was re-checked."));
        Assert.That(rendered.Html, Does.Contain("Upheld"));
        Assert.That(rendered.Html, Does.Contain("The transcript was re-checked."));
    }

    [Test]
    public void Describe_names_are_plain_English_for_every_kind()
    {
        foreach (var kind in EmailKinds.All) Assert.That(EmailKinds.Describe(kind), Is.Not.EqualTo(kind));
    }

    [Test]
    public void An_organization_name_is_encoded_once_in_the_lead_and_not_at_all_in_the_plain_text()
    {
        // GAD-T479: the lead used to be encoded twice, so "Smith & Jones" read "Smith &amp; Jones" in the email.
        var rendered = EmailTemplates.Render(EmailKinds.ExamAssigned, new EmailData("Smith & Jones", "Alex", ExamTitle: "Midterm"));
        Assert.That(rendered.Html, Does.Contain("Smith &amp; Jones assigned you an exam."));
        Assert.That(rendered.Html, Does.Not.Contain("&amp;amp;"));
        Assert.That(rendered.Text, Does.Contain("Smith & Jones assigned you an exam."));
    }

    [TestCase("/my-exams", "https://gadfly.example/", "https://gadfly.example/my-exams")]
    [TestCase("/my-results", "https://gadfly.example", "https://gadfly.example/my-results")]
    [TestCase("https://lms.example/x", null, "https://lms.example/x")]
    [TestCase("/my-exams", null, null)]
    [TestCase("//evil.example/x", "https://gadfly.example", null)]
    [TestCase("javascript:alert(1)", "https://gadfly.example", null)]
    [TestCase(null, "https://gadfly.example", null)]
    public void An_action_link_is_made_absolute_or_dropped(string? url, string? publicBase, string? expected)
    {
        // GAD-T479: callers stage app paths, which are dead links in a mail client.
        Assert.That(EmailTemplates.AbsoluteActionUrl(url, publicBase), Is.EqualTo(expected));
    }
}

/// <summary>When a notification email may reach real SMTP (GAD-T310/GAD-T374): never unconfigured, never in mock mode unless asked. Pure.</summary>
[TestFixture]
public class EmailDeliveryGateTests
{
    private sealed class Panel(bool mock) : Gadfly.Socrates.IPanelInfo
    {
        public bool IsMock => mock;
        public string Mode => mock ? Gadfly.Socrates.PanelModes.Mock : Gadfly.Socrates.PanelModes.Live;
        public string Reason => "test";
    }

    private static readonly MindAttic.Authentication.Services.AuthEmailStatus Configured =
        new(new MindAttic.Authentication.Services.AuthEmailSettings("smtp.example.org", 587, "u", "p", "noreply@example.org"), []);
    private static readonly MindAttic.Authentication.Services.AuthEmailStatus Unconfigured = new(null, []);

    private static Microsoft.Extensions.Configuration.IConfiguration Config(bool sendInMock = false) =>
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(sendInMock ? new Dictionary<string, string?> { [EmailDeliveryGate.SendInMockModeSetting] = "true" } : [])
            .Build();

    [Test]
    public void Live_with_SMTP_may_send() =>
        Assert.That(EmailDeliveryGate.BlockedReason(Configured, new Panel(false), Config()), Is.Null);

    [Test]
    public void No_SMTP_never_sends_even_live() =>
        Assert.That(EmailDeliveryGate.BlockedReason(Unconfigured, new Panel(false), Config()), Does.Contain("not configured"));

    [Test]
    public void Mock_mode_never_sends_even_with_SMTP() =>
        Assert.That(EmailDeliveryGate.BlockedReason(Configured, new Panel(true), Config()), Does.Contain("mock mode"));

    [Test]
    public void Mock_mode_sends_only_when_explicitly_allowed() =>
        Assert.That(EmailDeliveryGate.BlockedReason(Configured, new Panel(true), Config(sendInMock: true)), Is.Null);
}
