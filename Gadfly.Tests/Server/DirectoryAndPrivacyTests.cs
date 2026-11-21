using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Privacy;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Organization directory rules (GAD-T41/T76/T106): role validation, owner protection, outside accounts.</summary>
[TestFixture]
public class OrganizationDirectoryTests : ServerTest
{
    private static async Task<T> WithDirectory<T>(Func<IOrganizationDirectory, Task<T>> act)
    {
        await using var scope = Scope(TenantSnapshot.System);
        return await act(scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>());
    }

    private static Task<DirectoryResult> Add(Guid org, string user, string role, string? acting = null) =>
        WithDirectory(d => d.AddMemberAsync(org, user, null, role, Seed.StrongPassword, actingRole: acting));

    private static async Task<MindAttic.Authentication.Entities.AuthMembership> MemberRow(Guid org, Guid user)
    {
        await using var db = SystemDb();
        return await db.OrganizationMembers.AsNoTracking().SingleAsync(m => m.OrganizationId == org && m.UserId == user);
    }

    [TestCase("Teacher")]
    [TestCase("student")]
    [TestCase("")]
    [TestCase("SuperAdmin")]
    public async Task InvalidRoles_AreRefused(string role)
    {
        var org = await Seed.OrganizationAsync();
        var r = await Add(org, Seed.UserName(), role);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("is not a role"));
    }

    [Test]
    public async Task EveryPersonRole_CanBeAdded()
    {
        var org = await Seed.OrganizationAsync();
        foreach (var role in OrgRoles.All)
        {
            var r = await Add(org, Seed.UserName(), role, OrgRoles.Owner);
            Assert.That(r.Ok, Is.True, $"{role}: {r.Message}");
            Assert.That((await MemberRow(org, r.Id!.Value)).Role, Is.EqualTo(role));
        }
    }

    [Test]
    public async Task OnlyAnOwnerAddsAnOwner()
    {
        var org = await Seed.OrganizationAsync();
        var byAdmin = await Add(org, Seed.UserName(), OrgRoles.Owner, OrgRoles.Admin);
        Assert.That(byAdmin.Ok, Is.False);
        Assert.That(byAdmin.Message, Does.Contain("Only an owner"));
        Assert.That((await Add(org, Seed.UserName(), OrgRoles.Owner, OrgRoles.Owner)).Ok, Is.True);
        Assert.That((await Add(org, Seed.UserName(), OrgRoles.Owner, null)).Ok, Is.True, "platform staff and system callers may");
    }

    [Test]
    public async Task OnlyAnOwnerChangesAnOwnersRole_AndTheLastOwnerStays()
    {
        var org = await Seed.OrganizationAsync();
        var ownerName = Seed.UserName();
        var owner = await Add(org, ownerName, OrgRoles.Owner);
        var demoteByAdmin = await Add(org, ownerName, OrgRoles.Admin, OrgRoles.Admin);
        Assert.That(demoteByAdmin.Ok, Is.False);
        Assert.That(demoteByAdmin.Message, Does.Contain("Only an owner can change an owner's role"));
        var demoteLast = await Add(org, ownerName, OrgRoles.Admin, OrgRoles.Owner);
        Assert.That(demoteLast.Ok, Is.False);
        Assert.That(demoteLast.Message, Does.Contain("at least one active owner"));
        Assert.That((await MemberRow(org, owner.Id!.Value)).Role, Is.EqualTo(OrgRoles.Owner));

        await Add(org, Seed.UserName(), OrgRoles.Owner, OrgRoles.Owner);
        Assert.That((await Add(org, ownerName, OrgRoles.Admin, OrgRoles.Owner)).Ok, Is.True, "with a second owner, the first may step down");
    }

    [Test]
    public async Task RemovingOwners_IsProtected()
    {
        var org = await Seed.OrganizationAsync();
        var owner = await Add(org, Seed.UserName(), OrgRoles.Owner);
        var member = await MemberRow(org, owner.Id!.Value);
        var byAdmin = await WithDirectory(d => d.SetMemberActiveAsync(org, member.Id, false, actingRole: OrgRoles.Admin));
        Assert.That(byAdmin.Ok, Is.False);
        var last = await WithDirectory(d => d.SetMemberActiveAsync(org, member.Id, false, actingRole: OrgRoles.Owner));
        Assert.That(last.Ok, Is.False);
        Assert.That(last.Message, Does.Contain("at least one active owner"));
        Assert.That((await MemberRow(org, owner.Id!.Value)).IsActive, Is.True);
    }

    [Test]
    public async Task RemovingAMember_DeactivatesAndRotatesTheirSecurityStamp()
    {
        var org = await Seed.OrganizationAsync();
        var student = await Add(org, Seed.UserName(), OrgRoles.Student);
        string stampBefore;
        await using (var db = SystemDb())
            stampBefore = (await db.AuthUsers.AsNoTracking().SingleAsync(u => u.Id == student.Id)).SecurityStamp;
        var member = await MemberRow(org, student.Id!.Value);
        var r = await WithDirectory(d => d.SetMemberActiveAsync(org, member.Id, false, actingRole: OrgRoles.Admin));
        Assert.That(r.Ok, Is.True);
        Assert.That((await MemberRow(org, student.Id!.Value)).IsActive, Is.False);
        await using (var db = SystemDb())
            Assert.That((await db.AuthUsers.AsNoTracking().SingleAsync(u => u.Id == student.Id)).SecurityStamp, Is.Not.EqualTo(stampBefore));
        Assert.That((await WithDirectory(d => d.SetMemberActiveAsync(org, member.Id, true, actingRole: OrgRoles.Admin))).Ok, Is.True);
    }

    [Test]
    public async Task AMemberOfAnotherOrganization_IsNotFound()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        var student = await Add(other, Seed.UserName(), OrgRoles.Student);
        var member = await MemberRow(other, student.Id!.Value);
        var r = await WithDirectory(d => d.SetMemberActiveAsync(org, member.Id, false, actingRole: OrgRoles.Owner));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task OrgAdmins_CannotPullInAnOutsideAccount()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        var name = Seed.UserName();
        await Add(other, name, OrgRoles.Student);
        var r = await Add(org, name, OrgRoles.Student, OrgRoles.Admin);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("existing Gadfly account"));
        await using var db = SystemDb();
        Assert.That(await db.OrganizationMembers.CountAsync(m => m.OrganizationId == org), Is.Zero);
        Assert.That((await Add(org, name, OrgRoles.Student, null)).Ok, Is.True, "platform staff may");
    }

    [Test]
    public async Task ChangingARole_UpdatesTheMembership()
    {
        var org = await Seed.OrganizationAsync();
        var name = Seed.UserName();
        var s = await Add(org, name, OrgRoles.Student, OrgRoles.Admin);
        var r = await Add(org, name, OrgRoles.Examiner, OrgRoles.Admin);
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That((await MemberRow(org, s.Id!.Value)).Role, Is.EqualTo(OrgRoles.Examiner));
    }

    [Test]
    public async Task UnknownOrBlankInput_IsRefused()
    {
        Assert.That((await Add(Guid.NewGuid(), Seed.UserName(), OrgRoles.Student)).Message, Does.Contain("does not exist"));
        var org = await Seed.OrganizationAsync();
        Assert.That((await Add(org, "   ", OrgRoles.Student)).Ok, Is.False);
    }

    [Test]
    public async Task AccountTextOverTheColumnLimitOrNotAnEmail_IsRefusedInsteadOfCrashing()
    {
        var org = await Seed.OrganizationAsync();
        var longName = new string('u', 257);
        var longEmail = new string('e', 250) + "@example.test";
        var tooLongName = await WithDirectory(d => d.AddMemberAsync(org, longName, null, OrgRoles.Student, Seed.StrongPassword));
        Assert.That(tooLongName.Ok, Is.False);
        Assert.That(tooLongName.Message, Does.Contain("256"));
        var tooLongEmail = await WithDirectory(d => d.AddMemberAsync(org, Seed.UserName(), longEmail, OrgRoles.Student, Seed.StrongPassword));
        Assert.That(tooLongEmail.Ok, Is.False);
        var notAnEmail = await WithDirectory(d => d.AddMemberAsync(org, Seed.UserName(), "not an address", OrgRoles.Student, Seed.StrongPassword));
        Assert.That(notAnEmail.Ok, Is.False);
        Assert.That(notAnEmail.Message, Does.Contain("email"));

        var name = "Limits " + Guid.NewGuid().ToString("N")[..6];
        Assert.That((await WithDirectory(d => d.CreateOrganizationAsync(name, longName, "x@example.test", Seed.StrongPassword, Guid.Empty))).Ok, Is.False);
        Assert.That((await WithDirectory(d => d.CreateOrganizationAsync(name, Seed.UserName(), longEmail, Seed.StrongPassword, Guid.Empty))).Ok, Is.False);
        Assert.That((await WithDirectory(d => d.CreateOrganizationAsync(new string('o', 201), Seed.UserName(), "x@example.test", Seed.StrongPassword, Guid.Empty))).Ok, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.Organizations.AnyAsync(o => o.Name == name), Is.False, "nothing was created");

        Assert.That((await WithDirectory(d => d.InviteAsync(org, longEmail, OrgRoles.Student, null, null))).Ok, Is.False);
        Assert.That((await WithDirectory(d => d.InviteAsync(org, "nobody", OrgRoles.Student, null, null))).Ok, Is.False);
    }

    [Test]
    public async Task AValidEmailAndAUserNameAtTheLimit_AreAccepted()
    {
        var org = await Seed.OrganizationAsync();
        var r = await WithDirectory(d => d.AddMemberAsync(org, Seed.UserName(), "someone@example.test", OrgRoles.Student, Seed.StrongPassword));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(OrganizationDirectory.IsValidEmail("someone@example.test"), Is.True);
        Assert.That(OrganizationDirectory.IsValidEmail(new string('e', 244) + "@example.test"), Is.False, "257 characters");
    }

    [Test]
    public async Task CreateOrganization_MakesItsOwner_AndRefusesDuplicates()
    {
        var name = "Academy " + Guid.NewGuid().ToString("N")[..6];
        var owner = Seed.UserName();
        var r = await WithDirectory(d => d.CreateOrganizationAsync(name, owner, owner + "@example.test", Seed.StrongPassword, Guid.Empty));
        Assert.That(r.Ok, Is.True, r.Message);
        await using (var db = SystemDb())
        {
            var members = await db.OrganizationMembers.Where(m => m.OrganizationId == r.Id).ToListAsync();
            Assert.That(members.Single().Role, Is.EqualTo(OrgRoles.Owner));
            Assert.That(await db.OrganizationSettings.AnyAsync(s => s.OrganizationId == r.Id), Is.True);
        }
        var again = await WithDirectory(d => d.CreateOrganizationAsync(name.ToUpperInvariant(), Seed.UserName(), "x@example.test", Seed.StrongPassword, Guid.Empty));
        Assert.That(again.Ok, Is.False);
        var tooShort = await WithDirectory(d => d.CreateOrganizationAsync(" x ", Seed.UserName(), "x@example.test", Seed.StrongPassword, Guid.Empty));
        Assert.That(tooShort.Ok, Is.False);
    }

    [Test]
    public async Task CreateOrganization_WithABadOwnerPassword_LeavesNothingBehind()
    {
        var name = "Rollback " + Guid.NewGuid().ToString("N")[..6];
        var r = await WithDirectory(d => d.CreateOrganizationAsync(name, Seed.UserName(), "x@example.test", "short", Guid.Empty));
        Assert.That(r.Ok, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.Organizations.AnyAsync(o => o.Name == name), Is.False);
    }
}

