using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Browser integrity signals (GAD-T56; under GAD-T399) stored on the test database: only the session's own student, only while it
/// runs (or just after, for a span that began before the end), only when the exam watches; unknown kinds and times outside the
/// session are dropped; consecutive identical signals merge; severity is conservative and never High (a reviewer note, never a
/// verdict); and volume is capped per session.
/// </summary>
[TestFixture]
public class IntegrityRecorderTests : ServerTest
{
    private sealed record World(Guid Org, Guid StudentId, Guid SessionId, DateTimeOffset Now);

    private async Task<World> RunningAsync(IntegrityLevel level = IntegrityLevel.Standard, SessionState state = SessionState.Running, DateTimeOffset? endedAt = null,
        bool withOpenTurn = false)
    {
        var now = DateTimeOffset.UtcNow;
        Clock.Now = now;
        var w = await Seed.ExamAsync(integrity: level);
        await using var db = OrgDb(w.Org);
        var session = new ExamSession
        {
            OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, State = state,
            CreatedAt = now.AddMinutes(-20), ConsentRecordedAt = now.AddMinutes(-15), StartedAt = now.AddMinutes(-15), EndedAt = endedAt,
            TranscriptJson = "{}", LedgerJson = "{}", ClientInfo = "browser",
        };
        db.ExamSessions.Add(session);
        if (withOpenTurn)
            db.SessionTurns.Add(new SessionTurn
            {
                OrganizationId = w.Org, SessionId = session.Id, Index = 0, ConceptKey = "photosynthesis", Move = "Elicit",
                Question = "What is photosynthesis?", AskedAt = now.AddMinutes(-5), WordsJson = "[]", SpeechMetricsJson = "{}",
            });
        await db.SaveChangesAsync();
        return new World(w.Org, w.StudentId, session.Id, now);
    }

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    private static BrowserSignal Span(string type, DateTimeOffset start, TimeSpan length) => new() { Type = type, At = Ms(start), EndAt = Ms(start + length) };

    private static BrowserSignal Instant(string type, DateTimeOffset at, int? chars = null) => new() { Type = type, At = Ms(at), Chars = chars };

    private async Task<bool> RecordAsync(World w, params BrowserSignal[] signals) => await RecordAsync(w, w.StudentId, signals);

    private async Task<bool> RecordAsync(World w, Guid student, params BrowserSignal[] signals)
    {
        await using var db = OrgDb(w.Org);
        var recorder = new IntegrityRecorder(db, Clock, NullLogger<IntegrityRecorder>.Instance);
        return await recorder.RecordAsync(w.Org, student, w.SessionId, new BrowserSignalBatch { SentAt = Ms(w.Now), Events = [.. signals] },
            CancellationToken.None, clockSkew: TimeSpan.Zero);
    }

    private static async Task<List<IntegrityEvent>> RowsAsync(World w)
    {
        await using var db = SystemDb();
        return await db.IntegrityEvents.AsNoTracking().Where(e => e.SessionId == w.SessionId).OrderBy(e => e.StartedAt).ToListAsync();
    }

