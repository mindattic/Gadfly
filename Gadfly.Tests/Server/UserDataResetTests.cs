using Gadfly.Server.Data;
using Gadfly.Server.Notifications;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Entities;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The platform-wide user data reset (GAD-T789): its table set comes from the EF model, and on a database of its own (the reset
/// is platform-wide, so never the shared test database) it erases every attempt and what hangs off it while accounts, exams,
/// assignments and the audit log stay.
/// </summary>
[TestFixture]
public class UserDataResetTests : ServerTest
{
    private static GadflyDbContext ModelOnly()
    {
        var options = new DbContextOptionsBuilder<GadflyDbContext>();
        GadflyDatabase.Configure(options, GadflyDatabase.LocalDbConnectionString);
        return new GadflyDbContext(options.Options);
    }

    [Test]
    public void Session_tables_come_from_the_model_children_first()
    {
        using var db = ModelOnly();
        var tables = UserDataReset.SessionTables(db.Model);
        var types = tables.Select(t => t.Entity.ClrType).ToList();

        Assert.That(types, Is.SupersetOf(new[]
        {
            typeof(ExamSession), typeof(SessionTurn), typeof(SessionClaim), typeof(Judgment), typeof(JudgeVoteRecord), typeof(ConceptMasteryRecord),
            typeof(IntegrityEvent), typeof(Gadfly.Server.Data.SessionReport), typeof(GradeDecision), typeof(StudentResponse), typeof(TranscriptVersion),
            typeof(ResponseVerification), typeof(ClarificationEvent), typeof(AssessmentEvidence), typeof(AiObjectiveGrade), typeof(HumanObjectiveGrade),
            typeof(GradeDisagreement), typeof(GradeReconciliation), typeof(FinalGrade), typeof(ApprovalRecord), typeof(GradeAppeal),
            typeof(InboxMessage), typeof(UsageEntry), typeof(ApiCallTiming),
        }));
        Assert.That(types, Has.None.AnyOf(typeof(AuditEvent), typeof(Exam), typeof(ExamAssignment), typeof(Course), typeof(LearningObjective),
            typeof(RubricCriterion), typeof(AuthUser), typeof(AuthOrganization), typeof(AuthMembership), typeof(AuthSession), typeof(ApiKey)));

        // Every table comes after each table that points at it, so nothing is left referencing a deleted row.
        for (var i = 0; i < tables.Count; i++)
            foreach (var fk in tables[i].Entity.GetForeignKeys().Where(fk => fk.PrincipalEntityType != tables[i].Entity))
            {
                var parent = types.IndexOf(fk.PrincipalEntityType.ClrType);
                if (parent >= 0) Assert.That(parent, Is.GreaterThan(i), $"{tables[i].Name} must go before {tables[parent].Name}");
            }

        Assert.That(tables.Single(t => t.Entity.ClrType == typeof(ExamSession)).Filter, Is.Null, "every attempt goes");
        Assert.That(tables.Single(t => t.Entity.ClrType == typeof(UsageEntry)).Filter, Is.EqualTo("[SessionId] IS NOT NULL"),
            "usage that no session caused (source processing, translation) stays");
    }

    [Test]
    public async Task Reset_erases_exam_activity_and_keeps_accounts_exams_and_the_audit_log()
    {
        var connection = ServerFixture.ConnectionString.Replace("Database=GadflyTests_", "Database=GadflyReset_");
        var options = new DbContextOptionsBuilder<GadflyDbContext>();
        GadflyDatabase.Configure(options, connection);
        await using var db = new GadflyDbContext(options.Options);
        await db.Database.MigrateAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var org = new AuthOrganization { Name = "Reset org", Slug = "reset-" + Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow };
            var user = new AuthUser { UserName = "erin", NormalizedUserName = "ERIN", PasswordHash = "argon2-hash", PasswordUpdatedUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow };
            db.AddRange(org, user);
            await db.SaveChangesAsync();
            var collection = new SourceCollection { OrganizationId = org.Id, Title = "Material", CreatedByUserId = user.Id, CreatedAt = now };
            var exam = new Exam { OrganizationId = org.Id, CollectionId = collection.Id, Title = "Catalog exam", Status = ExamStatus.Published, CreatedAt = now };
            var assignment = new ExamAssignment { OrganizationId = org.Id, ExamId = exam.Id, StudentUserId = user.Id, AssignedAt = now, ResultsReleasedAt = now };
            var session = new ExamSession
            {
                OrganizationId = org.Id, AssignmentId = assignment.Id, ExamId = exam.Id, StudentUserId = user.Id, State = SessionState.Ended,
                CreatedAt = now, EndedAt = now, TranscriptJson = "{}",
            };
            var turn = new SessionTurn { OrganizationId = org.Id, SessionId = session.Id, Index = 0, ConceptKey = "c", Move = "Elicit", Question = "Why?", AskedAt = now, AnswerText = "Because." };
            var response = new StudentResponse { OrganizationId = org.Id, SessionId = session.Id, TurnId = turn.Id, OriginalText = "Because.", VerifiedText = "Because.", SubmittedAt = now };
            db.AddRange(collection, exam, assignment, session, turn, response);
            db.Add(new Judgment
            {
                OrganizationId = org.Id, TurnId = turn.Id, Summary = "ok", CriteriaJson = "[]", CreatedAt = now,
                Votes = [new JudgeVoteRecord { OrganizationId = org.Id, ProviderId = "a", Model = "m", Verdict = "ok" }],
            });
            db.Add(new SessionClaim { OrganizationId = org.Id, SessionId = session.Id, TurnId = turn.Id, ConceptKey = "c", Quote = "Because.", Normalized = "n" });
            db.Add(new TranscriptVersion { OrganizationId = org.Id, ResponseId = response.Id, Version = 1, Text = "Because.", CreatedAt = now });
            db.Add(new GradeDecision { OrganizationId = org.Id, SessionId = session.Id, Grade = "B", DecidedAt = now });
            db.Add(new FinalGrade { OrganizationId = org.Id, SessionId = session.Id, UpdatedAt = now });
            db.Add(new InboxMessage { OrganizationId = org.Id, RecipientUserId = user.Id, Kind = "grade.final", Title = "Result", SessionId = session.Id, CreatedAt = now });
            db.Add(new UsageEntry { OrganizationId = org.Id, SessionId = session.Id, Provider = "p", CreatedAt = now });
            db.Add(new UsageEntry { OrganizationId = org.Id, Provider = "translation", CreatedAt = now });
            db.Add(new Job { OrganizationId = org.Id, Kind = "grading.ai", CreatedAt = now, RunAfter = now });
            db.Add(new Job { OrganizationId = org.Id, Kind = SourceProcessingJob.JobKind, CreatedAt = now, RunAfter = now });
            db.Add(new EmailLog { OrganizationId = org.Id, Kind = EmailKinds.ResultReleased, ToUserId = user.Id, Status = EmailLogStatus.Sent, CreatedAt = now });
            db.Add(new EmailLog { OrganizationId = org.Id, Kind = EmailKinds.Reminder, ToUserId = user.Id, Status = EmailLogStatus.Queued, CreatedAt = now });
            db.Add(new EmailLog { OrganizationId = org.Id, Kind = EmailKinds.Reminder, ToUserId = user.Id, RelatedId = assignment.Id, Status = EmailLogStatus.Sent, CreatedAt = now });
            db.Add(new AuditEvent { OrganizationId = org.Id, Action = "grade.published", EntityType = "Session", SessionId = session.Id, At = now });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dry = await UserDataReset.CountAsync(db, CancellationToken.None);
            int Rows(IEnumerable<UserDataReset.Line> lines, string table, string action = "delete") =>
                lines.Single(l => l.Table == "gadfly." + table && l.Action == action).Rows;
            Assert.Multiple(() =>
            {
                Assert.That(Rows(dry, "ExamSessions"), Is.EqualTo(1));
                Assert.That(Rows(dry, "JudgeVotes"), Is.EqualTo(1));
                Assert.That(Rows(dry, "UsageEntries"), Is.EqualTo(1));
                Assert.That(Rows(dry, "Jobs"), Is.EqualTo(1));
                Assert.That(Rows(dry, "EmailLogs"), Is.EqualTo(2));
                Assert.That(Rows(dry, "ExamAssignments", "update"), Is.EqualTo(1));
            });
            Assert.That(await db.ExamSessions.CountAsync(), Is.EqualTo(1), "a dry run changes nothing");

            var done = await UserDataReset.ResetAsync(db, TimeProvider.System, CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.That(Rows(done, "SessionTurns"), Is.EqualTo(1));
            foreach (var t in UserDataReset.SessionTables(db.Model).Where(t => t.Filter is null))
                Assert.That(await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {t.From}").SingleAsync(), Is.Zero, t.Name);
            Assert.That(await db.UsageEntries.Select(u => u.Provider).ToListAsync(), Is.EqualTo(new[] { "translation" }));
            Assert.That(await db.Jobs.Select(j => j.Kind).ToListAsync(), Is.EqualTo(new[] { SourceProcessingJob.JobKind }));
            Assert.That(await db.EmailLogs.Select(e => e.RelatedId).ToListAsync(), Is.EqualTo(new Guid?[] { assignment.Id }), "the sent reminder stays");
            Assert.That((await db.AuthUsers.SingleAsync(u => u.Id == user.Id)).PasswordHash, Is.EqualTo("argon2-hash"));
            Assert.That(await db.Exams.CountAsync(e => e.Id == exam.Id), Is.EqualTo(1));
            Assert.That((await db.ExamAssignments.SingleAsync(a => a.Id == assignment.Id)).ResultsReleasedAt, Is.Null);
            Assert.That(await db.AuditEvents.CountAsync(a => a.Action == "grade.published"), Is.EqualTo(1), "the audit log is kept");
            var reset = await db.AuditEvents.SingleAsync(a => a.Action == UserDataReset.ResetAction);
            Assert.That(reset.AfterJson, Does.Contain("\"gadfly.ExamSessions\":1"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
