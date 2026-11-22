using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The class summary and its PDF (GAD-T311, GAD-T378; under GAD-T399) on the test database, through the real grading workflow:
/// only results released to the student (a Finalize and a Publish record) count; an ended but unreleased attempt and a student
/// who never finished are reported separately; a later unfinished attempt never hides an earlier released one; the PDF says the
/// same figures and names only the students who haven't finished. ClassSummaryReportTests covers the arithmetic.
/// </summary>
[TestFixture]
public class ClassSummaryServiceTests : ServerTest
{
    private sealed record World(Guid Org, Guid ExamId, string ReleasedStudent, string WaitingStudent, string NotStartedStudent);

    private GradingService Grading(GadflyDbContext db, TenantSnapshot tenant) => new(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));

    private async Task<ClassSummaryReport?> BuildAsync(Guid org, Guid examId, string role = OrgRoles.Instructor)
    {
        var me = Member(org, Guid.NewGuid(), role);
        await using var db = Db(me);
        return await new ClassSummaryService(db, new FixedTenant(me), Grading(db, me), Clock).BuildAsync(examId);
    }

    private static async Task<string> UserNameAsync(Guid id)
    {
        await using var db = SystemDb();
        return await db.AuthUsers.Where(u => u.Id == id).Select(u => u.UserName).SingleAsync();
    }

    /// <summary>Three students on one exam: one released (AI 85, examiner 88: 86.8%), one ended but awaiting review, one never started.</summary>
    private async Task<World> ClassAsync()
    {
        var (w, releasedSession, _) = await Seed.EndedSessionAsync();
        var now = Clock.GetUtcNow();
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "understanding", Title = "Understanding", MaxScore = 100, Weight = 1 };
        var waitingStudent = await Seed.AccountAsync();
        var notStartedStudent = await Seed.AccountAsync();
        await using (var db = OrgDb(w.Org))
        {
            db.LearningObjectives.Add(objective);
            db.AiObjectiveGrades.Add(new AiObjectiveGrade
            {
                OrganizationId = w.Org, SessionId = releasedSession, ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = 85, MaxScore = 100,
                Explanation = "AI rationale", Confidence = "high", CreatedAt = now,
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = releasedSession, Status = GradeStatus.HumanReviewPending, UpdatedAt = now });
            // A second, unfinished attempt by the same student must not hide the released one.
            db.ExamSessions.Add(new ExamSession
            {
                OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, Attempt = 2,
                State = SessionState.Running, CreatedAt = now, StartedAt = now,
            });

            var waiting = new ExamAssignment { OrganizationId = w.Org, ExamId = w.ExamId, StudentUserId = waitingStudent, AttemptsAllowed = 1, AssignedAt = now.AddMinutes(1) };
            var notStarted = new ExamAssignment { OrganizationId = w.Org, ExamId = w.ExamId, StudentUserId = notStartedStudent, AttemptsAllowed = 1, AssignedAt = now.AddMinutes(2) };
            db.ExamAssignments.AddRange(waiting, notStarted);
            var waitingSession = new ExamSession
            {
                OrganizationId = w.Org, AssignmentId = waiting.Id, ExamId = w.ExamId, StudentUserId = waitingStudent, Attempt = 1, State = SessionState.Ended,
                CreatedAt = now.AddMinutes(-30), StartedAt = now.AddMinutes(-30), EndedAt = now.AddMinutes(-5), EndReason = "Mastery",
            };
            db.ExamSessions.Add(waitingSession);
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = waitingSession.Id, Status = GradeStatus.HumanReviewPending, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        var examiner = Member(w.Org, Guid.NewGuid(), OrgRoles.Examiner);
        await using (var db = Db(examiner))
        {
            var r = await Grading(db, examiner).SubmitHumanGradesAsync(releasedSession, [new HumanGradeInput(objective.Id, MasteryLevel.Application, 88, "Solid.", null)]);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        var teacher = Member(w.Org, Guid.NewGuid(), OrgRoles.Instructor);
        await using (var db = Db(teacher))
        {
            var g = Grading(db, teacher);
            Assert.That((await g.ApproveAsync(releasedSession, "B+", "Agreed.")).Ok, Is.True);
            Assert.That((await g.PublishAsync(releasedSession, null)).Ok, Is.True);
        }
        return new World(w.Org, w.ExamId, await UserNameAsync(w.StudentId), await UserNameAsync(waitingStudent), await UserNameAsync(notStartedStudent));
    }

    [Test]
    public async Task Only_released_results_count_and_the_rest_are_reported_apart()
    {
        var w = await ClassAsync();
        var r = await BuildAsync(w.Org, w.ExamId);
        Assert.That(r, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(r!.ExamTitle, Is.EqualTo("Biology check"));
            Assert.That(r.TotalAssigned, Is.EqualTo(3));
            Assert.That(r.Released, Is.EqualTo(1), "the released attempt counts although a later attempt is still running");
            Assert.That(r.CompletedNotReleased, Is.EqualTo(1));
            Assert.That(r.NotStarted, Is.EqualTo(new[] { w.NotStartedStudent }));
            Assert.That(r.Scores, Is.EqualTo(new[] { 86.8m }), "85 x 0.4 + 88 x 0.6, as the student's own report computes it");
            Assert.That(r.Objectives.Select(o => (o.Title, o.Count)), Is.EqualTo(new[] { ("Understanding", 1) }));
            Assert.That(r.Objectives[0].Average, Is.Not.Null);
        });
    }

    [Test]
    public async Task Nothing_released_yet_gives_an_empty_distribution_but_still_lists_who_has_not_finished()
    {
        var w = await Seed.ExamAsync();
        var r = await BuildAsync(w.Org, w.ExamId);
        Assert.That(r!.Released, Is.Zero);
        Assert.That(r.Scores, Is.Empty);
        Assert.That(r.Mean, Is.Null);
        Assert.That(r.NotStarted, Is.EqualTo(new[] { await UserNameAsync(w.StudentId) }));
    }

    [Test]
    public async Task A_student_is_refused_and_the_refusal_is_audited()
    {
        var w = await Seed.ExamAsync();
        Assert.That(await BuildAsync(w.Org, w.ExamId, OrgRoles.Student), Is.Null);
        await using var db = SystemDb();
        Assert.That(await db.AuditEvents.AnyAsync(a => a.OrganizationId == w.Org && a.Action == "access.denied"), Is.True);
    }

    [Test]
    public async Task Another_organizations_exam_is_not_summarized()
    {
        var mine = await Seed.ExamAsync();
        var theirs = await Seed.ExamAsync();
        Assert.That(await BuildAsync(mine.Org, theirs.ExamId), Is.Null);
    }

    // ------------------------------------------------------------------ the PDF

    [Test]
    public async Task The_pdf_states_the_same_figures_and_names_only_students_who_have_not_finished()
    {
        var w = await ClassAsync();
        var r = (await BuildAsync(w.Org, w.ExamId))!;
        var pdf = ClassSummaryPdf.Render(r, Clock.GetUtcNow());
        Assert.That(System.Text.Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
        var text = PdfText.Squash(PdfText.All(pdf));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(PdfText.Squash("Assigned: 3 · released: 1 · completed, awaiting release: 1 · not yet attempted: 1")));
            Assert.That(text, Does.Contain(PdfText.Squash("Mean: 86.8%")));
            Assert.That(text, Does.Contain("80-89%"));
            Assert.That(text, Does.Contain(PdfText.Squash("(from 1 released result)")));
            Assert.That(text, Does.Contain(w.NotStartedStudent), "who hasn't completed is listed by name");
            Assert.That(text, Does.Not.Contain(w.ReleasedStudent), "the summary never says which student holds which score");
            Assert.That(text, Does.Not.Contain(w.WaitingStudent));
            Assert.That(text, Does.Contain(PdfText.Squash("1 attempt has ended but its result is not yet released")));
        });
        Assert.That(PdfText.Info(pdf), Does.Contain("/Title (Class summary: Biology check)"));
    }

    [Test]
    public void An_empty_summary_still_renders_with_plain_explanations()
    {
        var r = new ClassSummaryReport
        {
            ExamId = Guid.NewGuid(), ExamTitle = "Nothing yet", TotalAssigned = 0, Released = 0, CompletedNotReleased = 0, NotStarted = [], Scores = [], Objectives = [],
        };
        var pages = PdfText.Pages(ClassSummaryPdf.Render(r, DateTimeOffset.UtcNow));
        Assert.That(pages, Has.Count.EqualTo(1));
        var text = PdfText.Squash(pages[0]);
        Assert.That(text, Does.Contain(PdfText.Squash("No results have been released to students yet")));
        Assert.That(text, Does.Contain(PdfText.Squash("No per-objective scores are available yet.")));
        Assert.That(text, Does.Contain(PdfText.Squash("Every assigned student has at least one ended attempt.")));
        Assert.That(pages[0], Does.Contain("Gadfly class summary · Nothing yet · page 1 of 1"));
    }
}
