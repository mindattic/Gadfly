using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Gadfly.Server.Sessions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Partial updates of an ongoing webcam episode fold into ONE IntegrityEvent (D21, GAD-T171): validation of a
/// partial, the upsert rules (grow, never duplicate, never reopen), the separate rate bucket and the caps. Pure.
/// </summary>
[TestFixture]
public class WebcamEpisodeTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-20T10:00:00Z");
    private static readonly DateTimeOffset End = Start.AddMinutes(60);

    private static HubIntegrityEvent Partial(string type = "face-absent", int startSecond = 120, int endSecond = 180, string? id = null, double confidence = 0.9) =>
        new("webcam", type, Start.AddSeconds(startSecond), Start.AddSeconds(endSecond), confidence, "client text", id, Ongoing: true);

    private static HubIntegrityEvent Final(string type = "face-absent", int startSecond = 120, int endSecond = 600, string? id = null) =>
        new("webcam", type, Start.AddSeconds(startSecond), Start.AddSeconds(endSecond), 0.9, "No face was in view for 8 min.", id);

    private static WebcamIntegrityRecorder.Clean Clean(HubIntegrityEvent e) => WebcamIntegrityRecorder.Clean.From(e, Start, End)!;

    /// <summary>What the recorder does for the first update of an episode: a new row from the cleaned event.</summary>
    private static IntegrityEvent NewRow(WebcamIntegrityRecorder.Clean c) => new()
    {
        Source = "webcam", Type = c.Type, StartedAt = c.Start, EndedAt = c.End, Confidence = c.Confidence, Severity = c.Severity,
        Summary = c.Summary, DataJson = WebcamIntegrityRecorder.EpisodeData.Json(c.EpisodeKey!, c.Ongoing),
    };

    [Test]
    public void A_partial_is_ongoing_has_a_key_and_its_text_is_written_by_the_server()
    {
        var c = Clean(Partial(id: "face-absent@1"));
        Assert.That(c.Ongoing, Is.True);
        Assert.That(c.EpisodeKey, Is.EqualTo("face-absent@1"));
        Assert.That(c.End, Is.EqualTo(Start.AddSeconds(180)));
        Assert.That(c.Summary, Does.Contain("so far"));
        Assert.That(c.Summary, Does.Not.Contain("client text"));
    }

    [Test]
    public void Without_an_id_the_key_is_type_and_start_so_a_closing_event_still_matches_its_partials()
    {
        var p = Clean(Partial());
        var f = Clean(Final());
        Assert.That(p.EpisodeKey, Is.EqualTo(f.EpisodeKey));
        Assert.That(p.EpisodeKey, Is.EqualTo($"face-absent@{Start.AddSeconds(120).ToUnixTimeMilliseconds()}"));
    }

    [TestCase("")]
    [TestCase("has spaces")]
    [TestCase("<script>")]
    public void An_unusable_client_id_is_replaced_by_the_derived_key(string id) =>
        Assert.That(Clean(Partial(id: id)).EpisodeKey, Does.StartWith("face-absent@"));

    [Test]
    public void An_overlong_client_id_is_replaced_by_the_derived_key() =>
        Assert.That(Clean(Partial(id: new string('a', WebcamIntegrityRecorder.MaxEpisodeKey + 1))).EpisodeKey, Does.StartWith("face-absent@"));

    [TestCase("camera-unavailable")]
    [TestCase("thinking-glance")]
    [TestCase("emotion-stress")]
    public void Only_span_types_may_be_ongoing(string type) =>
        Assert.That(WebcamIntegrityRecorder.Clean.From(Partial(type), Start, End), Is.Null);

    [Test]
    public void A_partial_without_an_end_is_dropped() =>
        Assert.That(WebcamIntegrityRecorder.Clean.From(new HubIntegrityEvent("webcam", "face-absent", Start.AddSeconds(10), null, 0.4, "", "k", true), Start, End), Is.Null);

    [Test]
    public void A_partial_keeps_the_confidence_cap_and_severity_ceiling()
    {
        foreach (var type in WebcamIntegrityRecorder.PartialTypes)
        {
            var c = Clean(Partial(type, 0, 3600, confidence: 1));
            Assert.That(c.Confidence, Is.LessThanOrEqualTo(WebcamIntegrityRecorder.MaxConfidence), type);
            Assert.That(c.Severity, Is.LessThanOrEqualTo(IntegritySeverity.Low), type);
        }
        Assert.That(Clean(Partial(startSecond: 100, endSecond: 105)).Severity, Is.EqualTo(IntegritySeverity.Info), "a few seconds so far");
    }

    [Test]
    public void A_partial_end_is_clamped_to_the_session_window()
    {
        var c = WebcamIntegrityRecorder.Clean.From(Partial(endSecond: 9999), Start, Start.AddMinutes(10))!;
        Assert.That(c.End, Is.EqualTo(Start.AddMinutes(10)));
    }

    [Test]
    public void Later_partials_grow_the_same_row_and_never_shrink_it()
    {
        var row = NewRow(Clean(Partial(endSecond: 180)));
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Partial(endSecond: 240))), Is.True);
        Assert.That(row.EndedAt, Is.EqualTo(Start.AddSeconds(240)));
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Partial(endSecond: 200))), Is.False, "an out-of-order partial");
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Partial(endSecond: 240))), Is.False, "a retried partial");
        Assert.That(row.EndedAt, Is.EqualTo(Start.AddSeconds(240)));
        Assert.That(WebcamIntegrityRecorder.EpisodeData.Parse(row.DataJson)!.Ongoing, Is.True);
    }

    [Test]
    public void The_closing_event_sets_the_final_end_and_closes_the_row()
    {
        var row = NewRow(Clean(Partial(endSecond: 180)));
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Final(endSecond: 600))), Is.True);
        Assert.That(row.EndedAt, Is.EqualTo(Start.AddSeconds(600)));
        Assert.That(row.Summary, Is.EqualTo("No face was in view for 8 min."));
        Assert.That(WebcamIntegrityRecorder.EpisodeData.Parse(row.DataJson)!.Ongoing, Is.False);
        Assert.That(WebcamIntegrityRecorder.EpisodeData.Parse(row.DataJson)!.Episode, Is.EqualTo(Clean(Final()).EpisodeKey));
    }

    [Test]
    public void A_late_partial_never_reopens_a_closed_episode_and_a_retried_close_changes_nothing()
    {
        var row = NewRow(Clean(Partial(endSecond: 180)));
        WebcamIntegrityRecorder.Apply(row, Clean(Final(endSecond: 600)));
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Partial(endSecond: 700))), Is.False);
        Assert.That(WebcamIntegrityRecorder.Apply(row, Clean(Final(endSecond: 600))), Is.False);
        Assert.That(row.EndedAt, Is.EqualTo(Start.AddSeconds(600)));
        Assert.That(WebcamIntegrityRecorder.EpisodeData.Parse(row.DataJson)!.Ongoing, Is.False);
    }

    [Test]
    public void A_row_stored_before_partial_updates_existed_takes_a_closing_retry_without_change()
    {
        var f = Clean(Final());
        var old = new IntegrityEvent { Source = "webcam", Type = f.Type, StartedAt = f.Start, EndedAt = f.End, Summary = f.Summary, Confidence = f.Confidence, Severity = f.Severity };
        Assert.That(WebcamIntegrityRecorder.Apply(old, f), Is.False);
        Assert.That(old.DataJson, Is.Null);
    }

    [Test]
    public void Closing_confidence_and_severity_replace_the_partial_values_within_the_caps()
    {
        var row = NewRow(Clean(Partial(startSecond: 100, endSecond: 105)));
        Assert.That(row.Severity, Is.EqualTo(IntegritySeverity.Info));
        WebcamIntegrityRecorder.Apply(row, Clean(Final(startSecond: 100, endSecond: 900)));
        Assert.That(row.Severity, Is.EqualTo(IntegritySeverity.Low));
        Assert.That(row.Confidence, Is.LessThanOrEqualTo(WebcamIntegrityRecorder.MaxConfidence));
    }

    [Test]
    public void Partial_updates_have_their_own_rate_bucket_and_cannot_crowd_out_real_events()
    {
        // The public TakeRate is the events bucket; partial updates spend a different one (checked through RecordAsync's split),
        // so exhausting the events bucket leaves the partial limit a separate, smaller number.
        Assert.That(WebcamIntegrityRecorder.MaxPartialPerWindow, Is.LessThan(WebcamIntegrityRecorder.MaxPerWindow));
        var session = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now, WebcamIntegrityRecorder.MaxPerWindow), Is.EqualTo(WebcamIntegrityRecorder.MaxPerWindow));
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now, 1), Is.EqualTo(0));
        Assert.That(WebcamIntegrityRecorder.TakeRate(session, now + WebcamIntegrityRecorder.RateWindow + TimeSpan.FromSeconds(1), 5), Is.EqualTo(5));
    }

    [Test]
    public void The_partial_summary_is_plain_and_never_infers_anything()
    {
        foreach (var type in WebcamIntegrityRecorder.PartialTypes)
        {
            var text = WebcamIntegrityRecorder.PartialSummary(type, TimeSpan.FromSeconds(95));
            Assert.That(text, Does.Contain("1 min 35 s"), type);
            Assert.That(text.ToLowerInvariant(), Does.Not.Contain("cheat").And.Not.Contain("dishonest").And.Not.Contain("emotion"), type);
        }
    }
}
