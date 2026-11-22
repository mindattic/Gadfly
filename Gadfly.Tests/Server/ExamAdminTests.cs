using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The exams manager (GAD-T297): the deep copy behind Duplicate, the versioned JSON definition (round trip and validation),
/// and on LocalDB that duplicating copies objectives, criteria, levels and policy but never assignments or attempts.
/// </summary>
[TestFixture]
public class ExamAdminTests : ServerTest
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static (Exam Exam, List<LearningObjective> Objectives, GradingPolicy Grading) Sample()
    {
        var org = Guid.NewGuid();
        var exam = new Exam
        {
            OrganizationId = org, CollectionId = Guid.NewGuid(), CourseId = Guid.NewGuid(), Title = "Cell biology", Subject = "Biology", Topic = "Cells",
            Instructions = "Speak clearly.", AssessmentMode = AssessmentMode.Examination, Mode = ExamMode.Voice, IntegrityLevel = IntegrityLevel.Strict,
            VoiceId = "examinerVoice01", AvatarId = "avatar-1", Status = ExamStatus.Published, PublishedAt = DateTimeOffset.UtcNow,
            PolicyJson = JsonSerializer.Serialize(new SessionPolicy
            {
                ConceptIds = ["photosynthesis", "respiration"], TimeBudget = TimeSpan.FromMinutes(25), MaxTurns = 14, Difficulty = 4,
                ReadOutCheck = false, MinSummativeObjectives = 2, Seed = 42,
            }, Camel),
        };
        var objectives = new List<LearningObjective>();
        for (var i = 0; i < 2; i++)
        {
            var o = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = $"obj-{i + 1}", Title = $"Objective {i + 1}", Description = "Explain it.",
                ConceptKey = i == 0 ? "photosynthesis" : "respiration", Weight = 1 + i, MaxScore = 50, Ordinal = i,
                HintLadderJson = JsonSerializer.Serialize(new[] { "Think about light.", "What do leaves need?" }),
            };
            var c = new RubricCriterion { OrganizationId = org, ObjectiveId = o.Id, Dimension = AssessmentDimension.ConceptualUnderstanding, Name = "Accuracy", Weight = 2, Ordinal = 0 };
            foreach (var level in Enum.GetValues<MasteryLevel>())
                c.Levels.Add(new RubricLevelDescriptor { OrganizationId = org, CriterionId = c.Id, Level = level, Descriptor = $"Level {(int)level} for {o.Key}" });
            o.Criteria.Add(c);
            objectives.Add(o);
        }
        var grading = new GradingPolicy { ExamId = exam.Id, OrganizationId = org, AiWeight = 0.3m, HumanWeight = 0.7m, BlindHumanGrading = true, LockedAt = DateTimeOffset.UtcNow };
        return (exam, objectives, grading);
    }

    // ------------------------------------------------------------------ duplicate (pure)

    [Test]
    public void CopyOf_IsADeepCopyAsADraft_WithNewIdsAndAnUnlockedPolicy()
    {
        var (exam, objectives, grading) = Sample();
        var copy = ExamAdminService.CopyOf(exam, objectives, grading, "Cell biology (copy)", Guid.NewGuid(), DateTimeOffset.UtcNow, seed: 7);

        Assert.That(copy.Exam.Id, Is.Not.EqualTo(exam.Id));
        Assert.That(copy.Exam.Status, Is.EqualTo(ExamStatus.Draft));
        Assert.That(copy.Exam.PublishedAt, Is.Null);
        Assert.That(copy.Exam.Title, Is.EqualTo("Cell biology (copy)"));
        Assert.That((copy.Exam.OrganizationId, copy.Exam.CollectionId, copy.Exam.CourseId), Is.EqualTo((exam.OrganizationId, exam.CollectionId, exam.CourseId)));
        Assert.That((copy.Exam.IntegrityLevel, copy.Exam.VoiceId, copy.Exam.AvatarId, copy.Exam.Instructions),
            Is.EqualTo((exam.IntegrityLevel, exam.VoiceId, exam.AvatarId, exam.Instructions)));

        var p = Gadfly.Server.Exams.ExamService.ReadPolicy(copy.Exam.PolicyJson);
        Assert.That(p.Seed, Is.EqualTo(7), "the copy gets its own seed");
        Assert.That(p.ConceptIds, Is.EqualTo(new[] { "photosynthesis", "respiration" }));
        Assert.That((p.TimeBudget, p.MaxTurns, p.ReadOutCheck, p.MinSummativeObjectives), Is.EqualTo((TimeSpan.FromMinutes(25), 14, false, 2)));

        Assert.That(copy.Grading.ExamId, Is.EqualTo(copy.Exam.Id));
        Assert.That(copy.Grading.LockedAt, Is.Null, "a draft's grading policy is not locked");
        Assert.That((copy.Grading.AiWeight, copy.Grading.HumanWeight), Is.EqualTo((0.3m, 0.7m)));

        Assert.That(copy.Objectives, Has.Count.EqualTo(2));
        for (var i = 0; i < 2; i++)
        {
            var (a, b) = (objectives[i], copy.Objectives[i]);
            Assert.That(b.Id, Is.Not.EqualTo(a.Id));
            Assert.That(b.ExamId, Is.EqualTo(copy.Exam.Id));
            Assert.That((b.Key, b.Title, b.ConceptKey, b.Weight, b.MaxScore, b.HintLadderJson), Is.EqualTo((a.Key, a.Title, a.ConceptKey, a.Weight, a.MaxScore, a.HintLadderJson)));
            var (ca, cb) = (a.Criteria.Single(), b.Criteria.Single());
            Assert.That(cb.Id, Is.Not.EqualTo(ca.Id));
            Assert.That(cb.ObjectiveId, Is.EqualTo(b.Id));
            Assert.That(cb.Levels.Select(l => (l.Level, l.Descriptor)), Is.EqualTo(ca.Levels.Select(l => (l.Level, l.Descriptor))));
            Assert.That(cb.Levels.All(l => l.CriterionId == cb.Id), Is.True);
        }
    }

    // ------------------------------------------------------------------ JSON definition (pure)

    [Test]
    public void Definition_RoundTrips_ThroughJson()
    {
        var (exam, objectives, grading) = Sample();
        var json = ExamDefinitionJson.Serialize(ExamDefinitionJson.Build(exam, objectives, grading, "Biology 101", "Biology notes", DateTimeOffset.UtcNow));
        var read = ExamDefinitionJson.Parse(json);

        Assert.That(read.Errors, Is.Empty);
        Assert.That(read.Ok, Is.True);
        var d = read.Definition!;
        Assert.That((d.Title, d.Subject, d.Topic, d.Instructions), Is.EqualTo((exam.Title, exam.Subject, exam.Topic, exam.Instructions)));
        Assert.That((d.IntegrityLevel, d.Mode, d.AssessmentMode), Is.EqualTo(("Strict", "Voice", "Examination")));
        Assert.That((d.CourseTitle, d.CollectionTitle), Is.EqualTo(("Biology 101", "Biology notes")));

        var policy = ExamDefinitionJson.ToPolicy(d.Settings, new SessionPolicy());
        Assert.That((policy.TimeBudget, policy.MaxTurns, policy.Difficulty, policy.ReadOutCheck, policy.MinSummativeObjectives),
            Is.EqualTo((TimeSpan.FromMinutes(25), 14, 4, false, 2)));
        Assert.That(policy.ConceptIds, Is.EqualTo(new[] { "photosynthesis", "respiration" }));
        Assert.That(json, Does.Not.Contain("\"seed\""), "the seed is never exported");

        var g = ExamDefinitionJson.ToSnapshot(d.GradingPolicy);
        Assert.That((g.AiWeight, g.HumanWeight, g.BlindHumanGrading), Is.EqualTo((0.3m, 0.7m, true)));

        Assert.That(d.Objectives!.Select(o => o.Key), Is.EqualTo(new[] { "obj-1", "obj-2" }));
        Assert.That(d.Objectives![0].HintLadder, Is.EqualTo(new[] { "Think about light.", "What do leaves need?" }));
        Assert.That(d.Objectives![1].Criteria!.Single().Levels, Has.Count.EqualTo(6));
        Assert.That(d.Objectives![1].Criteria!.Single().Levels![5], Is.EqualTo("Level 5 for obj-2"));
    }

    [Test]
    public void Parse_ListsEveryProblem_NotJustTheFirst()
    {
        var json = """
        {
          "format": "gadfly.exam", "version": 1,
          "exam": {
            "title": "",
            "integrityLevel": "Paranoid",
            "settings": { "durationMinutes": 0 },
            "gradingPolicy": { "inconclusiveProcedure": "Coin toss" },
            "objectives": [
              { "key": "a", "title": "One", "criteria": [ { "dimension": "Vibes", "name": "", "levels": ["x"] } ] },
              { "key": "a", "title": "" }
            ]
          }
        }
        """;
        var read = ExamDefinitionJson.Parse(json);
        Assert.That(read.Ok, Is.False);
        Assert.That(read.Definition, Is.Null);
        var all = string.Join("\n", read.Errors);
        Assert.That(all, Does.Contain("needs a title"));
        Assert.That(all, Does.Contain("integrity level"));
        Assert.That(all, Does.Contain("duration"));
        Assert.That(all, Does.Contain("inconclusive procedure"));
        Assert.That(all, Does.Contain("not a known dimension"));
        Assert.That(all, Does.Contain("give it a name"));
        Assert.That(all, Does.Contain("six levels"));
        Assert.That(all, Does.Contain("keys must be unique"));
        Assert.That(all, Does.Contain("Objective 2 (a): give it a title"));
    }

    [TestCase("", "empty")]
    [TestCase("{ not json", "not valid JSON")]
    [TestCase("""{ "format": "something.else", "version": 1, "exam": { "title": "x" } }""", "not a Gadfly exam file")]
    [TestCase("""{ "format": "gadfly.exam", "version": 99, "exam": { "title": "x" } }""", "newer than this app reads")]
    [TestCase("""{ "format": "gadfly.exam", "version": 1 }""", "no \"exam\" section")]
    public void Parse_RefusesFilesThatAreNotExamDefinitions(string json, string expected)
    {
        var read = ExamDefinitionJson.Parse(json);
        Assert.That(read.Ok, Is.False);
        Assert.That(string.Join("\n", read.Errors), Does.Contain(expected));
    }

    [Test]
    public void FileName_IsSafe()
    {
        Assert.That(ExamDefinitionJson.FileName("Cell Biology: Unit 3/4"), Is.EqualTo("cell-biology-unit-3-4.gadfly-exam.json"));
        Assert.That(ExamDefinitionJson.FileName("   "), Is.EqualTo("exam.gadfly-exam.json"));
    }

    // ------------------------------------------------------------------ duplicate (LocalDB)

    [Test]
    public async Task Duplicate_CopiesTheExamItsObjectivesAndPolicy_NeverItsAssignmentsOrAttempts()
    {
        var w = await Seed.ExamAsync();
        await using (var db = OrgDb(w.Org))
        {
            var o = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "obj-1", Title = "Photosynthesis", ConceptKey = "photosynthesis" };
            var c = new RubricCriterion { OrganizationId = w.Org, ObjectiveId = o.Id, Name = "Accuracy", Dimension = AssessmentDimension.ConceptualUnderstanding };
            db.LearningObjectives.Add(o);
            db.RubricCriteria.Add(c);
            foreach (var level in Enum.GetValues<MasteryLevel>())
                db.RubricLevelDescriptors.Add(new RubricLevelDescriptor { OrganizationId = w.Org, CriterionId = c.Id, Level = level, Descriptor = $"L{(int)level}" });
            db.GradingPolicies.Add(new GradingPolicy { ExamId = w.ExamId, OrganizationId = w.Org, AiWeight = 0.5m, HumanWeight = 0.5m, LockedAt = DateTimeOffset.UtcNow });
            db.ExamSessions.Add(new ExamSession
            {
                OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, State = SessionState.Ended,
                CreatedAt = DateTimeOffset.UtcNow, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20), EndedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        Gadfly.Server.Assessments.AssessmentResult r;
        await using (var scope = Scope(admin))
            r = await ActivatorUtilities.CreateInstance<ExamAdminService>(scope.ServiceProvider).DuplicateAsync(w.ExamId);
        Assert.That(r.Ok, Is.True, r.Message);
        var copyId = r.Id!.Value;

        await using var check = SystemDb();
        var copy = await check.Exams.SingleAsync(e => e.Id == copyId);
        Assert.That(copy.Status, Is.EqualTo(ExamStatus.Draft));
        Assert.That(copy.Title, Is.EqualTo("Biology check (copy)"));
        Assert.That(await check.ExamAssignments.CountAsync(a => a.ExamId == copyId), Is.Zero, "assignments stay with the original");
        Assert.That(await check.ExamSessions.CountAsync(s => s.ExamId == copyId), Is.Zero, "attempts stay with the original");
        var objective = await check.LearningObjectives.Include(o => o.Criteria).ThenInclude(c => c.Levels).SingleAsync(o => o.ExamId == copyId);
        Assert.That(objective.Criteria.Single().Levels, Has.Count.EqualTo(6));
        var policy = await check.GradingPolicies.SingleAsync(p => p.ExamId == copyId);
        Assert.That((policy.AiWeight, policy.LockedAt), Is.EqualTo((0.5m, (DateTimeOffset?)null)));
        Assert.That(await check.AuditEvents.CountAsync(a => a.Action == "assessment.duplicated" && a.EntityId == copyId.ToString()), Is.EqualTo(1));
        Assert.That(await check.ExamSessions.CountAsync(s => s.ExamId == w.ExamId), Is.EqualTo(1), "the original keeps its attempt");
    }

    [Test]
    public async Task ArchiveThenRestore_ReturnsAPublishedExamToPublished_AndAuditsBoth()
    {
        var w = await Seed.ExamAsync();
        await using (var db = OrgDb(w.Org))
            await db.Exams.Where(e => e.Id == w.ExamId).ExecuteUpdateAsync(u => u.SetProperty(e => e.PublishedAt, DateTimeOffset.UtcNow));
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Owner);
        await using (var scope = Scope(admin))
        {
            var svc = ActivatorUtilities.CreateInstance<ExamAdminService>(scope.ServiceProvider);
            Assert.That((await svc.ArchiveAsync(w.ExamId)).Ok, Is.True);
            Assert.That((await svc.RestoreAsync(w.ExamId)).Ok, Is.True);
        }
        await using var check = SystemDb();
        Assert.That((await check.Exams.SingleAsync(e => e.Id == w.ExamId)).Status, Is.EqualTo(ExamStatus.Published));
        Assert.That(await check.AuditEvents.CountAsync(a => a.EntityId == w.ExamId.ToString() && (a.Action == "assessment.archived" || a.Action == "assessment.restored")), Is.EqualTo(2));
    }
}
