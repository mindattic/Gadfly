using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>
/// Blind double marking (GAD-T428) and keeping the second marker blind on the normal grading page (GAD-T468), through the real
/// services on LocalDB: the second marker sees a session code and the rubric, never the panel's or the first examiner's
/// grade; the first examiner and anyone who has seen the grades can't second-mark; a second marker is refused the grading page.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class DoubleMarkingTests : ServerTest
{
    private sealed record World(Guid Org, Guid ExamId, Guid SessionId, Guid ObjectiveId, Guid FirstExaminer);

    /// <summary>An ended session with an AI grade and a blind first grade from an instructor, optionally on a double-marked exam.</summary>
    private async Task<World> Graded(bool doubleMarking = true, bool firstGrade = true, GradeStatus status = GradeStatus.HumanReviewPending)
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "understanding", Title = "Understanding", MaxScore = 100, Weight = 1 };
        await using (var db = OrgDb(w.Org))
        {
            db.LearningObjectives.Add(objective);
            db.AiObjectiveGrades.Add(new AiObjectiveGrade
            {
                OrganizationId = w.Org, SessionId = sessionId, ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = 92, MaxScore = 100,
                Explanation = "AI rationale", Confidence = "high", CreatedAt = Clock.GetUtcNow(),
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = status, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        if (doubleMarking)
        {
            var r = await Panel(w.Org, OrgRoles.Owner, Guid.NewGuid(), p => p.SaveAsync(w.ExamId, new ExamPanelSettings(DoubleMarking: true)));
            Assert.That(r.Ok, Is.True, r.Message);
        }
        var first = Guid.NewGuid();
        if (firstGrade)
        {
            var grading = As(w.Org, OrgRoles.Instructor, first, out var gdb);
            await using (gdb)
            {
                var r = await grading.SubmitHumanGradesAsync(sessionId, [new HumanGradeInput(objective.Id, MasteryLevel.Application, 90, null, null)]);
                Assert.That(r.Ok, Is.True, r.Message);
            }
        }
        return new World(w.Org, w.ExamId, sessionId, objective.Id, first);
    }

    private GradingService As(Guid org, string role, Guid user, out GadflyDbContext db)
    {
        var tenant = Member(org, user, role);
        db = Db(tenant);
        return new GradingService(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));
    }

    private static async Task<T> Marking<T>(Guid org, string role, Guid user, Func<DoubleMarkingService, Task<T>> act)
    {
        await using var scope = Scope(Member(org, user, role));
        return await act(scope.ServiceProvider.GetRequiredService<DoubleMarkingService>());
    }

    private static async Task<T> Panel<T>(Guid org, string role, Guid user, Func<PanelGovernanceService, Task<T>> act)
    {
        await using var scope = Scope(Member(org, user, role));
        return await act(scope.ServiceProvider.GetRequiredService<PanelGovernanceService>());
    }

    private static async Task<int> Audits(Guid org, string action, Guid actor, Guid? session = null)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking()
            .CountAsync(a => a.OrganizationId == org && a.Action == action && a.ActorUserId == actor && (session == null || a.SessionId == session));
    }

    private async Task<GradingWorkspace?> OpenGradingPage(World w, string role, Guid user)
    {
        var grading = As(w.Org, role, user, out var db);
        await using (db) return await grading.WorkspaceAsync(w.SessionId);
    }

    private static Task<GradingResult> SecondMark(World w, Guid marker, int level = 3, string role = OrgRoles.Examiner) =>
        Marking(w.Org, role, marker, m => m.SubmitAsync(w.SessionId, [new SecondMarkInput(w.ObjectiveId, level, "Blind second mark.")]));

    // ------------------------------------------------------------------ settings

    [Test]
    public void DoubleMarking_IsOffByDefault() => Assert.That(ExamPanelSettings.Default.DoubleMarking, Is.False);

    [Test]
    public async Task TurningDoubleMarkingOn_NeedsManageAssessments_AndIsAudited()
    {
        var w = await Graded(doubleMarking: false, firstGrade: false);
        var examiner = Guid.NewGuid();
        var refused = await Panel(w.Org, OrgRoles.Examiner, examiner, p => p.SaveAsync(w.ExamId, new ExamPanelSettings(DoubleMarking: true)));
        Assert.That(refused.Ok, Is.False);
        Assert.That(await Audits(w.Org, "access.denied", examiner), Is.EqualTo(1));

        var instructor = Guid.NewGuid();
        var ok = await Panel(w.Org, OrgRoles.Instructor, instructor, p => p.SaveAsync(w.ExamId, new ExamPanelSettings(DoubleMarking: true)));
        Assert.That(ok.Ok, Is.True, ok.Message);
        Assert.That(await Audits(w.Org, PanelGovernanceService.SettingsAction, instructor), Is.EqualTo(1));
        var again = await Panel(w.Org, OrgRoles.Instructor, instructor, p => p.SaveAsync(w.ExamId, new ExamPanelSettings(DoubleMarking: true)));
        Assert.That(again.Message, Does.Contain("Nothing changed"));
        Assert.That(await Panel(w.Org, OrgRoles.Examiner, examiner, p => p.SettingsAsync(w.ExamId)), Is.EqualTo(new ExamPanelSettings(DoubleMarking: true)));
    }

    // ------------------------------------------------------------------ who may second-mark

    [Test]
    public async Task AnExamNotSetForDoubleMarking_RefusesTheSheet()
    {
        var w = await Graded(doubleMarking: false);
        var (sheet, refusal) = await Marking(w.Org, OrgRoles.Examiner, Guid.NewGuid(), m => m.SheetAsync(w.SessionId));
        Assert.That(sheet, Is.Null);
        Assert.That(refusal, Does.Contain("not set for double marking"));
    }

    [Test]
    public async Task AStudent_IsRefusedAndTheAttemptAudited()
    {
        var w = await Graded();
        var student = Guid.NewGuid();
        var (sheet, refusal) = await Marking(w.Org, OrgRoles.Student, student, m => m.SheetAsync(w.SessionId));
        Assert.That(sheet, Is.Null);
        Assert.That(refusal, Does.Contain("does not include grading"));
        Assert.That(await Audits(w.Org, "access.denied", student), Is.EqualTo(1));
        Assert.That(await Marking(w.Org, OrgRoles.Student, student, m => m.QueueAsync(w.ExamId)), Is.Null);
    }

    [Test]
    public async Task ASessionStillBeingGraded_IsNotReady()
    {
        var w = await Graded(firstGrade: false, status: GradeStatus.AiGradingPending);
        var (_, refusal) = await Marking(w.Org, OrgRoles.Examiner, Guid.NewGuid(), m => m.SheetAsync(w.SessionId));
        Assert.That(refusal, Does.Contain("not ready for marking"));
    }

    [Test]
    public async Task TheFirstExaminer_CannotAlsoSecondMark()
    {
        var w = await Graded();
        var (sheet, refusal) = await Marking(w.Org, OrgRoles.Instructor, w.FirstExaminer, m => m.SheetAsync(w.SessionId));
        Assert.That(sheet, Is.Null);
        Assert.That(refusal, Does.Contain("graded this session as its examiner"));
        var row = (await Marking(w.Org, OrgRoles.Instructor, w.FirstExaminer, m => m.QueueAsync(w.ExamId)))!.Rows.Single(r => r.SessionId == w.SessionId);
        Assert.That(row.IAmFirstExaminer, Is.True);
        Assert.That(row.Open, Is.False);
    }

    // ------------------------------------------------------------------ the blind sheet and marks

    [Test]
    public async Task TheSheet_ShowsACodeAndTheRubric_NeverAGrade()
    {
        var w = await Graded();
        var (sheet, refusal) = await Marking(w.Org, OrgRoles.Examiner, Guid.NewGuid(), m => m.SheetAsync(w.SessionId));
        Assert.That(refusal, Is.Null);
        Assert.That(sheet!.Code, Is.EqualTo(AnonymousMarking.Code(w.SessionId)));
        var objective = sheet.Objectives.Single();
        Assert.That(objective.ObjectiveId, Is.EqualTo(w.ObjectiveId));
        Assert.That(objective.Criteria, Is.Not.Empty, "an objective without criteria gets the default rubric");
        Assert.That(objective.Criteria.All(c => c.Levels.Count == Enum.GetValues<MasteryLevel>().Length), Is.True);
        Assert.That(objective.MyLevel, Is.Null);
        // The sheet's types carry no score or level from anyone else; check nothing like the AI's rationale leaks in text either.
        Assert.That(System.Text.Json.JsonSerializer.Serialize(sheet), Does.Not.Contain("AI rationale"));
    }

    [Test]
    public async Task Marks_NeedEveryObjective_AndALevelInRange()
    {
        var w = await Graded();
        var marker = Guid.NewGuid();
        var none = await Marking(w.Org, OrgRoles.Examiner, marker, m => m.SubmitAsync(w.SessionId, []));
        Assert.That(none.Ok, Is.False);
        Assert.That(none.Message, Does.Contain("every objective"));
        var outOfRange = await SecondMark(w, marker, level: 6);
        Assert.That(outOfRange.Ok, Is.False);
        Assert.That(outOfRange.Message, Does.Contain("Levels run from"));
        var foreign = await Marking(w.Org, OrgRoles.Examiner, marker, m => m.SubmitAsync(w.SessionId,
            [new SecondMarkInput(w.ObjectiveId, 3, null), new SecondMarkInput(Guid.NewGuid(), 3, null)]));
        Assert.That(foreign.Message, Does.Contain("objective this exam does not have"));
        var longComment = await Marking(w.Org, OrgRoles.Examiner, marker, m => m.SubmitAsync(w.SessionId,
            [new SecondMarkInput(w.ObjectiveId, 3, new string('c', DoubleMarkingService.MaxCommentLength + 1))]));
        Assert.That(longComment.Ok, Is.False);
        Assert.That(await Audits(w.Org, DoubleMarkingService.MarkAction, marker), Is.Zero, "refused marks record nothing");
    }

    [Test]
    public async Task ASecondMark_IsRecorded_DoesNotChangeTheGrade_AndClosesTheSessionToOtherMarkers()
    {
        var w = await Graded();
        decimal? before;
        await using (var db = SystemDb()) before = await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == w.SessionId).Select(g => g.FinalPercent).SingleAsync();

        var marker = Guid.NewGuid();
        var r = await SecondMark(w, marker, level: 2);
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(await Audits(w.Org, DoubleMarkingService.MarkAction, marker, w.SessionId), Is.EqualTo(1));
        await using (var db = SystemDb())
            Assert.That(await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == w.SessionId).Select(g => g.FinalPercent).SingleAsync(), Is.EqualTo(before));

        // The marker sees their own level again and may revise it; another marker is refused.
        var (sheet, _) = await Marking(w.Org, OrgRoles.Examiner, marker, m => m.SheetAsync(w.SessionId));
        Assert.That(sheet!.Objectives.Single().MyLevel, Is.EqualTo(2));
        Assert.That((await SecondMark(w, marker, level: 3)).Ok, Is.True);
        var (otherSheet, refusal) = await Marking(w.Org, OrgRoles.Examiner, Guid.NewGuid(), m => m.SheetAsync(w.SessionId));
        Assert.That(otherSheet, Is.Null);
        Assert.That(refusal, Does.Contain("Another examiner has already second-marked"));

        var row = (await Marking(w.Org, OrgRoles.Examiner, marker, m => m.QueueAsync(w.ExamId)))!.Rows.Single(x => x.SessionId == w.SessionId);
        Assert.That(row.MarkedByMe, Is.True);
        Assert.That(row.MarkedByOther, Is.False);
        Assert.That(row.Code, Is.EqualTo(AnonymousMarking.Code(w.SessionId)));
    }

    [Test]
    public async Task TheSummary_PairsPanelFirstAndSecondLevels_ForThoseWhoMayReadAgreement()
    {
        var w = await Graded();
        Assert.That((await SecondMark(w, Guid.NewGuid(), level: 3)).Ok, Is.True);
        Assert.That(await Marking(w.Org, OrgRoles.Examiner, Guid.NewGuid(), m => m.SummaryAsync(w.ExamId)), Is.Null, "an examiner cannot read agreement");
        var summary = await Marking(w.Org, OrgRoles.Owner, Guid.NewGuid(), m => m.SummaryAsync(w.ExamId));
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.SessionsSecondMarked, Is.EqualTo(1));
        var item = summary.Items.Single();
        Assert.That((item.PanelLevel, item.FirstLevel, item.SecondLevel), Is.EqualTo(((int)MasteryLevel.Application, (int)MasteryLevel.Application, 3)));
    }

    [Test]
    public async Task RecordingACalibration_WithNothingDoubleMarked_SaysSo()
    {
        var w = await Graded();
        var (result, report) = await Marking(w.Org, OrgRoles.Owner, Guid.NewGuid(), m => m.RecordCalibrationAsync(w.ExamId));
        Assert.That(result.Ok, Is.False);
        Assert.That(report, Is.Null);
        Assert.That(result.Message, Does.Contain("No answer has a panel grade"));
    }

    // ------------------------------------------------------------------ GAD-T468: blind on the normal grading page too

    [Test]
    public async Task ASecondMarker_IsRefusedTheSessionsGradingPage_AndTheAttemptAudited()
    {
        var w = await Graded();
        var marker = Guid.NewGuid();
        Assert.That((await SecondMark(w, marker)).Ok, Is.True);

        var deniedBefore = await Audits(w.Org, "access.denied", marker);
        Assert.That(await OpenGradingPage(w, OrgRoles.Examiner, marker), Is.Null);
        Assert.That(await Audits(w.Org, "access.denied", marker) - deniedBefore, Is.EqualTo(1));
        var grading = As(w.Org, OrgRoles.Examiner, marker, out var db);
        await using (db) Assert.That(await grading.IsSecondMarkerAsync(w.SessionId), Is.True);

        // Someone who did not second-mark still opens it.
        Assert.That(await OpenGradingPage(w, OrgRoles.Owner, Guid.NewGuid()), Is.Not.Null);
    }

    [Test]
    public async Task OpeningTheGradingPageWithGradesShowing_IsRecordedOnce_AndThenRefusesSecondMarking()
    {
        var w = await Graded();
        var looker = Guid.NewGuid();
        var page = await OpenGradingPage(w, OrgRoles.Examiner, looker);
        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Objectives.Any(o => o.Human is not null), Is.True, "the first examiner's grade is showing");
        Assert.That(await OpenGradingPage(w, OrgRoles.Examiner, looker), Is.Not.Null);
        Assert.That(await Audits(w.Org, SecondMarkerBlindness.SawGradesAction, looker, w.SessionId), Is.EqualTo(1), "recorded once per person and session");

        var (sheet, refusal) = await Marking(w.Org, OrgRoles.Examiner, looker, m => m.SheetAsync(w.SessionId));
        Assert.That(sheet, Is.Null);
        Assert.That(refusal, Does.Contain("already opened this session's grading page"));
        var submit = await SecondMark(w, looker);
        Assert.That(submit.Ok, Is.False);
        Assert.That(submit.Message, Does.Contain("already opened this session's grading page"));
        Assert.That(await Audits(w.Org, DoubleMarkingService.MarkAction, looker), Is.Zero);

        // Someone who never looked can still mark it blind.
        Assert.That((await SecondMark(w, Guid.NewGuid())).Ok, Is.True);
    }

    [Test]
    public async Task TheFirstExaminerOpeningTheirOwnPage_IsNotRecorded()
    {
        var w = await Graded();
        Assert.That(await OpenGradingPage(w, OrgRoles.Instructor, w.FirstExaminer), Is.Not.Null);
        Assert.That(await Audits(w.Org, SecondMarkerBlindness.SawGradesAction, w.FirstExaminer), Is.Zero);
    }

    [Test]
    public async Task OnAnExamWithoutDoubleMarking_OpeningTheGradingPageRecordsNothing()
    {
        var w = await Graded(doubleMarking: false);
        var looker = Guid.NewGuid();
        Assert.That(await OpenGradingPage(w, OrgRoles.Examiner, looker), Is.Not.Null);
        Assert.That(await Audits(w.Org, SecondMarkerBlindness.SawGradesAction, looker), Is.Zero);
    }

    [Test]
    public async Task OpeningThePageBeforeAnyGradeShows_RecordsNothing()
    {
        // No human grade yet and the AI grade hidden while blind: nothing to see, so nothing stops this person second-marking later.
        var w = await Graded(firstGrade: false);
        var looker = Guid.NewGuid();
        var page = await OpenGradingPage(w, OrgRoles.Examiner, looker);
        Assert.That(page, Is.Not.Null);
        if (page!.AiVisible) Assert.Ignore("This exam's policy shows the AI grade before the human grade, so the page does show a grade.");
        Assert.That(await Audits(w.Org, SecondMarkerBlindness.SawGradesAction, looker), Is.Zero);
    }
}