/// <summary>Retention redaction, data-subject export and erasure (GAD-T61).</summary>
[TestFixture]
public class PrivacyServiceTests : ServerTest
{
    private const string Words = "my very private answer about chlorophyll";

    private static async Task<T> WithPrivacy<T>(Guid org, Func<PrivacyService, Task<T>> act)
    {
        await using var scope = Scope(TenantSnapshot.ForOrganization(org));
        return await act(scope.ServiceProvider.GetRequiredService<PrivacyService>());
    }

    [Test]
    public async Task Redaction_RemovesTheWords_KeepsTheScores()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        await WithPrivacy(w.Org, async p => { await p.RedactSessionAsync(sessionId); return 0; });

        await using var db = SystemDb();
        var turn = await db.SessionTurns.AsNoTracking().SingleAsync(t => t.Id == turnId);
        Assert.That(turn.AnswerText, Is.EqualTo("[redacted after the retention period]"));
        Assert.That(turn.WordsJson, Is.Null);
        Assert.That(turn.SpeechMetricsJson, Is.Null);
        Assert.That(turn.AudioRef, Is.Null);
        Assert.That(turn.MasteryAfter, Is.EqualTo(0.7), "the outcome stays");
        Assert.That(turn.Question, Is.EqualTo("What is photosynthesis?"));

