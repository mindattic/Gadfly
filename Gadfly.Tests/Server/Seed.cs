using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Gadfly.Tests.Server;

/// <summary>Test data on the shared test database. Every call makes fresh organizations, so tests never collide.</summary>
internal static class Seed
{
    public static readonly string StrongPassword = "Correct-horse-battery-staple-" + Guid.NewGuid().ToString("N")[..8];

    public static async Task<Guid> OrganizationAsync(string? name = null, decimal? quota = null)
    {
        await using var db = ServerFixture.NewContext(TenantSnapshot.System);
        var org = new MindAttic.Authentication.Entities.AuthOrganization
        {
            Name = name ?? "Org " + Guid.NewGuid().ToString("N")[..8],
            Slug = "org-" + Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
        };
        db.Organizations.Add(org);
        db.OrganizationSettings.Add(new OrganizationSettings { OrganizationId = org.Id, MonthlyCostQuotaUsd = quota, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return org.Id;
    }

    /// <summary>A real account (MindAttic.Authentication) and membership, through the app's directory.</summary>
    public static async Task<Guid> MemberAsync(IServiceProvider services, Guid org, string role, string? userName = null)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeFor(TenantSnapshot.System);
        var directory = scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>();
        var r = await directory.AddMemberAsync(org, userName ?? UserName(), null, role, StrongPassword);
        if (!r.Ok || r.Id is null) throw new InvalidOperationException("Could not seed a member: " + r.Message);
        return r.Id.Value;
    }

    public static string UserName() => "u" + Guid.NewGuid().ToString("N")[..12];

    public static readonly SourcePassage[] Passages =
    [
        new() { Id = "p0", DocumentId = "doc", Ordinal = 0, Text = "Photosynthesis converts light energy into chemical energy stored in glucose, releasing oxygen." },
        new() { Id = "p1", DocumentId = "doc", Ordinal = 1, Text = "Chlorophyll in chloroplasts absorbs light for the light reactions." },
        new() { Id = "p2", DocumentId = "doc", Ordinal = 2, Text = "Cellular respiration breaks glucose down with oxygen to release ATP." },
    ];

    public sealed record ExamWorld(Guid Org, Guid CollectionId, Guid ExamId, Guid AssignmentId, Guid StudentId);

    /// <summary>A processed collection (passages, concept map, rubrics), a published exam on it and one assignment.</summary>
    public static async Task<ExamWorld> ExamAsync(Guid? organization = null, SessionPolicy? policy = null, int attempts = 1,
        DateTimeOffset? opens = null, DateTimeOffset? closes = null, ExamStatus status = ExamStatus.Published, decimal? quota = null,
        IntegrityLevel integrity = IntegrityLevel.Standard)
    {
        var org = organization ?? await OrganizationAsync(quota: quota);
        var collectionId = Guid.NewGuid();
        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(org)))
        {
            var store = new SqlSourceStore(db, org, Guid.Empty);
            await store.EnsureCollectionAsync(collectionId.ToString(), "Biology");
            await store.SaveDocumentAsync(collectionId.ToString(), new SourceDocumentInfo
            {
                Id = "doc", FileName = "biology.md", Sha256 = new string('a', 64), PassageCount = Passages.Length, IngestedAt = DateTimeOffset.UtcNow,
            }, Passages);
        }
        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(org)))
        {
            var maps = new SqlConceptMapStore(db, org);
            await maps.SaveMapAsync(new ConceptMap
            {
                CollectionId = collectionId.ToString(),
                Version = 1,
                Concepts =
                [
                    new ConceptEntry { Key = "photosynthesis", Title = "Photosynthesis", Definition = "Plants turn light into chemical energy." },
                    new ConceptEntry { Key = "respiration", Title = "Cellular respiration", Definition = "Cells release energy from glucose." },
                ],
            });
        }
        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(org)))
        {
            await new SqlConceptMapStore(db, org).SaveRubricsAsync(collectionId.ToString(),
            [
                new ConceptRubric { ConceptKey = "photosynthesis", Understanding = [new RubricItem("Light energy becomes chemical energy", 2, [])] },
                new ConceptRubric { ConceptKey = "respiration", Understanding = [new RubricItem("Glucose is broken down to release ATP", 2, [])] },
            ]);
        }
        var p = policy ?? new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 6, TimeBudget = TimeSpan.FromMinutes(20) };
        var studentId = Guid.NewGuid();
        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(org)))
        {
            var exam = new Exam
            {
                OrganizationId = org, CollectionId = collectionId, Title = "Biology check", Status = status,
                PolicyJson = JsonSerializer.Serialize(p, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                PromptSetVersion = ExamPrompts.Version, CreatedAt = DateTimeOffset.UtcNow, IntegrityLevel = integrity,
            };
            db.Exams.Add(exam);
            var assignment = new ExamAssignment
            {
                OrganizationId = org, ExamId = exam.Id, StudentUserId = studentId, AttemptsAllowed = attempts, OpensAt = opens, ClosesAt = closes,
                AssignedAt = DateTimeOffset.UtcNow,
            };
            db.ExamAssignments.Add(assignment);
            await db.SaveChangesAsync();
            return new ExamWorld(org, collectionId, exam.Id, assignment.Id, studentId);
        }
    }

    /// <summary>An ended session with one judged turn, a claim, a report and a grade decision: what privacy code works on.</summary>
    public static async Task<(ExamWorld World, Guid SessionId, Guid TurnId)> EndedSessionAsync(string answer = "Plants use light to make glucose.",
        DateTimeOffset? endedAt = null)
    {
        var w = await ExamAsync();
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(w.Org));
        var ended = endedAt ?? DateTimeOffset.UtcNow;
        var session = new ExamSession
        {
            OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, State = SessionState.Ended,
            CreatedAt = ended.AddMinutes(-20), StartedAt = ended.AddMinutes(-20), EndedAt = ended, EndReason = "Mastery",
            TranscriptJson = "{\"secret\": \"" + answer + "\"}", LedgerJson = "{}", ClientInfo = "browser",
        };
        db.ExamSessions.Add(session);
        var turn = new SessionTurn
        {
            OrganizationId = w.Org, SessionId = session.Id, Index = 0, ConceptKey = "photosynthesis", Move = "Elicit",
            Question = "What is photosynthesis?", AskedAt = ended.AddMinutes(-19), AnswerText = answer, AnswerMode = "text",
            WordsJson = "[]", SpeechMetricsJson = "{}", AudioRef = "audio-1", MasteryAfter = 0.7,
        };
        db.SessionTurns.Add(turn);
        db.Judgments.Add(new Judgment
        {
            OrganizationId = w.Org, TurnId = turn.Id, OverallScore = 0.8, Agreement = 0.9, Summary = "Says: " + answer,
            CriteriaJson = "[{\"criterion\":\"accuracy\",\"score\":0.8,\"rationale\":\"Quotes: " + answer + "\",\"citations\":[{\"chunkId\":\"p0\",\"quote\":\"q\"}]}]",
            CreatedAt = ended,
            Votes = [new JudgeVoteRecord { OrganizationId = w.Org, ProviderId = "alpha", Model = "m", OverallScore = 0.8, Verdict = "Verdict on " + answer }],
        });
        db.SessionClaims.Add(new SessionClaim { OrganizationId = w.Org, SessionId = session.Id, TurnId = turn.Id, ConceptKey = "photosynthesis", Quote = answer, Normalized = "c1|x" });
        db.ConceptMastery.Add(new ConceptMasteryRecord { OrganizationId = w.Org, SessionId = session.Id, ConceptKey = "photosynthesis", Estimate = 0.7, Status = "Open", UpdatedAt = ended });
        var s = new SocraticSession("subject", new SessionPolicy(), ExamPrompts.Set, ended.AddMinutes(-20));
        s.Ask("photosynthesis", MoveKind.Elicit, "What is photosynthesis?", ended.AddMinutes(-19));
        s.RecordAnswer(new Answer { Text = answer, CompletedAt = ended.AddMinutes(-18) });
        s.RecordJudgment(new AnswerJudgment { Criteria = [], OverallScore = 0.8, Summary = "fine" }, 0.7);
        var report = Gadfly.Engine.SessionReport.From(s);
        db.SessionReports.Add(new Gadfly.Server.Data.SessionReport
        {
            OrganizationId = w.Org, SessionId = session.Id, Recommendation = report.Recommendation, OverallScore = report.OverallScore,
            ReportJson = report.ToJson(), Markdown = report.ToMarkdown(), CreatedAt = ended,
        });
        db.GradeDecisions.Add(new GradeDecision { OrganizationId = w.Org, SessionId = session.Id, Grade = "B", Score = 0.8, DecidedAt = ended });
        db.IntegrityEvents.Add(new IntegrityEvent
        {
            OrganizationId = w.Org, SessionId = session.Id, Source = "text", Type = "paste", StartedAt = ended, Summary = "pasted", DataJson = "{\"text\":\"" + answer + "\"}",
        });
        await db.SaveChangesAsync();
        return (w, session.Id, turn.Id);
    }
}
