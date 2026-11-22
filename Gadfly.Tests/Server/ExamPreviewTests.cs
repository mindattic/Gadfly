using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// "Preview as a student" (GAD-T297) on LocalDB: the exam's own engine runs in memory on the mock panel, whatever the
/// deployment's panel is; nothing is written except one assessment.previewed audit row; only those who manage assessments may preview.
/// </summary>
[TestFixture]
public class ExamPreviewTests : ServerTest
{
    private async Task<ExamPreview> Start(Guid org, Guid examId, string role = OrgRoles.Instructor, Guid? user = null)
    {
        var tenant = Member(org, user ?? Guid.NewGuid(), role);
        await using var db = Db(tenant);
        return await ExamPreview.StartAsync(db, new FixedTenant(tenant), Clock, examId);
    }

    [Test]
    public async Task APreview_AsksAndTakesAnswers_InMemoryOnTheMockPanel()
    {
        var w = await Seed.ExamAsync();
        var preview = await Start(w.Org, w.ExamId);
        Assert.That(preview.Title, Is.EqualTo("Biology check"));
        Assert.That(preview.ExamId, Is.EqualTo(w.ExamId));

        var first = await preview.NextAsync();
        Assert.That(first, Is.Not.Null);
        Assert.That(first!.Question, Is.Not.Empty);
        Assert.That(preview.OpenTurn, Is.SameAs(first));

        await preview.AnswerAsync("Plants turn light energy into chemical energy stored in glucose.");
        var turns = preview.Turns();
        Assert.That(turns[0].Number, Is.EqualTo(1));
        Assert.That(turns[0].Answer, Does.Contain("glucose"));
        Assert.That(turns[0].ConceptTitle, Is.EqualTo("Photosynthesis"));

        preview.End();
        Assert.That(preview.IsEnded, Is.True);
        Assert.That(preview.EndReason, Is.EqualTo(nameof(Gadfly.Engine.EndReason.Abandoned)));
        Assert.That(await preview.AnswerAsync("too late"), Is.Null);

        Assert.That(Panel.Calls, Is.Empty, "a preview never uses the deployment's panel");
        await using var sys = SystemDb();
        Assert.That(await sys.ExamSessions.AsNoTracking().CountAsync(s => s.ExamId == w.ExamId), Is.Zero, "no session is written");
        Assert.That(await sys.AuditEvents.AsNoTracking().CountAsync(a => a.OrganizationId == w.Org && a.Action == "assessment.previewed" && a.EntityId == w.ExamId.ToString()),
            Is.EqualTo(1));
    }

    [Test]
    public async Task ABlankAnswer_CountsAsIDontKnow()
    {
        var w = await Seed.ExamAsync();
        var preview = await Start(w.Org, w.ExamId);
        Assert.That(await preview.NextAsync(), Is.Not.Null);
        await preview.AnswerAsync("   ");
        Assert.That(preview.Turns()[0].Answer, Is.EqualTo(SourceExamEngine.AporiaWord));
    }

    [Test]
    public async Task AnExaminer_CannotPreview_AndTheAttemptIsAudited()
    {
        var w = await Seed.ExamAsync();
        var examiner = Guid.NewGuid();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Start(w.Org, w.ExamId, OrgRoles.Examiner, examiner));
        Assert.That(ex!.Message, Does.Contain("ManageAssessments"));
        await using var sys = SystemDb();
        Assert.That(await sys.AuditEvents.AsNoTracking().CountAsync(a => a.Action == "access.denied" && a.ActorUserId == examiner), Is.EqualTo(1));
        Assert.That(await sys.AuditEvents.AsNoTracking().CountAsync(a => a.Action == "assessment.previewed" && a.EntityId == w.ExamId.ToString()), Is.Zero);
    }

    [Test]
    public async Task AnExamOfAnotherOrganization_NoLongerExists()
    {
        var w = await Seed.ExamAsync();
        var other = await Seed.OrganizationAsync();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Start(other, w.ExamId));
        Assert.That(ex!.Message, Does.Contain("no longer exists"));
    }

    [Test]
    public async Task AnExamWithoutAConceptMap_SaysToProcessTheSourceFirst()
    {
        var org = await Seed.OrganizationAsync();
        // A real source collection (an exam must reference one: FK_Exams_SourceCollections_CollectionId), never processed, so no concept map.
        var collectionId = Guid.NewGuid();
        await using (var db = OrgDb(org))
            await new SqlSourceStore(db, org, Guid.Empty).EnsureCollectionAsync(collectionId.ToString(), "Unprocessed source");
        var exam = new Exam { OrganizationId = org, CollectionId = collectionId, Title = "Unprocessed", Status = ExamStatus.Draft, PolicyJson = "{}", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = OrgDb(org))
        {
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
        }
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Start(org, exam.Id));
        Assert.That(ex!.Message, Does.Contain("Process the source material first"));
    }

    [TestCase(MoveKind.Elicit, "Opening question")]
    [TestCase(MoveKind.Hint, "Hint (practice)")]
    [TestCase(MoveKind.GuidedExplanation, "Explains, then asks again (practice)")]
    [TestCase(MoveKind.ScenarioVariation, "Scenario variation")]
    [TestCase(MoveKind.Apply, "Apply")]
    public void Moves_AreInPlainWords(MoveKind move, string words) => Assert.That(ExamPreview.Words(move), Is.EqualTo(words));
}