    [Test]
    public async Task A_hidden_tab_is_stored_as_a_low_confidence_reviewer_note_never_a_verdict()
    {
        var w = await RunningAsync();
        Assert.That(await RecordAsync(w, Span("tab-hidden", w.Now.AddMinutes(-3), TimeSpan.FromSeconds(30))), Is.True);
        var row = (await RowsAsync(w)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Source, Is.EqualTo(IntegrityRecorder.Source));
            Assert.That(row.Type, Is.EqualTo("tab-hidden"));
            Assert.That(row.Severity, Is.EqualTo(IntegritySeverity.Low));
            Assert.That(row.Confidence, Is.EqualTo(0.95));
            Assert.That(row.EndedAt!.Value - row.StartedAt, Is.EqualTo(TimeSpan.FromSeconds(30)).Within(TimeSpan.FromMilliseconds(5)));
            Assert.That(row.Summary, Is.EqualTo("The exam tab was hidden for 30 s between questions."));
        });
    }

    [Test]
    public async Task A_signal_during_a_question_names_that_question()
    {
        var w = await RunningAsync(withOpenTurn: true);
        await RecordAsync(w, Instant("paste", w.Now.AddMinutes(-1), chars: 12));
        var row = (await RowsAsync(w)).Single();
        Assert.That(row.TurnId, Is.Not.Null);
        Assert.That(row.Summary, Is.EqualTo("Text was pasted into the answer box while answering question 1: 12 characters."));
        Assert.That(row.Severity, Is.EqualTo(IntegritySeverity.Info));
    }

    [Test]
    public async Task Consecutive_identical_spans_merge_into_one_row_with_a_count()
    {
        var w = await RunningAsync();
        var t = w.Now.AddMinutes(-4);
        await RecordAsync(w, Span("focus-lost", t, TimeSpan.FromSeconds(3)), Span("focus-lost", t.AddSeconds(4), TimeSpan.FromSeconds(3)));
        await RecordAsync(w, Span("focus-lost", t.AddSeconds(8), TimeSpan.FromSeconds(2)));   // a later batch still joins the stored row
        var row = (await RowsAsync(w)).Single();
        Assert.That(row.DataJson, Does.Contain("\"count\":3"));
        Assert.That(row.EndedAt!.Value, Is.EqualTo(t.AddSeconds(10)).Within(TimeSpan.FromMilliseconds(5)));
        Assert.That(row.Summary, Does.Contain("3 times close together"));
    }

    [Test]
    public async Task Different_kinds_are_separate_rows()
    {
        var w = await RunningAsync();
        var t = w.Now.AddMinutes(-4);
        await RecordAsync(w, Instant("paste", t, 5), Instant("copy-question", t.AddSeconds(1)), Instant("paste", t.AddSeconds(2), 5));
        Assert.That((await RowsAsync(w)).Select(r => r.Type), Is.EqualTo(new[] { "paste", "copy-question", "paste" }));
    }

    [Test]
    public async Task No_browser_signal_is_ever_High_however_much_there_is()
    {
        var w = await RunningAsync(IntegrityLevel.Strict);
        var t = w.Now.AddMinutes(-10);
        await RecordAsync(w,
            Span("tab-hidden", t, TimeSpan.FromMinutes(5)), Instant("paste", t.AddMinutes(6), 50_000), Span("devtools-open", t.AddMinutes(7), TimeSpan.FromMinutes(1)),
            Instant("fullscreen-exit", t.AddMinutes(8)), new BrowserSignal { Type = "extra-screen", At = Ms(t.AddMinutes(9)), Screens = 3, Method = "screen-details" });
        var rows = await RowsAsync(w);
        Assert.That(rows, Has.Count.EqualTo(5));
        Assert.That(rows.Any(r => r.Severity == IntegritySeverity.High), Is.False);
        Assert.That(rows.Single(r => r.Type == "paste").Severity, Is.EqualTo(IntegritySeverity.Medium));
        Assert.That(rows.Single(r => r.Type == "devtools-open").Confidence, Is.EqualTo(0.3), "a weak guess stays a weak guess");
    }

    [Test]
    public async Task Unknown_kinds_and_signals_outside_the_session_are_dropped()
    {
        var w = await RunningAsync();
        Assert.That(await RecordAsync(w,
            Instant("keystroke-logger", w.Now.AddMinutes(-1)),
            Instant("paste", w.Now.AddMinutes(-30), 10),             // before consent
            Instant("paste", w.Now.AddMinutes(10), 10),              // in the future
            new BrowserSignal { Type = "paste", At = 0 }), Is.True);
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task Another_students_signals_for_this_session_are_refused()
    {
        var w = await RunningAsync();
        Assert.That(await RecordAsync(w, Guid.NewGuid(), Instant("paste", w.Now.AddMinutes(-1), 10)), Is.False);
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task An_exam_with_integrity_off_records_nothing_and_tells_the_browser_to_stop()
    {
        var w = await RunningAsync(IntegrityLevel.Off);
        Assert.That(await RecordAsync(w, Instant("paste", w.Now.AddMinutes(-1), 10)), Is.False);
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task Before_the_exam_runs_the_browser_keeps_watching_but_nothing_is_stored()
    {
        var w = await RunningAsync(state: SessionState.ConsentGiven);
        Assert.That(await RecordAsync(w, Instant("paste", w.Now.AddMinutes(-1), 10)), Is.True);
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task Just_after_the_end_a_late_span_is_kept_but_the_browser_is_told_to_stop()
    {
        var w = await RunningAsync(state: SessionState.Ended, endedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert.That(await RecordAsync(w, Span("tab-hidden", w.Now.AddMinutes(-4), TimeSpan.FromMinutes(3))), Is.False);
        var row = (await RowsAsync(w)).Single();
        Assert.That(row.EndedAt, Is.LessThanOrEqualTo(w.Now.AddMinutes(-2).AddSeconds(1)), "clamped to the end of the session");
    }

    [Test]
    public async Task Long_after_the_end_nothing_is_accepted()
    {
        var w = await RunningAsync(state: SessionState.Ended, endedAt: DateTimeOffset.UtcNow - IntegrityRecorder.LateGrace - TimeSpan.FromMinutes(5));
        Assert.That(await RecordAsync(w, Instant("paste", w.Now.AddMinutes(-30), 10)), Is.False);
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task A_session_that_keeps_reporting_is_capped_with_one_plain_row_and_then_refused()
    {
        var w = await RunningAsync();
        var t = w.Now.AddMinutes(-14);
        string[] alternate = ["paste", "copy-question"];   // never consecutive identical, so never merged
        for (var batch = 0; batch < IntegrityRecorder.MaxEventsPerSession / IntegrityRecorder.MaxBatch; batch++)
        {
            var signals = Enumerable.Range(0, IntegrityRecorder.MaxBatch)
                .Select(i => Instant(alternate[i % 2], t.AddMilliseconds((batch * IntegrityRecorder.MaxBatch + i) * 1000), 1)).ToArray();
            await RecordAsync(w, signals);
        }
        var rows = await RowsAsync(w);
        Assert.That(rows, Has.Count.EqualTo(IntegrityRecorder.MaxEventsPerSession));
        var capped = rows.Single(r => r.Type == IntegrityRecorder.CappedType);
        Assert.That(capped.Severity, Is.EqualTo(IntegritySeverity.Info));
        Assert.That(capped.Summary, Does.Contain("were not stored"));
        Assert.That(await RecordAsync(w, Instant("paste", w.Now.AddMinutes(-1), 1)), Is.False, "once capped, the browser is told to stop");
        Assert.That(await RowsAsync(w), Has.Count.EqualTo(IntegrityRecorder.MaxEventsPerSession));
    }

    [Test]
    public void The_observed_clock_skew_comes_from_the_batch_send_time()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.That(IntegrityRecorder.ObservedSkew(new BrowserSignalBatch { SentAt = Ms(now.AddSeconds(-3)) }, now), Is.EqualTo(TimeSpan.FromSeconds(3)).Within(TimeSpan.FromMilliseconds(2)));
        Assert.That(IntegrityRecorder.ObservedSkew(new BrowserSignalBatch { SentAt = 0 }, now), Is.Null);
    }

    [Test]
    public async Task Settings_are_given_only_for_the_students_own_session()
    {
        var w = await RunningAsync(IntegrityLevel.Strict);
        await using var db = OrgDb(w.Org);
        var recorder = new IntegrityRecorder(db, Clock, NullLogger<IntegrityRecorder>.Instance);
        Assert.That((await recorder.SettingsAsync(w.Org, w.StudentId, w.SessionId))!.Level, Is.EqualTo(IntegrityLevel.Strict));
        Assert.That(await recorder.SettingsAsync(w.Org, Guid.NewGuid(), w.SessionId), Is.Null);
    }
}
