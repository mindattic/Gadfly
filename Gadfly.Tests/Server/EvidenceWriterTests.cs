using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Evidence writing and its background sync (GAD-T91, GAD-T92, GAD-T93, GAD-T404; under GAD-T399) on the test database: evidence is
/// written only as a verbatim quote of a verified original answer, linked to its latest transcript version; anything else is
/// counted as skipped and never fabricated; contamination and assistance only ever turn on; a repeat sync adds nothing; and the
/// queued writer stores the same thing off the session step.
/// </summary>
[TestFixture]
public class EvidenceWriterTests : ServerTest
{
    private const string Answer = "Plants use light to make glucose.";

    private sealed record World(Guid Org, Guid SessionId, Guid ExamId, Guid ResponseId, Guid LatestVersionId);

    /// <summary>An ended session whose first turn has a confirmed original answer (two transcript versions) and an objective "light".</summary>
    private async Task<World> WorldAsync(VerificationState state = VerificationState.Confirmed, bool assisted = false)
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Answer);
        var now = Clock.GetUtcNow();
        await using var db = OrgDb(w.Org);
        db.LearningObjectives.Add(new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "light", Title = "Light energy", MaxScore = 100, Weight = 1 });
        var response = new StudentResponse
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, Kind = ResponseKind.Original, Modality = "voice",
            OriginalText = "Plants use lite to make glucose.", VerifiedText = Answer, VerificationState = state, Assisted = assisted, SubmittedAt = now,
        };
        db.StudentResponses.Add(response);
        var v1 = new TranscriptVersion { OrganizationId = w.Org, ResponseId = response.Id, Version = 1, Text = response.OriginalText, Source = "speech-recognition", CreatedAt = now };
        var v2 = new TranscriptVersion { OrganizationId = w.Org, ResponseId = response.Id, Version = 2, Text = Answer, CorrectionKind = CorrectionKind.Transcription, CreatedAt = now };
        db.TranscriptVersions.AddRange(v1, v2);
        // A later substantive revision of the same turn: evidence still comes from the original answer the engine judged.
        db.StudentResponses.Add(new StudentResponse
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 2, Kind = ResponseKind.SubstantiveRevision, RevisesResponseId = response.Id,
            OriginalText = "Actually plants eat soil.", VerifiedText = "Actually plants eat soil.", VerificationState = VerificationState.Confirmed, SubmittedAt = now,
        });
        await db.SaveChangesAsync();
        return new World(w.Org, sessionId, w.ExamId, response.Id, v2.Id);
    }

    private static CollectedEvidence Item(string quote = "use light to make glucose", string objective = "light", int turn = 0,
        EvidencePolarity polarity = EvidencePolarity.Supports, bool contaminated = false, bool assisted = false) =>
        new(turn, objective, polarity, AssessmentDimension.ConceptualUnderstanding, quote, "explains where the energy comes from", 1, assisted, contaminated, MoveKind.Elicit);

    private async Task<EvidenceSyncResult> SyncAsync(World w, params CollectedEvidence[] items)
    {
        await using var db = OrgDb(w.Org);
        var r = await EvidenceWriter.SyncAsync(db, w.Org, w.SessionId, w.ExamId, items, Clock.GetUtcNow());
        await db.SaveChangesAsync();
        return r;
    }

    private static async Task<List<AssessmentEvidence>> RowsAsync(World w)
    {
        await using var db = SystemDb();
        return await db.AssessmentEvidence.AsNoTracking().Where(e => e.SessionId == w.SessionId).ToListAsync();
    }

    private static async Task<int> AuditAsync(World w, string action)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.CountAsync(a => a.SessionId == w.SessionId && a.Action == action);
    }

    [Test]
    public async Task A_verbatim_quote_of_a_verified_answer_is_written_once_linked_to_its_latest_transcript_version()
    {
        var w = await WorldAsync();
        var first = await SyncAsync(w, Item());
        Assert.That(first.Added, Is.EqualTo(1));

        var row = (await RowsAsync(w)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.ResponseId, Is.EqualTo(w.ResponseId), "the original answer, not the later revision");
            Assert.That(row.TranscriptVersionId, Is.EqualTo(w.LatestVersionId));
            Assert.That(row.Stance, Is.EqualTo(EvidenceStance.Supports));
            Assert.That(row.Dimension, Is.EqualTo(AssessmentDimension.ConceptualUnderstanding));
            Assert.That(row.Note, Is.EqualTo("\"use light to make glucose\" - explains where the energy comes from"));
            Assert.That(row.Contaminated, Is.False);
            Assert.That(row.Assisted, Is.False);
        });
        Assert.That(await AuditAsync(w, "evidence.recorded"), Is.EqualTo(1));

        var again = await SyncAsync(w, Item());
        Assert.That(again.Added, Is.Zero, "a repeat sync is idempotent");
        Assert.That(await RowsAsync(w), Has.Count.EqualTo(1));
        Assert.That(await AuditAsync(w, "evidence.recorded"), Is.EqualTo(1), "nothing new, nothing audited");
    }

    [Test]
    public async Task Nothing_is_fabricated_every_unusable_item_is_counted_and_skipped()
    {
        var w = await WorldAsync();
        var r = await SyncAsync(w,
            Item(quote: "plants eat soil"),            // only in the revision, and not in the verified answer
            Item(objective: "no-such-objective"),
            Item(turn: 7));
        Assert.That(r.Added, Is.Zero);
        Assert.That(r.SkippedNotVerbatim, Is.EqualTo(1));
        Assert.That(r.SkippedNoObjective, Is.EqualTo(1));
        Assert.That(r.SkippedNoResponse, Is.EqualTo(1));
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [TestCase(VerificationState.Unresolved)]
    [TestCase(VerificationState.FlaggedForHumanReview)]
    [TestCase(VerificationState.AwaitingStudentConfirmation)]
    public async Task An_answer_not_yet_verified_gives_no_evidence(VerificationState state)
    {
        var w = await WorldAsync(state);
        var r = await SyncAsync(w, Item());
        Assert.That(r.SkippedUnverified, Is.EqualTo(1));
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public async Task Contamination_turns_on_for_a_stored_row_is_audited_and_never_turns_off()
    {
        var w = await WorldAsync();
        await SyncAsync(w, Item());
        var r = await SyncAsync(w, Item(contaminated: true));
        Assert.That(r.NewlyContaminated, Is.EqualTo(1));
        Assert.That((await RowsAsync(w)).Single().Contaminated, Is.True);
        Assert.That(await AuditAsync(w, "evidence.contaminated"), Is.EqualTo(1));

        var later = await SyncAsync(w, Item(contaminated: false));
        Assert.That(later.NewlyContaminated, Is.Zero);
        Assert.That((await RowsAsync(w)).Single().Contaminated, Is.True, "a flag only ever turns on");
    }

    [Test]
    public async Task Assistance_from_the_item_or_the_answer_marks_the_row_assisted()
    {
        var helped = await WorldAsync(assisted: true);
        await SyncAsync(helped, Item());
        Assert.That((await RowsAsync(helped)).Single().Assisted, Is.True, "the answer was given after help");

        var w = await WorldAsync();
        await SyncAsync(w, Item());
        var r = await SyncAsync(w, Item(assisted: true));
        Assert.That(r.NewlyAssisted, Is.EqualTo(1));
        Assert.That((await RowsAsync(w)).Single().Assisted, Is.True);
        Assert.That(await AuditAsync(w, "evidence.assisted"), Is.EqualTo(1));
    }

    [Test]
    public async Task Each_polarity_maps_to_its_stance_and_distinct_items_are_separate_rows()
    {
        var w = await WorldAsync();
        await SyncAsync(w, Item(polarity: EvidencePolarity.Supports), Item(polarity: EvidencePolarity.Misconception), Item(polarity: EvidencePolarity.Uncertain));
        Assert.That((await RowsAsync(w)).Select(r => r.Stance), Is.EquivalentTo(new[] { EvidenceStance.Supports, EvidenceStance.Misconception, EvidenceStance.Uncertain }));
    }

    [Test]
    public async Task The_sync_leaves_saving_to_the_caller()
    {
        var w = await WorldAsync();
        await using (var db = OrgDb(w.Org))
            Assert.That((await EvidenceWriter.SyncAsync(db, w.Org, w.SessionId, w.ExamId, [Item()], Clock.GetUtcNow())).Added, Is.EqualTo(1));
        Assert.That(await RowsAsync(w), Is.Empty);
    }

    [Test]
    public void A_long_note_is_capped()
    {
        var e = Item() with { Note = new string('n', 5000) };
        Assert.That(EvidenceWriter.NoteOf(e), Has.Length.EqualTo(EvidenceWriter.MaxNote));
    }

    // ------------------------------------------------------------------ the queued writer

    [Test]
    public async Task The_queued_writer_stores_the_evidence_off_the_step_and_goes_on_past_a_request_that_writes_nothing()
    {
        var w = await WorldAsync();
        var queue = new EvidenceSyncQueue();
        using var writer = new EvidenceSyncWriter(queue, Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EvidenceSyncWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);
        try
        {
            // A request for a session that does not exist writes nothing; the next request is still processed.
            queue.Enqueue(new EvidenceSyncRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Item()], Clock.GetUtcNow()));
            queue.Enqueue(new EvidenceSyncRequest(w.Org, w.SessionId, w.ExamId, [Item()], Clock.GetUtcNow()));
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while ((await RowsAsync(w)).Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(100);
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
        }
        Assert.That(await RowsAsync(w), Has.Count.EqualTo(1));
    }
}