        var judgment = await db.Judgments.AsNoTracking().Include(j => j.Votes).SingleAsync(j => j.TurnId == turnId);
        Assert.That(judgment.OverallScore, Is.EqualTo(0.8));
        Assert.That(judgment.Summary, Does.Not.Contain(Words));
        Assert.That(judgment.CriteriaJson, Does.Not.Contain(Words));
        Assert.That(judgment.CriteriaJson, Does.Contain("0.8"), "criterion scores stay");
        Assert.That(judgment.Votes.Single().Verdict, Does.Not.Contain(Words));
        Assert.That(judgment.Votes.Single().OverallScore, Is.EqualTo(0.8));

        Assert.That(await db.SessionClaims.AnyAsync(c => c.SessionId == sessionId), Is.False);
        var report = await db.SessionReports.AsNoTracking().SingleAsync(r => r.SessionId == sessionId);
        Assert.That(report.ReportJson, Does.Not.Contain(Words));
        Assert.That(report.Markdown, Does.Not.Contain(Words));
        Assert.That(report.OverallScore, Is.EqualTo(0.7));
        Assert.That(await db.GradeDecisions.AnyAsync(g => g.SessionId == sessionId), Is.True);
        Assert.That((await db.IntegrityEvents.AsNoTracking().SingleAsync(e => e.SessionId == sessionId)).DataJson, Is.Null);

