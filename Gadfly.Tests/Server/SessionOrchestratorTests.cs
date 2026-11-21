using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Live exam sessions on the server (GAD-T44/T72/T74) with the scripted panel: create, resume, limits, ownership, persistence.</summary>
[TestFixture]
public class SessionOrchestratorTests : ServerTest
{
    private static SessionOrchestrator Orchestrator => Services.GetRequiredService<SessionOrchestrator>();

    /// <summary>A second orchestrator over the same database: what a server restart (or another server) sees.</summary>
    private static SessionOrchestrator Fresh() => ActivatorUtilities.CreateInstance<SessionOrchestrator>(Services);

    private Answer Typed(string text) => new() { Text = text, CompletedAt = Clock.GetUtcNow() };

    private static async Task<Guid> StartedAsync(Seed.ExamWorld w, SessionOrchestrator? o = null)
    {
        o ??= Orchestrator;
        var id = await o.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "test-browser");
        await o.RecordConsentAsync(w.Org, id, w.StudentId, new string('c', 64));
        return id;
    }

    [Test]
    public async Task Create_StartsAttemptOne_AndResumesTheSameAttempt()
    {
        var w = await Seed.ExamAsync(attempts: 2);
        var first = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "ua");
        var again = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "ua");
        Assert.That(again, Is.EqualTo(first), "one active attempt per assignment");
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == first);
        Assert.That(row.Attempt, Is.EqualTo(1));
        Assert.That(row.State, Is.EqualTo(SessionState.Created));
        Assert.That(row.PromptSetSha256, Is.Not.Empty);
        Assert.That(row.ClientInfo, Is.EqualTo("ua"));
    }

    /// <summary>GAD-T207: two starts at once (a double click on Begin exam) get the same attempt, not a unique-index failure.</summary>
    [Test]
    public async Task TwoStartsAtOnce_ShareOneAttempt()
    {
        var w = await Seed.ExamAsync(attempts: 2);
        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "ua")));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(1));
        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.CountAsync(s => s.AssignmentId == w.AssignmentId), Is.EqualTo(1));
    }

    [Test]
    public async Task ClientInfo_IsCappedAt500()
    {
        var w = await Seed.ExamAsync();
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, new string('x', 900));
        await using var db = SystemDb();
        Assert.That((await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id)).ClientInfo!.Length, Is.EqualTo(500));
    }

    [Test]
    public async Task NotAssignedToYou_IsRefused()
    {
        var w = await Seed.ExamAsync();
        var ex = Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, Guid.NewGuid(), null));
        Assert.That(ex!.Message, Does.Contain("not assigned to you"));
        var otherOrg = await Seed.OrganizationAsync();
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(otherOrg, w.AssignmentId, w.StudentId, null));
    }

    [Test]
    public async Task ExamWindowAndStatus_AreEnforced()
    {
        var notYet = await Seed.ExamAsync(opens: DateTimeOffset.UtcNow.AddDays(1));
        Assert.That(Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(notYet.Org, notYet.AssignmentId, notYet.StudentId, null))!
            .Message, Does.Contain("opens at"));
        var closed = await Seed.ExamAsync(closes: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.That(Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(closed.Org, closed.AssignmentId, closed.StudentId, null))!
            .Message, Does.Contain("has closed"));
        var draft = await Seed.ExamAsync(status: ExamStatus.Draft);
        Assert.That(Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(draft.Org, draft.AssignmentId, draft.StudentId, null))!
            .Message, Does.Contain("not open yet"));
        var archived = await Seed.ExamAsync(status: ExamStatus.Archived);
        Assert.That(Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(archived.Org, archived.AssignmentId, archived.StudentId, null))!
            .Message, Does.Contain("closed to new attempts"));
    }

    [Test]
    public async Task AnAttemptInProgress_ResumesEvenAfterTheExamIsArchived()
    {
        var w = await Seed.ExamAsync();
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, null);
        await using (var db = OrgDb(w.Org))
        {
            (await db.Exams.SingleAsync(e => e.Id == w.ExamId)).Status = ExamStatus.Archived;
            await db.SaveChangesAsync();
        }
        Assert.That(await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, null), Is.EqualTo(id));
    }

    [Test]
    public async Task AttemptLimit_IsEnforced()
    {
        var w = await Seed.ExamAsync(attempts: 2);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var id = await StartedAsync(w);
            var step = await Orchestrator.EndAsync(w.Org, id, w.StudentId, EndReason.Abandoned);
            Assert.That(step.State, Is.EqualTo(SessionState.Ended));
        }
        var ex = Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, null));
        Assert.That(ex!.Message, Does.Contain("every attempt"));
        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.Where(s => s.AssignmentId == w.AssignmentId).Select(s => s.Attempt).OrderBy(a => a).ToListAsync(),
            Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task MonthlyQuota_RefusesNewAttempts()
    {
        var w = await Seed.ExamAsync(quota: 1m);
        await using (var db = SystemDb())
        {
            db.UsageEntries.Add(new UsageEntry { OrganizationId = w.Org, Provider = "x", CostUsd = 1m, CreatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var ex = Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, null));
        Assert.That(ex!.Message, Does.Contain("monthly usage limit"));
    }

    [Test]
    public async Task NoQuestionBeforeConsent()
    {
        var w = await Seed.ExamAsync();
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, null);
        var ex = Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CurrentAsync(w.Org, id, w.StudentId));
        Assert.That(ex!.Message, Does.Contain("accept the exam notice"));
    }

    [Test]
    public async Task Consent_IsRecordedWithTheTextHash()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.ConsentGiven));
        Assert.That(row.ConsentTextSha256, Is.EqualTo(new string('c', 64)));
        Assert.That(row.ConsentRecordedAt, Is.Not.Null);
    }

    [Test]
    public async Task FirstStep_AsksAQuestion_AndPersistsTheTurnAndTranscript()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        Panel.QuestionText = _ => "In your own words, what does a plant do with sunlight?";
        var step = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Assert.That(step.Question, Is.EqualTo("In your own words, what does a plant do with sunlight?"));
        Assert.That(step.State, Is.EqualTo(SessionState.Running));
        Assert.That(step.TurnIndex, Is.EqualTo(0));
        Assert.That(step.ConceptTitle, Is.EqualTo("Photosynthesis"));
        Assert.That(step.Move, Is.EqualTo("Elicit"));
        Assert.That(step.Remaining, Is.GreaterThan(TimeSpan.Zero));

        var again = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Assert.That(again.Question, Is.EqualTo(step.Question), "asking again repeats the open question");
        Assert.That(Panel.Count(ScriptedPanel.Kind.Question), Is.EqualTo(1));

        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.Running));
        Assert.That(row.TranscriptJson, Is.Not.Null);
        Assert.That(row.SubjectSnapshotJson, Is.Not.Null, "the concept map and rubrics are pinned at start (GAD-T72)");
        var turn = await db.SessionTurns.AsNoTracking().SingleAsync(t => t.SessionId == id);
        Assert.That(turn.Question, Is.EqualTo(step.Question));
        Assert.That(turn.ConceptKey, Is.EqualTo("photosynthesis"));
    }

    [Test]
    public async Task AnsweringPersistsTheJudgment_ThenEndsWithAReport()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 2 });
        var id = await StartedAsync(w);
        Panel.JudgeScore = 9;
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        var second = await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("Plants capture light energy and store it as chemical energy in glucose."));
        Assert.That(second.Question, Is.Not.Null.And.Not.Empty);
        var end = await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("The light reactions split water, and the energy builds sugar from carbon dioxide."));
        Assert.That(end.State, Is.EqualTo(SessionState.Ended));
        Assert.That(end.Question, Is.Null);
        Assert.That(end.EndReason, Is.Not.Null);

        await using var db = SystemDb();
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == id).OrderBy(t => t.Index).ToListAsync();
        Assert.That(turns, Has.Count.EqualTo(2));
        Assert.That(turns[0].AnswerText, Does.Contain("glucose"));
        Assert.That(turns.All(t => t.MasteryAfter is not null), Is.True);
        var judgments = await db.Judgments.AsNoTracking().Include(j => j.Votes).Where(j => turns.Select(t => t.Id).Contains(j.TurnId)).ToListAsync();
        Assert.That(judgments, Has.Count.EqualTo(2));
        Assert.That(judgments.All(j => Math.Abs(j.OverallScore - 0.9) < 1e-9), Is.True);
        Assert.That(judgments.All(j => j.Votes.Count == 2), Is.True);
        Assert.That(await db.ConceptMastery.AnyAsync(m => m.SessionId == id && m.ConceptKey == "photosynthesis"), Is.True);
        var report = await db.SessionReports.AsNoTracking().SingleAsync(r => r.SessionId == id);
        Assert.That(report.Recommendation, Is.Not.Empty);
        var session = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(session.State, Is.EqualTo(SessionState.Ended));
        Assert.That(session.EndedAt, Is.Not.Null);
        Assert.That(await db.UsageEntries.AnyAsync(u => u.SessionId == id), Is.True, "every step's panel calls are metered");
    }

    [Test]
    public async Task ResultsCarryNoScoresToTheStudent()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        var step = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        var json = System.Text.Json.JsonSerializer.Serialize(step);
        Assert.That(json, Does.Not.Contain("Score").And.Not.Contain("Mastery").And.Not.Contain("Recommendation"));
    }

    [Test]
    public async Task AClassmateWithTheSessionId_CannotReadAnswerOrEndIt()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);   // the owner's engine is now cached in memory
        var classmate = Guid.NewGuid();
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CurrentAsync(w.Org, id, classmate));
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.AnswerAsync(w.Org, id, classmate, Typed("hijack")));
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.EndAsync(w.Org, id, classmate, EndReason.Abandoned));
        var otherOrg = await Seed.OrganizationAsync();
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.CurrentAsync(otherOrg, id, w.StudentId));
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.Running));
        Assert.That(await db.SessionTurns.AnyAsync(t => t.SessionId == id && t.AnswerText != null), Is.False);
    }

    [Test]
    public async Task AnswerAfterTheTimeLimitAndGrace_IsNotJudged_TheSessionEndsTimeUp()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], TimeBudget = TimeSpan.FromMinutes(5) });
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Clock.Advance(TimeSpan.FromMinutes(5) + SessionOrchestrator.AnswerGrace + TimeSpan.FromSeconds(5));
        var step = await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("A late answer about light and sugar."));
        Assert.That(step.State, Is.EqualTo(SessionState.Ended));
        Assert.That(step.EndReason, Is.EqualTo(nameof(EndReason.TimeUp)));
        Assert.That(Panel.Count(ScriptedPanel.Kind.Judge), Is.Zero);
    }

    [Test]
    public async Task AnswerWithinTheGrace_IsStillJudged()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], TimeBudget = TimeSpan.FromMinutes(5) });
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(5));
        await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("Plants turn light energy into chemical energy in sugar."));
        Assert.That(Panel.Count(ScriptedPanel.Kind.Judge), Is.EqualTo(1));
    }

    [Test]
    public async Task AnsweringAnEndedSession_IsRefused()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await Orchestrator.EndAsync(w.Org, id, w.StudentId, EndReason.Abandoned);
        Assert.ThrowsAsync<SessionRefusedException>(() => Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("too late")));
    }

    [Test]
    public async Task ASessionResumesOnAnotherServerAtTheSameTurn()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 5 });
        var id = await StartedAsync(w);
        var first = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("Plants store light energy as chemical energy in glucose."));
        var waiting = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);

        var other = Fresh();
        var resumed = await other.CurrentAsync(w.Org, id, w.StudentId);
        Assert.That(resumed.TurnIndex, Is.EqualTo(waiting.TurnIndex));
        Assert.That(resumed.Question, Is.EqualTo(waiting.Question));
        Assert.That(first.TurnIndex, Is.EqualTo(0));
    }

    [Test]
    public async Task ResumeIgnoresConceptMapEditsMadeAfterTheStart()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await using (var db = OrgDb(w.Org))
        {
            var concept = await db.Concepts.SingleAsync(c => c.CollectionId == w.CollectionId && c.Key == "photosynthesis");
            concept.Title = "Renamed after the start";
            await db.SaveChangesAsync();
        }
        var step = await Fresh().CurrentAsync(w.Org, id, w.StudentId);
        Assert.That(step.ConceptTitle, Is.EqualTo("Photosynthesis"));
    }

    [Test]
    public async Task APanelFailure_StillPersistsTheResumePoint()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Panel.Fail = true;
        try { await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("Plants make sugar from light energy.")); }
        catch (Exception) { /* a failing panel may surface; the resume point must survive either way */ }
        Panel.Fail = false;
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.TranscriptJson, Is.Not.Null);
        Assert.That(row.State, Is.Not.EqualTo(SessionState.Ended));
    }
}
