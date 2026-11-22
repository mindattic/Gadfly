using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Tenancy;
using Gadfly.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Fixed opening questions (GAD-T436) through the real service on LocalDB, with the scripted panel as the leak guard:
/// approval is checked as a question, against introspection and by the leak guard (failing closed); approvals and removals
/// are audited; only draft assessments can change.
/// </summary>
[TestFixture]
public class OpeningQuestionServiceTests : ServerTest
{
    private const string Good = "In your own words, how do plants turn sunlight into stored energy?";

    private sealed record World(Guid Org, Guid ExamId, Guid ObjectiveId, string Key);

    private static async Task<World> DraftExam(string? concept = "photosynthesis", ExamStatus status = ExamStatus.Draft)
    {
        var w = await Seed.ExamAsync(status: status);
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "photo", Title = "Photosynthesis", ConceptKey = concept };
        await using var db = OrgDb(w.Org);
        db.LearningObjectives.Add(objective);
        await db.SaveChangesAsync();
        return new World(w.Org, w.ExamId, objective.Id, objective.Key);
    }

    private OpeningQuestionService As(Guid org, string role, Guid user, out GadflyDbContext db)
    {
        var tenant = Member(org, user, role);
        db = Db(tenant);
        return new OpeningQuestionService(db, new FixedTenant(tenant), Panel, Clock);
    }

    private async Task<AssessmentResult> Approve(World w, string? text, string role = OrgRoles.Instructor, Guid? user = null)
    {
        var service = As(w.Org, role, user ?? Guid.NewGuid(), out var db);
        await using (db) return await service.ApproveAsync(w.ExamId, w.ObjectiveId, text);
    }

    private static async Task<IReadOnlyDictionary<string, string>> Stored(World w)
    {
        await using var db = SystemDb();
        var json = await db.Exams.AsNoTracking().Where(e => e.Id == w.ExamId).Select(e => e.PolicyJson).SingleAsync();
        return ExamService.ReadPolicy(json).OpeningQuestions;
    }

    private static async Task<int> Audits(World w, string action)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking().CountAsync(a => a.OrganizationId == w.Org && a.Action == action);
    }

    // ------------------------------------------------------------------ the text itself (checked before anything is loaded)

    [TestCase(null, "Write the opening question first")]
    [TestCase("   ", "Write the opening question first")]
    [TestCase("Explain photosynthesis?", "too short")]
    [TestCase("In your own words,\nwhat is photosynthesis?", "one line")]
    [TestCase("Tell me, how did you arrive at that answer about plants?", "cannot reliably report")]
    public async Task UnusableText_IsRefused_WithoutCallingThePanel(string? text, string reason)
    {
        var w = await DraftExam();
        var r = await Approve(w, text);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain(reason));
        Assert.That(Panel.Calls, Is.Empty);
        Assert.That(await Stored(w), Is.Empty);
    }

    [Test]
    public async Task OverlongText_IsRefused()
    {
        var w = await DraftExam();
        var r = await Approve(w, "Explain " + string.Join(" ", Enumerable.Repeat("photosynthesis", 60)) + "?");
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("spoken aloud"));
    }

    // ------------------------------------------------------------------ who and what

    [Test]
    public async Task AnExaminer_IsRefusedAndTheAttemptAudited()
    {
        var w = await DraftExam();
        var r = await Approve(w, Good, OrgRoles.Examiner);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Is.EqualTo(AssessmentGuard.Denied(Gadfly.Assessment.Permission.ManageAssessments)));
        Assert.That(await Audits(w, "access.denied"), Is.EqualTo(1));
    }

    [Test]
    public async Task APublishedAssessment_IsLocked()
    {
        var w = await DraftExam(status: ExamStatus.Published);
        var r = await Approve(w, Good);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("locked"));
    }

    [Test]
    public async Task AnObjectiveWithoutAConcept_MustBeLinkedFirst()
    {
        var w = await DraftExam(concept: null);
        Assert.That((await Approve(w, Good)).Message, Does.Contain("Link this objective to a source concept first"));
    }

    [Test]
    public async Task AConceptMissingFromTheMap_MustBeRelinked()
    {
        var w = await DraftExam(concept: "no-such-concept");
        Assert.That((await Approve(w, Good)).Message, Does.Contain("Relink it first"));
    }

    // ------------------------------------------------------------------ the leak guard fails closed

    [Test]
    public async Task AQuestionTheGuardSaysLeaks_IsNotSaved()
    {
        var w = await DraftExam();
        Panel.GuardVerdict = _ => "LEAKS";
        var r = await Approve(w, Good);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("states or hints at points"));
        Assert.That(Panel.Count(ScriptedPanel.Kind.Guard), Is.EqualTo(1));
        Assert.That(Panel.Calls.Single().Prompt, Does.Contain("Light energy becomes chemical energy"), "checked against the concept's rubric");
        Assert.That(await Stored(w), Is.Empty);
        Assert.That(await Audits(w, "objective.opening.approved"), Is.Zero);
    }

    [Test]
    public async Task AnUnclearVerdict_IsNotSaved()
    {
        var w = await DraftExam();
        Panel.GuardVerdict = _ => "";
        var r = await Approve(w, Good);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("could not confirm"));
        Assert.That(await Stored(w), Is.Empty);
    }

    [Test]
    public async Task APanelThatIsDown_IsNotSaved()
    {
        var w = await DraftExam();
        Panel.Fail = true;
        var r = await Approve(w, Good);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("could not confirm"));
        Assert.That(await Stored(w), Is.Empty);
    }

    // ------------------------------------------------------------------ approve, re-approve, remove

    [Test]
    public async Task ASafeQuestion_IsStoredInThePolicy_AndAudited()
    {
        var w = await DraftExam();
        var r = await Approve(w, "  " + Good + "  ");
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Id, Is.EqualTo(w.ExamId));
        Assert.That((await Stored(w))[w.Key], Is.EqualTo(Good));
        Assert.That(await Audits(w, "objective.opening.approved"), Is.EqualTo(1));

        var again = await Approve(w, Good);
        Assert.That(again.Ok, Is.True);
        Assert.That(again.Message, Does.Contain("already"));
        Assert.That(await Audits(w, "objective.opening.approved"), Is.EqualTo(1), "approving the same text again changes nothing");

        const string reworded = "Using your own example, how do plants store the energy they capture from light?";
        Assert.That((await Approve(w, reworded)).Ok, Is.True);
        Assert.That((await Stored(w))[w.Key], Is.EqualTo(reworded));
        Assert.That(await Audits(w, "objective.opening.approved"), Is.EqualTo(2));
    }

    [Test]
    public async Task Removing_GivesTheOpeningBackToThePanel_AndIsAudited()
    {
        var w = await DraftExam();
        Assert.That((await Approve(w, Good)).Ok, Is.True);
        var service = As(w.Org, OrgRoles.Instructor, Guid.NewGuid(), out var db);
        await using (db)
        {
            var removed = await service.RemoveAsync(w.ExamId, w.ObjectiveId);
            Assert.That(removed.Ok, Is.True, removed.Message);
            Assert.That(removed.Message, Does.StartWith("Removed"));
        }
        Assert.That(await Stored(w), Is.Empty);
        Assert.That(await Audits(w, "objective.opening.removed"), Is.EqualTo(1));

        var again = As(w.Org, OrgRoles.Instructor, Guid.NewGuid(), out var db2);
        await using (db2) Assert.That((await again.RemoveAsync(w.ExamId, w.ObjectiveId)).Message, Does.Contain("has no fixed opening question"));
        Assert.That(await Audits(w, "objective.opening.removed"), Is.EqualTo(1));
    }

    [Test]
    public async Task RemovingNeedsManageAssessments()
    {
        var w = await DraftExam();
        var service = As(w.Org, OrgRoles.Student, Guid.NewGuid(), out var db);
        await using (db) Assert.That((await service.RemoveAsync(w.ExamId, w.ObjectiveId)).Ok, Is.False);
        Assert.That(await Audits(w, "access.denied"), Is.EqualTo(1));
    }

    [Test]
    public void Drop_TakesOneObjectivesQuestionOut_AndLeavesTheOthers()
    {
        var exam = new Exam
        {
            PolicyJson = """{"openingQuestions":{"a":"Question for a, in your own words?","b":"Question for b, in your own words?"}}""",
        };
        Assert.That(OpeningQuestionService.Drop(exam, "a"), Is.EqualTo("Question for a, in your own words?"));
        var left = ExamService.ReadPolicy(exam.PolicyJson).OpeningQuestions;
        Assert.That(left.Keys, Is.EqualTo(new[] { "b" }));
        Assert.That(OpeningQuestionService.Drop(exam, "a"), Is.Null);
    }

    // ------------------------------------------------------------------ drafting

    [Test]
    public async Task Drafting_AsksThePanelForAnElicitQuestion_AndSavesNothing()
    {
        var w = await DraftExam();
        Panel.QuestionText = _ => "In your own words, what happens to light energy inside a leaf?";
        var service = As(w.Org, OrgRoles.Instructor, Guid.NewGuid(), out var db);
        OpeningQuestionDraft draft;
        await using (db) draft = await service.DraftAsync(w.ExamId, w.ObjectiveId);
        Assert.That(draft.Ok, Is.True, draft.Message);
        Assert.That(draft.Text, Is.Not.Null.And.Not.Empty);
        Assert.That(Panel.Count(ScriptedPanel.Kind.Question), Is.GreaterThanOrEqualTo(1));
        Assert.That(await Stored(w), Is.Empty);
    }

    [Test]
    public async Task Drafting_WithThePanelDown_SaysToWriteOneYourself()
    {
        var w = await DraftExam();
        Panel.Fail = true;
        var service = As(w.Org, OrgRoles.Instructor, Guid.NewGuid(), out var db);
        OpeningQuestionDraft draft;
        await using (db) draft = await service.DraftAsync(w.ExamId, w.ObjectiveId);
        Assert.That(draft.Ok, Is.False);
        Assert.That(draft.Message, Does.Contain("yourself"));
        Assert.That(draft.Text, Is.Null);
    }
}
