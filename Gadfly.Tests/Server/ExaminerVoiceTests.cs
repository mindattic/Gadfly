using Gadfly.Server.Voice;
using Gadfly.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// GAD-T475: the examiner speaks with the exam's voice, else the organization's default, else the platform's, chosen on the
/// server and kept on the student's grant; mock mode never passes a voice on; a malformed id falls back instead of being sent.
/// </summary>
[TestFixture]
public class ExaminerVoiceTests
{
    private const string ExamVoice = "21m00Tcm4TlvDq8ikWAM";
    private const string OrgVoice = "AZnzlk1XvdvUeBnXmlld";

    [Test]
    public void Exam_voice_wins_then_organization_then_platform()
    {
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, ExamVoice, OrgVoice), Is.EqualTo(ExamVoice));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, null, OrgVoice), Is.EqualTo(OrgVoice));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, "  ", null), Is.Null, "the platform voice");
    }

    [Test]
    public void Mock_and_off_never_pass_a_voice_on()
    {
        Assert.That(ExaminerVoice.Choose(VoiceMode.Mock, ExamVoice, OrgVoice), Is.Null);
        Assert.That(ExaminerVoice.Choose(VoiceMode.Off, ExamVoice, OrgVoice), Is.Null);
    }

    [Test]
    public void A_malformed_id_is_skipped_not_sent()
    {
        Assert.That(ExaminerVoice.IsWellFormed("Rachel the nice one"), Is.False);
        Assert.That(ExaminerVoice.IsWellFormed("short"), Is.False);
        Assert.That(ExaminerVoice.IsWellFormed(ExamVoice), Is.True);
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, "../../v1/voices", OrgVoice), Is.EqualTo(OrgVoice));
    }

    [Test]
    public void The_grant_that_offered_a_text_decides_its_voice()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-11-22T10:00:00Z"));
        var grants = new VoiceGrants(clock);
        Guid student = Guid.NewGuid(), org = Guid.NewGuid(), session = Guid.NewGuid(), other = Guid.NewGuid();
        grants.Arm(student, org, session);
        grants.Offer(student, session, "Why?");
        Assert.That(grants.VoiceFor(student, "Why?"), Is.Null, "no voice set: the platform voice");

        grants.SetVoice(student, session, ExamVoice);
        Assert.That(grants.VoiceFor(student, "Why?"), Is.EqualTo(ExamVoice));
        Assert.That(grants.VoiceFor(student, "Something never offered"), Is.Null);
        Assert.That(grants.VoiceFor(other, "Why?"), Is.Null, "another student's grant is never used");

        grants.SetVoice(Guid.NewGuid(), session, OrgVoice);   // not armed: ignored
        Assert.That(grants.VoiceFor(student, "Why?"), Is.EqualTo(ExamVoice));

        clock.Advance(VoiceGrants.ArmedFor + TimeSpan.FromMinutes(1));
        Assert.That(grants.VoiceFor(student, "Why?"), Is.Null, "an expired grant chooses nothing");
    }

    [Test]
    public void Warm_phrases_are_tracked_per_voice()
    {
        var settings = VoiceSettings.Disabled with { Mode = VoiceMode.Mock };
        var warmup = new ExaminerSpeechWarmup(new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(), settings,
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExaminerSpeechWarmup>.Instance);
        var now = DateTimeOffset.UtcNow;
        Assert.That(warmup.Due(["Go on."], now), Is.EqualTo(new[] { "Go on." }));
        Assert.That(warmup.Due(["Go on."], now), Is.Empty, "the platform voice already has it");
        Assert.That(warmup.Due(["Go on."], now, ExamVoice), Is.EqualTo(new[] { "Go on." }), "a different voice is a different recording");
    }
}
