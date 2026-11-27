using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sessions;
using Gadfly.Server.Tenancy;
using Gadfly.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The closing reflection (GAD-T817): one fixed question after the examiner ends the exam, stored on the session, outside the clock,
/// skippable, and never reaching a turn, the ledger, a judge, the report or grading.
/// </summary>
[TestFixture]
public class ClosingReflectionTests : ServerTest
{
    private static SessionOrchestrator Orchestrator => Services.GetRequiredService<SessionOrchestrator>();

    private const string Reflection = "My claim that the light reactions make sugar directly; I am not sure where the sugar is built.";

    private Answer Typed(string text) => new() { Text = text, CompletedAt = Clock.GetUtcNow() };

    private static async Task<Guid> StartedAsync(Seed.ExamWorld w)
    {
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "test-browser");
        await Orchestrator.RecordConsentAsync(w.Org, id, w.StudentId, new string('c', 64));
        return id;
    }

    /// <summary>Runs a two-question exam to its end by the examiner; returns the ending step.</summary>
    private async Task<(Seed.ExamWorld World, Guid Id, SessionStep End)> ExaminerEndedAsync()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 2 });
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("Plants capture light energy and store it as chemical energy in glucose."));
        var end = await Orchestrator.AnswerAsync(w.Org, id, w.StudentId, Typed("The light reactions split water, and the energy builds sugar from carbon dioxide."));
        return (w, id, end);
    }

    [Test]
    public void TheQuestion_IsFixedPerLanguage_AndAsksForReasonsNotProcess()
    {
        Assert.That(ClosingReflection.Prompt(ExamLanguage.English), Is.EqualTo(ClosingReflection.English));
        Assert.That(ClosingReflection.Prompt(ExamLanguage.Spanish), Is.EqualTo(ClosingReflection.Spanish));
        Assert.That(ClosingReflection.Prompt(ExamLanguage.Korean), Is.EqualTo(ClosingReflection.Korean));
        Assert.That(ClosingReflection.Prompt("fr"), Is.EqualTo(ClosingReflection.English));
        foreach (var prompt in new[] { ClosingReflection.English, ClosingReflection.Spanish, ClosingReflection.Korean })
            Assert.That(QuestionGenerator.IsIntrospective(prompt), Is.False, prompt);
    }

    [Test]
    public void ItIsOffered_OnlyWhenTheExaminerEndsTheExam()
    {
        Assert.That(Enum.GetValues<EndReason>().Where(ClosingReflection.IsOfferedAfter),
            Is.EquivalentTo(new[] { EndReason.EvidenceComplete, EndReason.TurnLimit, EndReason.TimeUp }));
    }

    [Test]
    public async Task AfterTheLastQuestion_TheStepCarriesIt_AndAReloadComesBackToIt()
    {
        var (w, id, end) = await ExaminerEndedAsync();
        Assert.That(end.State, Is.EqualTo(SessionState.Ended));
        Assert.That(end.Question, Is.Null);
        Assert.That(end.Reflection, Is.EqualTo(ClosingReflection.English));
        var reload = await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        Assert.That(reload.Reflection, Is.EqualTo(ClosingReflection.English));
        Assert.That(reload.EndReason, Is.EqualTo(end.EndReason), "the reflection does not change why the exam ended");
    }

    [Test]
    public async Task Answering_StoresItOnTheSession_AndClosesIt_WithoutReachingTurnsReportOrGrading()
    {
        var (w, id, end) = await ExaminerEndedAsync();
        int turnsBefore;
        string reportBefore;
        await using (var db = SystemDb())
        {
            turnsBefore = await db.SessionTurns.CountAsync(t => t.SessionId == id);
            reportBefore = (await db.SessionReports.AsNoTracking().SingleAsync(r => r.SessionId == id)).ReportJson;
        }
        var judgeCalls = Panel.Count(ScriptedPanel.Kind.Judge);

        var step = await Orchestrator.AnswerReflectionAsync(w.Org, id, w.StudentId, "  " + Reflection + " ", "voice");
        Assert.That(step.Reflection, Is.Null);
        Assert.That(step.State, Is.EqualTo(SessionState.Ended));
        Assert.That(step.EndReason, Is.EqualTo(end.EndReason));
        Assert.That((await Orchestrator.CurrentAsync(w.Org, id, w.StudentId)).Reflection, Is.Null, "never offered twice");

        await using var check = SystemDb();
        var row = await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.ClosingReflectionState, Is.EqualTo(ClosingReflection.Answered));
        Assert.That(row.ClosingReflectionText, Is.EqualTo(Reflection));
        Assert.That(row.ClosingReflectionMode, Is.EqualTo("voice"));
        Assert.That(row.ClosingReflectionAt, Is.Not.Null);
        Assert.That(row.EndReason, Is.EqualTo(end.EndReason));
        Assert.That(row.TranscriptJson, Does.Not.Contain("sugar is built"), "not a turn of the session");
        Assert.That(row.LedgerJson ?? "", Does.Not.Contain("sugar is built"), "not in the claim ledger");
        Assert.That(await check.SessionTurns.CountAsync(t => t.SessionId == id), Is.EqualTo(turnsBefore));
        Assert.That(await check.StudentResponses.AnyAsync(r => r.SessionId == id && r.OriginalText.Contains("sugar is built")), Is.False);
        Assert.That((await check.SessionReports.AsNoTracking().SingleAsync(r => r.SessionId == id)).ReportJson, Is.EqualTo(reportBefore));
        Assert.That(Panel.Count(ScriptedPanel.Kind.Judge), Is.EqualTo(judgeCalls), "never judged");
    }

    [Test]
    public async Task Skipping_OrAnEmptyAnswer_ClosesIt_AndALaterAnswerChangesNothing()
    {
        var (w, id, _) = await ExaminerEndedAsync();
        var step = await Orchestrator.AnswerReflectionAsync(w.Org, id, w.StudentId, "   ", "voice");
        Assert.That(step.Reflection, Is.Null);
        await Orchestrator.AnswerReflectionAsync(w.Org, id, w.StudentId, Reflection, "voice");
        await Orchestrator.SkipReflectionAsync(w.Org, id, w.StudentId);
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.ClosingReflectionState, Is.EqualTo(ClosingReflection.Skipped));
        Assert.That(row.ClosingReflectionText, Is.Null);
        Assert.That(row.ClosingReflectionMode, Is.Null);
    }

    [Test]
    public async Task SkipReflection_ClosesIt()
    {
        var (w, id, _) = await ExaminerEndedAsync();
        var step = await Orchestrator.SkipReflectionAsync(w.Org, id, w.StudentId);
        Assert.That(step.Reflection, Is.Null);
        await using var db = SystemDb();
        Assert.That((await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id)).ClosingReflectionState, Is.EqualTo(ClosingReflection.Skipped));
    }

    [Test]
    public async Task TimeRunningOut_OffersIt()
    {
        var w = await Seed.ExamAsync(policy: new SessionPolicy { ConceptIds = ["photosynthesis"], TimeBudget = TimeSpan.FromMinutes(5) });
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await Orchestrator.StartClockAsync(w.Org, id, w.StudentId);
        Clock.Advance(TimeSpan.FromMinutes(5) + SessionOrchestrator.AnswerGrace + TimeSpan.FromSeconds(5));
        var step = await Orchestrator.EndAsync(w.Org, id, w.StudentId, EndReason.TimeUp);
        Assert.That(step.EndReason, Is.EqualTo(nameof(EndReason.TimeUp)));
        Assert.That(step.Reflection, Is.EqualTo(ClosingReflection.English));
    }

    [Test]
    public async Task FinishingEarly_OrStaffEnding_DoesNotOfferIt()
    {
        var w = await Seed.ExamAsync();
        var id = await StartedAsync(w);
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        var step = await Orchestrator.EndAsync(w.Org, id, w.StudentId, EndReason.Abandoned);
        Assert.That(step.Reflection, Is.Null);
        Assert.That((await Orchestrator.CurrentAsync(w.Org, id, w.StudentId)).Reflection, Is.Null);
        var answered = await Orchestrator.AnswerReflectionAsync(w.Org, id, w.StudentId, Reflection, "voice");
        Assert.That(answered.Reflection, Is.Null);
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.ClosingReflectionState, Is.Null, "never offered, so nothing is stored");
        Assert.That(row.ClosingReflectionText, Is.Null);
    }

    [Test]
    public async Task Retention_RedactsTheText_AndExportIncludesIt()
    {
        var (w, id, _) = await ExaminerEndedAsync();
        await Orchestrator.AnswerReflectionAsync(w.Org, id, w.StudentId, Reflection, "voice");
        await using (var scope = Scope(TenantSnapshot.ForOrganization(w.Org)))
        {
            var privacy = scope.ServiceProvider.GetRequiredService<PrivacyService>();
            Assert.That(await privacy.ExportStudentAsync(w.Org, w.StudentId), Does.Contain("sugar is built"));
            await privacy.RedactSessionAsync(id);
        }
        await using var check = SystemDb();
        var row = await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.ClosingReflectionText, Does.Not.Contain("sugar"));
        Assert.That(row.ClosingReflectionState, Is.EqualTo(ClosingReflection.Answered));
    }
}
