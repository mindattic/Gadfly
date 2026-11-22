using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Gadfly.Server.Sessions;
using Gadfly.Server.Student;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Webcam integrity events: allow-list, bounds, severity, rate limit and the consent wording (GAD-T57). Pure: no database, no camera.</summary>
[TestFixture]
public class WebcamIntegrityTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-20T10:00:00Z");
    private static readonly DateTimeOffset End = Start.AddMinutes(30);

    private static HubIntegrityEvent Event(string type, int startSecond = 60, int? endSecond = 90, double confidence = 0.9, string summary = "x", string source = "webcam") =>
        new(source, type, Start.AddSeconds(startSecond), endSecond is { } e ? Start.AddSeconds(e) : null, confidence, summary);

    [TestCase("face-absent")]
    [TestCase("multiple-faces")]
    [TestCase("identity-drift")]
    [TestCase("gaze-off-screen")]
    [TestCase("thinking-glance")]
    [TestCase("reading-pattern")]
    public void The_librarys_event_types_are_accepted(string type) =>
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event(type), Start, End), Is.Not.Null);

    /// <summary>
    /// GAD-T446: no verified source shows that looking away is a sign of thinking, so a look-away note is written by the server,
    /// names its many causes and never says the student was thinking, whatever the browser's library sent.
    /// </summary>
    [TestCase("thinking-glance")]
    [TestCase("gaze-off-screen")]
    [TestCase("examiner-look-away")]
    [TestCase("examiner-reading")]
    public void A_look_away_note_never_claims_the_student_was_thinking(string type)
    {
        var c = WebcamIntegrityRecorder.Clean.From(Event(type, summary: "a short glance of the kind people make while thinking"), Start, End)!;
        Assert.That(c.Summary, Does.Not.Contain("think").IgnoreCase);
        Assert.That(c.Summary, Does.Contain("30 s"));
    }

    /// <summary>GAD-T446: the reviewer's reading of any gaze event names causes, never "thinking", and calls nothing the most meaningful signal.</summary>
    [TestCase("looked-away")]
    [TestCase("gaze-away")]
    [TestCase("thinking-glance")]
    [TestCase("gaze-off-screen")]
    [TestCase("offscreen-reading")]
    [TestCase("reading-pattern")]
    [TestCase("examiner-look-away")]
    [TestCase("examiner-reading")]
    [TestCase("some-new-gaze-type")]
    public void Reviewer_gaze_wording_claims_only_what_the_evidence_supports(string type)
    {
        var (label, explanation, _) = Gadfly.Server.Review.IntegrityExplanations.For(type, "webcam");
        Assert.That(label + " " + explanation, Does.Not.Contain("thinking").IgnoreCase);
        Assert.That(explanation, Does.Not.Contain("most meaningful").IgnoreCase);
    }

    [TestCase("low-light")]
    [TestCase("emotion-stress")]
    [TestCase("deception")]
    [TestCase("")]
    [TestCase("FACE-ABSENT")]
    public void Anything_else_is_dropped(string type) =>
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event(type), Start, End), Is.Null);

    [Test]
    public void Only_the_webcam_source_is_taken_by_this_recorder() =>
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event("face-absent", source: "audio"), Start, End), Is.Null);

    [Test]
    public void Confidence_is_capped_low_and_severity_is_decided_by_the_server_never_above_Low()
    {
        foreach (var type in WebcamIntegrityRecorder.Types)
        {
            var c = WebcamIntegrityRecorder.Clean.From(Event(type, confidence: 1, endSecond: 60 + 3600), Start, End.AddHours(2))!;
            Assert.That(c.Confidence, Is.LessThanOrEqualTo(WebcamIntegrityRecorder.MaxConfidence), type);
            Assert.That(c.Severity, Is.LessThanOrEqualTo(IntegritySeverity.Low), type);
        }
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event("thinking-glance", confidence: 0.9), Start, End)!.Severity, Is.EqualTo(IntegritySeverity.Info));
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event("thinking-glance", confidence: 0.9), Start, End)!.Confidence, Is.LessThanOrEqualTo(0.3));
        Assert.That(WebcamIntegrityRecorder.SeverityOf("face-absent", TimeSpan.FromSeconds(5)), Is.EqualTo(IntegritySeverity.Info));
        Assert.That(WebcamIntegrityRecorder.SeverityOf("face-absent", TimeSpan.FromSeconds(40)), Is.EqualTo(IntegritySeverity.Low));
        Assert.That(WebcamIntegrityRecorder.SeverityOf("reading-pattern", TimeSpan.Zero), Is.EqualTo(IntegritySeverity.Low));
    }

    [Test]
    public void Camera_unavailable_is_neutral_and_its_text_is_fixed_by_the_server()
    {
        var denied = WebcamIntegrityRecorder.Clean.From(Event("camera-unavailable", summary: "denied", endSecond: null), Start, End)!;
        Assert.That(denied.Severity, Is.EqualTo(IntegritySeverity.Info));
        Assert.That(denied.Summary, Does.Contain("did not allow the camera"));
        var rubbish = WebcamIntegrityRecorder.Clean.From(Event("camera-unavailable", summary: "<script>alert(1)</script> you are a cheat", endSecond: null), Start, End)!;
        Assert.That(rubbish.Summary, Is.EqualTo(WebcamIntegrityRecorder.UnavailableReasons["error"]));
    }

    [Test]
    public void Times_are_bounded_to_the_session_and_text_is_clean_and_short()
    {
        var before = WebcamIntegrityRecorder.Clean.From(Event("face-absent", startSecond: -600, endSecond: -300), Start, End);
        Assert.That(before, Is.Null, "wholly before the session");
        var after = WebcamIntegrityRecorder.Clean.From(Event("face-absent", startSecond: 3600, endSecond: 3700), Start, End);
        Assert.That(after, Is.Null, "wholly after the session");
        var straddle = WebcamIntegrityRecorder.Clean.From(Event("face-absent", startSecond: -10, endSecond: 20), Start, End)!;
        Assert.That(straddle.Start, Is.EqualTo(Start));
        var backwards = WebcamIntegrityRecorder.Clean.From(Event("face-absent", startSecond: 100, endSecond: 50), Start, End)!;
        Assert.That(backwards.End, Is.Null);
        var text = WebcamIntegrityRecorder.Clean.From(Event("face-absent", summary: "look data:image/png;base64,AAAA\u0007\u0000 here " + new string('a', 2000)), Start, End)!;
        Assert.That(text.Summary, Does.Not.Contain("data:"));
        Assert.That(text.Summary.Length, Is.LessThanOrEqualTo(WebcamIntegrityRecorder.MaxSummary));
        Assert.That(text.Summary.Any(char.IsControl), Is.False);
        Assert.That(WebcamIntegrityRecorder.Clean.From(Event("face-absent", confidence: double.NaN), Start, End)!.Confidence, Is.EqualTo(0));
    }

    [Test]
    public void The_rate_limit_allows_a_burst_then_refuses_and_recovers_after_the_window()
    {
        var session = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-11-20T10:00:00Z");
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now, 30), Is.EqualTo(30));
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now, 30), Is.EqualTo(WebcamIntegrityRecorder.MaxPerWindow - 30));
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now.AddSeconds(30), 5), Is.EqualTo(0));
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now.Add(WebcamIntegrityRecorder.RateWindow).AddSeconds(1), 5), Is.EqualTo(5));
        Assert.That(WebcamIntegrityRecorder.TakeRate(Guid.NewGuid(), now, 5), Is.EqualTo(5), "another session has its own allowance");
    }

    [Test]
    public void The_notice_describes_the_camera_only_for_Strict_exams_and_says_what_is_and_isnt_collected()
    {
        var strict = string.Join("\n", ConsentNotice.For("Org", "Exam", 90, ExamMode.Text, IntegrityLevel.Strict).Paragraphs);
        Assert.That(strict, Does.Contain("this exam uses your camera"));
        Assert.That(strict, Does.Contain("No video, photo, screenshot"));
        Assert.That(strict, Does.Contain("emotions, mood or honesty"));
        Assert.That(strict, Does.Contain("never an automatic fail"));
        Assert.That(strict, Does.Contain("The camera must be on to take this exam"));
        foreach (var level in new[] { IntegrityLevel.Off, IntegrityLevel.Standard })
            Assert.That(string.Join("\n", ConsentNotice.For("Org", "Exam", 90, ExamMode.Text, level).Paragraphs), Does.Not.Contain("uses your camera"));
        Assert.That(ConsentNotice.For("Org", "Exam", 90, ExamMode.Text, IntegrityLevel.Strict).Sha256,
            Is.Not.EqualTo(ConsentNotice.For("Org", "Exam", 90, ExamMode.Text, IntegrityLevel.Standard).Sha256));
    }
}