        var session = await db.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.That(session.TranscriptJson, Is.Null);
        Assert.That(session.LedgerJson, Is.Null);
        Assert.That(session.ClientInfo, Is.Null);
        Assert.That(session.RedactedAt, Is.Not.Null);
        Assert.That(session.EndReason, Is.EqualTo("Mastery"));
    }

    [Test]
    public async Task Redaction_AlsoRemovesTheWordsFromTheResponseLedger()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        await using (var db = OrgDb(w.Org))
        {
            db.StudentResponses.Add(new StudentResponse
            {
                OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, OriginalText = Words, VerifiedText = Words,
                SubmittedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await WithPrivacy(w.Org, async p => { await p.RedactSessionAsync(sessionId); return 0; });
        await using var check = SystemDb();
        var stillThere = await check.StudentResponses.AsNoTracking()
            .Where(r => r.SessionId == sessionId && (r.OriginalText.Contains("chlorophyll") || r.VerifiedText.Contains("chlorophyll"))).AnyAsync();
        Assert.That(stillThere, Is.False, "retention leaves the student's words in StudentResponse after the retention period");
    }

    [Test]
    public async Task Redaction_RemovesTheClarificationQuestionsRepliesAndTheirAuditJson()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        var response = new StudentResponse
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, OriginalText = Words, VerifiedText = Words,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        await using (var db = OrgDb(w.Org))
        {
            db.StudentResponses.Add(response);
            db.ClarificationEvents.Add(new ClarificationEvent
            {
                OrganizationId = w.Org, SessionId = sessionId, ResponseId = response.Id, Type = ClarificationType.Transcription,
                Question = "I recorded your answer as: \"" + Words + "\" Is this what you said?", StudentReply = Words, AskedAt = DateTimeOffset.UtcNow,
            });
            foreach (var action in new[] { "clarification.asked", "clarification.answered" })
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganizationId = w.Org, SessionId = sessionId, Action = action, EntityType = nameof(StudentResponse), EntityId = response.Id.ToString(),
                    AfterJson = "{\"text\":\"" + Words + "\"}", At = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }
        await WithPrivacy(w.Org, async p => { await p.RedactSessionAsync(sessionId); return 0; });
        await using var check = SystemDb();
        Assert.That(await check.ClarificationEvents.AnyAsync(c => c.SessionId == sessionId && (c.Question.Contains("chlorophyll") || (c.StudentReply ?? "").Contains("chlorophyll"))), Is.False);
        Assert.That(await check.AuditEvents.AnyAsync(a => a.SessionId == sessionId && a.Action.StartsWith("clarification.") && a.AfterJson != null), Is.False);
    }

    [Test]
    public async Task Redaction_RemovesTheQuotesFromAiObjectiveGrades_KeepsLevelsAndScores()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync(Words);
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "obj-1", Title = "Photosynthesis", MaxScore = 10m, Weight = 1m };
        var detail = new Gadfly.Server.Grading.StoredAiDetail(
            [new Gadfly.Sources.Grading.CriterionGrade("Conceptual understanding", Gadfly.Assessment.AssessmentDimension.ConceptualUnderstanding,
                Gadfly.Assessment.MasteryLevel.Application, 7m, 10m, "They said " + Words, [], [Words], [3], false)],
            [new Gadfly.Sources.Grading.VoterGrade("mock", "mock", 7m, Gadfly.Assessment.MasteryLevel.Application, "Quoted: " + Words, false)]);
        await using (var db = OrgDb(w.Org))
        {
            db.LearningObjectives.Add(objective);
            db.AiObjectiveGrades.Add(new AiObjectiveGrade
            {
                OrganizationId = w.Org, SessionId = sessionId, ObjectiveId = objective.Id, Level = Gadfly.Assessment.MasteryLevel.Application, Score = 7m, MaxScore = 10m,
                Explanation = "The student wrote: " + Words, CriteriaJson = System.Text.Json.JsonSerializer.Serialize(detail, new System.Text.Json.JsonSerializerOptions
                    { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }),
                MisconceptionsJson = "[{\"text\":\"" + Words + "\",\"voters\":1}]", AmbiguitiesJson = "[\"" + Words + "\"]", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await WithPrivacy(w.Org, async p => { await p.RedactSessionAsync(sessionId); return 0; });
        await using var check = SystemDb();
        var row = await check.AiObjectiveGrades.AsNoTracking().SingleAsync(g => g.SessionId == sessionId);
        Assert.That(row.Explanation + row.CriteriaJson + row.MisconceptionsJson + row.AmbiguitiesJson, Does.Not.Contain("chlorophyll"));
        Assert.That(row.Score, Is.EqualTo(7m));
        Assert.That(row.Level, Is.EqualTo(Gadfly.Assessment.MasteryLevel.Application));
        Assert.That(row.CriteriaJson, Does.Contain("Conceptual understanding"));
    }

    [Test]
    public async Task Retention_RedactsOnlySessionsPastTheirOrganizationsPeriod()
    {
        var old = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-91));
        var recent = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-10));
        var shortPolicy = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await using (var db = SystemDb())
        {
            (await db.OrganizationSettings.SingleAsync(s => s.OrganizationId == shortPolicy.World.Org)).RetentionDays = 7;
            await db.SaveChangesAsync();
        }
        await using (var scope = Scope(TenantSnapshot.System))
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().ApplyRetentionAsync();

        await using var check = SystemDb();
        async Task<bool> Redacted(Guid id) => (await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id)).RedactedAt is not null;
        Assert.That(await Redacted(old.SessionId), Is.True, "90-day default");
        Assert.That(await Redacted(recent.SessionId), Is.False);
        Assert.That(await Redacted(shortPolicy.SessionId), Is.True, "the organization's own 7-day period");
    }

    [Test]
    public async Task Retention_RedactsAnAbandonedSessionThatWasNeverEnded()
    {
        var abandoned = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-200));
        var inProgress = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-1));
        await using (var db = SystemDb())
        {
            await db.ExamSessions.Where(s => s.Id == abandoned.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.State, SessionState.Running).SetProperty(s => s.EndedAt, (DateTimeOffset?)null).SetProperty(s => s.EndReason, (string?)null));
            await db.ExamSessions.Where(s => s.Id == inProgress.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.State, SessionState.Running).SetProperty(s => s.EndedAt, (DateTimeOffset?)null).SetProperty(s => s.EndReason, (string?)null));
        }
        await using (var scope = Scope(TenantSnapshot.System))
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().ApplyRetentionAsync();

        await using var check = SystemDb();
        var row = await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == abandoned.SessionId);
        Assert.That(row.RedactedAt, Is.Not.Null, "never ended, started 200 days ago");
        Assert.That(row.State, Is.EqualTo(SessionState.Failed), "a redacted session cannot be resumed");
        Assert.That(await check.SessionTurns.AsNoTracking().Where(t => t.SessionId == abandoned.SessionId).Select(t => t.AnswerText).SingleAsync(), Does.Not.Contain("chlorophyll"));
        Assert.That((await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == inProgress.SessionId)).RedactedAt, Is.Null, "a session in progress is left alone");
    }

    [Test]
    public async Task Export_ContainsEverythingAboutTheStudent_AndNothingElse()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync(Words);
        var other = await Seed.EndedSessionAsync("someone else's words");
        var json = await WithPrivacy(w.Org, p => p.ExportStudentAsync(w.Org, w.StudentId));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("studentUserId").GetGuid(), Is.EqualTo(w.StudentId));
        Assert.That(root.GetProperty("assignments").GetArrayLength(), Is.EqualTo(1));
        var session = root.GetProperty("sessions")[0];
        Assert.That(session.GetProperty("id").GetGuid(), Is.EqualTo(sessionId));
        var turn = session.GetProperty("turns")[0];
        Assert.That(turn.GetProperty("answerText").GetString(), Is.EqualTo(Words));
        Assert.That(turn.GetProperty("judgment").GetProperty("overallScore").GetDouble(), Is.EqualTo(0.8));
        Assert.That(root.GetProperty("reports").GetArrayLength(), Is.EqualTo(1));
        Assert.That(root.GetProperty("grades")[0].GetProperty("grade").GetString(), Is.EqualTo("B"));
        Assert.That(root.GetProperty("integrityEvents").GetArrayLength(), Is.EqualTo(1));
        Assert.That(json, Does.Not.Contain("someone else's words"));
        Assert.That(json, Does.Not.Contain(other.SessionId.ToString()));
    }

    [Test]
    public async Task Export_IncludesTheResponseLedgerAndTheGradingRecord()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        await using (var db = OrgDb(w.Org))
        {
            db.StudentResponses.Add(new StudentResponse
            {
                OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, OriginalText = Words, VerifiedText = Words,
                SubmittedAt = DateTimeOffset.UtcNow,
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = Gadfly.Assessment.GradeStatus.Published, FinalPercent = 81.5m, UpdatedAt = DateTimeOffset.UtcNow });
            db.GradeAppeals.Add(new GradeAppeal { OrganizationId = w.Org, SessionId = sessionId, StudentUserId = w.StudentId, Reason = "I think question two was misread", State = AppealState.Open, OpenedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var json = await WithPrivacy(w.Org, p => p.ExportStudentAsync(w.Org, w.StudentId));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("responses")[0].GetProperty("originalText").GetString(), Is.EqualTo(Words));
        Assert.That(root.GetProperty("finalGrades")[0].GetProperty("finalPercent").GetDecimal(), Is.EqualTo(81.5m));
        Assert.That(root.GetProperty("appeals")[0].GetProperty("reason").GetString(), Does.Contain("misread"));
    }

    [Test]
    public async Task Export_ForAnotherOrganization_IsEmpty()
    {
        var (w, _, _) = await Seed.EndedSessionAsync(Words);
        var elsewhere = await Seed.OrganizationAsync();
        var json = await WithPrivacy(elsewhere, p => p.ExportStudentAsync(elsewhere, w.StudentId));
        Assert.That(json, Does.Not.Contain(Words));
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("sessions").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task Erasure_DeletesTheSessionRecords_KeepsUsageCosts_DeactivatesMembership()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        await using (var db = OrgDb(w.Org))
        {
            db.UsageEntries.Add(new UsageEntry { OrganizationId = w.Org, SessionId = sessionId, Provider = "alpha", CostUsd = 0.5m, CreatedAt = DateTimeOffset.UtcNow });
            db.OrganizationMembers.Add(new MindAttic.Authentication.Entities.AuthMembership { OrganizationId = w.Org, UserId = w.StudentId, Role = OrgRoles.Student, CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var n = await WithPrivacy(w.Org, p => p.DeleteStudentDataAsync(w.Org, w.StudentId));
        Assert.That(n, Is.EqualTo(1));
        await using var check = SystemDb();
        Assert.That(await check.ExamSessions.AnyAsync(s => s.Id == sessionId), Is.False);
        Assert.That(await check.SessionTurns.AnyAsync(t => t.Id == turnId), Is.False);
        Assert.That(await check.Judgments.AnyAsync(j => j.TurnId == turnId), Is.False);
        Assert.That(await check.SessionReports.AnyAsync(r => r.SessionId == sessionId), Is.False);
        Assert.That(await check.GradeDecisions.AnyAsync(g => g.SessionId == sessionId), Is.False);
        Assert.That(await check.IntegrityEvents.AnyAsync(e => e.SessionId == sessionId), Is.False);
        Assert.That(await check.ExamAssignments.AnyAsync(a => a.Id == w.AssignmentId), Is.False);
        var usage = await check.UsageEntries.SingleAsync(u => u.OrganizationId == w.Org);
        Assert.That(usage.SessionId, Is.Null);
        Assert.That(usage.CostUsd, Is.EqualTo(0.5m), "costs stay for billing, unlinked from the person");
        Assert.That((await check.OrganizationMembers.SingleAsync(m => m.OrganizationId == w.Org && m.UserId == w.StudentId)).IsActive, Is.False);
    }

    [Test]
    public async Task Erasure_SucceedsWhenTheSessionHasAssessmentRecords()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync(Words);
        await using (var db = OrgDb(w.Org))
        {
            db.StudentResponses.Add(new StudentResponse
            {
                OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, OriginalText = Words, VerifiedText = Words,
                SubmittedAt = DateTimeOffset.UtcNow,
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = Gadfly.Assessment.GradeStatus.HumanReviewPending, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.DoesNotThrowAsync(() => WithPrivacy(w.Org, p => p.DeleteStudentDataAsync(w.Org, w.StudentId)),
            "erasure must also remove responses, transcript versions, evidence and grades (no cascades)");
        await using var check = SystemDb();
        Assert.That(await check.StudentResponses.AnyAsync(r => r.SessionId == sessionId), Is.False);
    }

    [Test]
    public async Task Erasure_RemovesTheAppealTextFromTheAuditTrail()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync(Words);
        await using (var db = OrgDb(w.Org))
        {
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = w.Org, SessionId = sessionId, ActorUserId = w.StudentId, ActorRole = "Student", Action = "grade.appeal.opened",
                EntityType = "GradeAppeal", EntityId = Guid.NewGuid().ToString(), Reason = Words, At = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await WithPrivacy(w.Org, p => p.DeleteStudentDataAsync(w.Org, w.StudentId));
        await using var check = SystemDb();
        var events = await check.AuditEvents.AsNoTracking().Where(a => a.SessionId == sessionId && a.Action == "grade.appeal.opened").ToListAsync();
        Assert.That(events, Has.Count.EqualTo(1), "the audit row itself stays");
        Assert.That(events[0].Reason, Does.Not.Contain("chlorophyll"));
    }

    [Test]
    public async Task Erasure_LeavesOtherStudentsAndOrganizationsAlone()
    {
        var target = await Seed.EndedSessionAsync(Words);
        var bystander = await Seed.EndedSessionAsync("bystander");
        await WithPrivacy(target.World.Org, p => p.DeleteStudentDataAsync(target.World.Org, target.World.StudentId));
        await using var check = SystemDb();
        Assert.That(await check.ExamSessions.AnyAsync(s => s.Id == bystander.SessionId), Is.True);
    }
}
