using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Gadfly.Server.Sessions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The per-session eye-tracking quality sent with every eye-contact episode (GAD-T305): codes parsed and bounded, the reviewer's
/// sentence written by the server, the numbers kept in DataJson, poor tracking kept at Info, and older pages' plain codes still
/// understood. Pure.
/// </summary>
[TestFixture]
public class GazeQualityTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-20T10:00:00Z");
    private static readonly DateTimeOffset End = Start.AddMinutes(60);

    private static WebcamIntegrityRecorder.Clean Clean(string type, string summary, int seconds = 12) =>
        WebcamIntegrityRecorder.Clean.From(new HubIntegrityEvent("webcam", type, Start.AddSeconds(60), Start.AddSeconds(60 + seconds), 0.4, summary), Start, End)!;

    [Test]
    public void Codes_parse_into_a_bounded_quality()
    {
        var q = GazeQuality.Parse("remarked;q:78;m:cal;a:3.4");
        Assert.That(q, Is.EqualTo(new GazeQuality(78, true, 3.4)));
        Assert.That(q!.Band, Is.EqualTo("good"));
        Assert.That(GazeQuality.Parse("q:250;m:cal;a:999")!.Score, Is.EqualTo(100));
        Assert.That(GazeQuality.Parse("q:250;m:cal;a:999")!.AccuracyDeg, Is.EqualTo(GazeQuality.MaxAccuracyDeg));
        Assert.That(GazeQuality.Parse("q:45;m:head;a:2.0"), Is.EqualTo(new GazeQuality(45, false, null)), "accuracy only means something when calibrated");
        Assert.That(GazeQuality.Parse("remarked"), Is.Null, "an older page sends no quality");
        Assert.That(GazeQuality.Parse("q:-5"), Is.Null);
        Assert.That(GazeQuality.Parse("q:<script>"), Is.Null);
    }

    [TestCase(70, "good")]
    [TestCase(69, "fair")]
    [TestCase(40, "fair")]
    [TestCase(39, "poor")]
    public void Bands(int score, string band) => Assert.That(new GazeQuality(score, false, null).Band, Is.EqualTo(band));

    [Test]
    public void The_reviewer_sees_the_quality_beside_the_note_and_DataJson_keeps_it()
    {
        var c = Clean("examiner-look-away", "remarked;q:82;m:cal;a:3.1");
        Assert.That(c.Summary, Does.Contain(WebcamIntegrityRecorder.SpokenReminderSaid), "GAD-T557/T561: a gentle spoken reminder, not \"asked out loud\"");
        Assert.That(c.Summary, Does.Contain("Eye-tracking quality this session: good (82/100; set up to the student's screen, about 3.1° accuracy)."));
        Assert.That(c.Severity, Is.EqualTo(IntegritySeverity.Low), "12 s look-away with good tracking stays Low");
        Assert.That(GazeQuality.FromJson(c.Data), Is.EqualTo(new GazeQuality(82, true, 3.1)));
        Assert.That(WebcamIntegrityRecorder.EpisodeData.Parse(c.Data), Is.Null, "quality data is never mistaken for an episode key");
    }

    [Test]
    public void Poor_tracking_keeps_a_look_away_at_Info_and_says_so()
    {
        var c = Clean("examiner-look-away", "q:25;m:head");
        Assert.That(c.Severity, Is.EqualTo(IntegritySeverity.Info));
        Assert.That(c.Summary, Does.Contain("poor (25/100; head and eye direction only"));
        Assert.That(c.Summary, Does.Contain("very weak"));
        // Face presence does not depend on gaze quality.
        Assert.That(Clean("examiner-second-face", "q:25;m:head").Severity, Is.EqualTo(IntegritySeverity.Low));
    }

    [Test]
    public void The_fade_peak_is_read_among_other_codes_and_alone()
    {
        Assert.That(WebcamIntegrityRecorder.FadePeakOf("peak:64;q:70;m:cal;a:3.0"), Is.EqualTo(64));
        Assert.That(WebcamIntegrityRecorder.FadePeakOf("peak:55"), Is.EqualTo(55));
        Assert.That(Clean("examiner-fade", "peak:64;q:70;m:cal;a:3.0").Summary, Does.Contain("at most 64% faded"));
    }

    [Test]
    public void Older_pages_codes_still_work()
    {
        var c = Clean("examiner-look-away", "remarked");
        // GAD-T557/T561: a look-away reminder is a short, gentle spoken one at a pause, no longer "asked out loud for eye contact".
        Assert.That(c.Summary, Does.Contain(WebcamIntegrityRecorder.SpokenReminderSaid));
        Assert.That(WebcamIntegrityRecorder.SaysExaminerSpoke(c.Summary), Is.True, "the gaze analytics still count it as a remark");
        Assert.That(WebcamIntegrityRecorder.SaysExaminerSpoke("Looked off the screen for 9 s while answering. The examiner asked out loud for eye contact."), Is.True, "rows stored before GAD-T557 still count");
        Assert.That(c.Summary, Does.Not.Contain("Eye-tracking quality"));
        Assert.That(c.Data, Is.Null);
    }

    [Test]
    public void The_accommodation_note_keeps_the_quality()
    {
        var c = WebcamIntegrityRecorder.Accommodated(Clean("examiner-look-away", "q:80;m:cal;a:3.0"));
        Assert.That(c.Summary, Does.Contain("Eye-tracking quality"));
        Assert.That(c.Summary, Does.EndWith(WebcamIntegrityRecorder.AccommodationNote));
        Assert.That(c.Data, Is.Not.Null);
    }

    [Test]
    public void A_closing_retry_keeps_the_quality_in_DataJson()
    {
        var c = Clean("examiner-look-away", "q:60;m:cal;a:4.0");
        var row = new IntegrityEvent { Source = "webcam", Type = c.Type, StartedAt = c.Start, EndedAt = c.End, Summary = "old", DataJson = null };
        Assert.That(WebcamIntegrityRecorder.Apply(row, c), Is.True);
        Assert.That(GazeQuality.FromJson(row.DataJson)!.Score, Is.EqualTo(60));
    }
}
