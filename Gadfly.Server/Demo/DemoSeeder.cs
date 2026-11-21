using System.Security.Cryptography;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Demo;

/// <summary>
/// Seeds the demonstration institution (GAD-T103) for pilots and for the demo tour (GAD-T105): "Gadfly Demo Academy"
/// with an owner, a teacher, an examiner, a second reviewer and a student; two courses; and the master plan's two
/// demonstration assessments, the multiplication examination of Part 2.2 and the statistics objective "understand
/// interaction effects in regression" of Part 7.2, each with source notes (Demo/Material/*.md) processed by the
/// real source job, learning objectives, rubric criteria with level descriptors 0-5, and a locked grading policy
/// (AI 40% / human 60%, additional review at 10 points, mandatory reconciliation at 20).
/// <para>
/// Development only. Run with <c>Gadfly.Server --seed-demo</c>: the accounts, courses and source collections are
/// created before the server starts; the assessments are created as soon as the source job has built the concept
/// maps (the job runs on the server's own worker). Idempotent: an existing demo is completed, never duplicated, and
/// accounts that already exist keep their password (only accounts a run creates get the demo password). Passwords come from <c>Gadfly:Demo:Password</c> or are
/// generated and printed once; <c>Gadfly:Demo:ApiKey</c> (a <c>gfk_...</c> string) pins the demo API key.
/// </para>
/// </summary>
public static class DemoSeeder
{
    public const string SeedArgument = "--seed-demo";
    public const string OrganizationName = "Gadfly Demo Academy";
    public const string PasswordSetting = "Gadfly:Demo:Password";
    public const string ApiKeySetting = "Gadfly:Demo:ApiKey";
    public const string SeededAction = "demo.seeded";

    public static readonly IReadOnlyList<(string UserName, string Role)> Accounts =
    [
        ("demo.owner", OrgRoles.Owner),
        ("demo.teacher", OrgRoles.Instructor),
        ("demo.examiner", OrgRoles.Examiner),
        ("demo.reviewer", OrgRoles.SecondReviewer),
        ("demo.student", OrgRoles.Student),
        ("ryan", OrgRoles.Student),
    ];

    public static readonly IReadOnlyList<DemoAssessment> Assessments =
    [
        new DemoAssessment(
            Title: "Demo: Understanding multiplication",
            Course: "Demo Year 5 Mathematics",
            Subject: "Mathematics",
            Topic: "Multiplication",
            Collection: "Demo notes: Multiplication",
            MaterialFile: "multiplication.md",
            Instructions: "Explain your thinking in your own words. There are no trick questions; say \"I don't know\" if you don't.",
            Objectives:
            [
                new DemoObjective("equal-groups", "Explain multiplication as equal groups and repeated addition",
                    "Relates multiplication to repeated addition of equal groups and knows when it applies.", "Multiplication as equal groups", 10m),
                new DemoObjective("factor-roles", "Interpret what each factor represents",
                    "Says what the number of groups and the size of each group stand for in a situation.", "What each factor represents", 10m),
                new DemoObjective("scaling", "Reason about how changing a factor changes the product",
                    "Predicts and explains how the total changes when one factor doubles or halves, moving towards scaling.", "How changing a factor changes the product", 10m),
            ]),
        new DemoAssessment(
            Title: "Demo: Interaction effects in regression",
            Course: "Demo Introductory Statistics",
            Subject: "Statistics",
            Topic: "Regression",
            Collection: "Demo notes: Interaction effects",
            MaterialFile: "interaction-effects.md",
            Instructions: "Answer as you would explain it to a classmate. Use your own examples where you can.",
            Objectives:
            [
                new DemoObjective("interaction-effects", "Understand interaction effects in regression",
                    "Explains that the relationship between an independent variable and the outcome can depend on another variable, and interprets interaction coefficients.",
                    "Interaction effects", 20m),
            ]),
        new DemoAssessment(
            Title: "Demo: The American Civil War",
            Course: "Demo U.S. History",
            Subject: "History",
            Topic: "The American Civil War",
            Collection: "Demo notes: The American Civil War",
            MaterialFile: "civil-war.md",
            Instructions: "The examiner will talk with you about the Civil War. Answer in your own words and explain why, not only what. Say \"I don't know\" if you don't.",
            Objectives:
            [
                new DemoObjective("causes", "Explain why the Southern states seceded",
                    "Connects the dispute over slavery's expansion, the crises of the 1850s and Lincoln's election to secession.", "Why the Union broke apart", 10m),
                new DemoObjective("turning-points", "Explain how and why the war turned in the Union's favor",
                    "Relates the two sides' strengths and strategies to Antietam, Gettysburg, Vicksburg and the campaigns of 1864.", "The course of the war", 10m),
                new DemoObjective("emancipation", "Explain how ending slavery became a war aim and what the Emancipation Proclamation did and did not do",
                    "Distinguishes the Proclamation's limits from its significance, and explains the Thirteenth Amendment's role.", "Emancipation", 10m),
            ],
            Mode: ExamMode.Voice),
    ];

    /// <summary>The <c>Program.cs</c> hook: seeds when the process was started with <see cref="SeedArgument"/>.</summary>
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Demo");
        if (!app.Environment.IsDevelopment())
        {
            log.LogError("--seed-demo is for development only; nothing was seeded.");
            return;
        }
        var scopes = app.Services.GetRequiredService<IServiceScopeFactory>();
        var plan = await SeedAccountsAndSourcesAsync(scopes, app.Configuration, app.Environment.ContentRootPath, log, CancellationToken.None);
        // The assessments need the concept maps, which the server's job worker builds once the host is running.
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            try { await SeedAssessmentsAsync(scopes, plan, log, app.Lifetime.ApplicationStopping); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log.LogError(ex, "Demo seeding failed while creating the assessments."); }
        }));
    }

    /// <summary>What phase one created, for phase two.</summary>
    public sealed record SeedPlan(Guid OrganizationId, Guid TeacherId, Guid StudentId,
        IReadOnlyList<(DemoAssessment Assessment, Guid CourseId, Guid CollectionId, Guid? JobId)> Collections,
        IReadOnlyList<Guid>? OtherStudentIds = null);

    // ------------------------------------------------------------------ phase 1: institution, people, courses, sources

    public static async Task<SeedPlan> SeedAccountsAndSourcesAsync(IServiceScopeFactory scopes, IConfiguration config, string contentRoot,
        ILogger log, CancellationToken ct)
    {
        using var scope = scopes.CreateSystemScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<GadflyDbContext>();
        var directory = sp.GetRequiredService<IOrganizationDirectory>();
        var users = sp.GetRequiredService<IUserAdminService>();
        var clock = sp.GetRequiredService<TimeProvider>();

        var configuredPassword = config[PasswordSetting];
        var password = string.IsNullOrWhiteSpace(configuredPassword) ? GeneratePassword() : configuredPassword.Trim();

        // Accounts that exist before this run (a real account such as 'ryan', or one seeded earlier) keep their password and flags.
        var normalizedNames = Accounts.Select(a => MindAttic.Authentication.Services.IUserStore.Normalize(a.UserName)).ToList();
        var preexisting = (await db.AuthUsers.AsNoTracking().Where(u => normalizedNames.Contains(u.NormalizedUserName))
            .Select(u => u.NormalizedUserName).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Name == OrganizationName, ct);
        if (org is null)
        {
            var created = await directory.CreateOrganizationAsync(OrganizationName, Accounts[0].UserName, "demo.owner@demo.gadfly.invalid",
                password, Guid.Empty, ct);
            if (!created.Ok || created.Id is null) throw new InvalidOperationException("Demo organization not created: " + created.Message);
            org = await db.Organizations.FirstAsync(o => o.Id == created.Id, ct);
        }

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (userName, role) in Accounts)
        {
            var added = await directory.AddMemberAsync(org.Id, userName, $"{userName}@demo.gadfly.invalid", role, password, ct);
            if (!added.Ok || added.Id is null) throw new InvalidOperationException($"Demo account {userName} not created: {added.Message}");
            ids[userName] = added.Id.Value;
            if (preexisting.Contains(MindAttic.Authentication.Services.IUserStore.Normalize(userName))) continue;
            // A demo account this run created signs straight in with the demo password (no forced change); development only.
            await users.ResetPasswordAsync(added.Id.Value, password, false, ct);
            await db.AuthUsers.Where(u => u.Id == added.Id.Value).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.MustChangePassword, false)
                .SetProperty(u => u.MustEnrollMfa, false), ct);
        }

        var apiKey = await EnsureApiKeyAsync(db, sp.GetRequiredService<ApiKeyService>(), org.Id, ids["demo.owner"], config[ApiKeySetting], clock, ct);

        var collections = new List<(DemoAssessment, Guid, Guid, Guid?)>();
        var jobs = sp.GetRequiredService<JobQueue>();
        foreach (var a in Assessments)
        {
            var course = await db.Courses.FirstOrDefaultAsync(c => c.OrganizationId == org.Id && c.Title == a.Course, ct);
            if (course is null)
            {
                course = new Course
                {
                    OrganizationId = org.Id, Title = a.Course, Subject = a.Subject, CreatedByUserId = ids["demo.teacher"], CreatedAt = clock.GetUtcNow(),
                    Description = "Demonstration course (docs/MASTER-PLAN.md). Seeded by --seed-demo.",
                };
                db.Courses.Add(course);
                await db.SaveChangesAsync(ct);
            }

            var collection = await db.SourceCollections.FirstOrDefaultAsync(c => c.OrganizationId == org.Id && c.Title == a.Collection, ct);
            if (collection is null)
            {
                collection = new SourceCollection
                {
                    OrganizationId = org.Id, CourseId = course.Id, Title = a.Collection, CreatedByUserId = ids["demo.teacher"],
                    CreatedAt = clock.GetUtcNow(), Status = ProcessingStatus.Pending,
                    Description = "Source notes for a demonstration assessment.",
                };
                db.SourceCollections.Add(collection);
                await db.SaveChangesAsync(ct);
            }

            Guid? jobId = null;
            var hasMap = await new SqlConceptMapStore(db, org.Id).LoadMapAsync(collection.Id.ToString(), ct) is { Concepts.Count: > 0 };
            if (!hasMap)
            {
                var folder = SourceUploads.FolderFor(org.Id, collection.Id);
                Directory.CreateDirectory(folder);
                File.Copy(MaterialPath(contentRoot, a.MaterialFile), Path.Combine(folder, a.MaterialFile), overwrite: true);
                var active = await db.Jobs.Where(j => j.Kind == SourceProcessingJob.JobKind && j.OrganizationId == org.Id
                        && (j.State == JobState.Queued || j.State == JobState.Running) && j.PayloadJson.Contains(collection.Id.ToString()))
                    .Select(j => (Guid?)j.Id).FirstOrDefaultAsync(ct);
                jobId = active ?? await jobs.EnqueueAsync(SourceProcessingJob.JobKind,
                    new SourceProcessingRequest(org.Id, collection.Id, ids["demo.teacher"], collection.Title, folder), org.Id, ct: ct);
            }
            collections.Add((a, course.Id, collection.Id, jobId));
        }

        Print(log, password, configuredPassword is { Length: > 0 }, apiKey);
        var otherStudents = Accounts.Where(a => a.Role == OrgRoles.Student && a.UserName != "demo.student").Select(a => ids[a.UserName]).ToList();
        return new SeedPlan(org.Id, ids["demo.teacher"], ids["demo.student"], collections, otherStudents);
    }

    // ------------------------------------------------------------------ phase 2: assessments (after source processing)

    public static async Task SeedAssessmentsAsync(IServiceScopeFactory scopes, SeedPlan plan, ILogger log, CancellationToken ct)
    {
        foreach (var (assessment, courseId, collectionId, jobId) in plan.Collections)
        {
            if (jobId is { } id && !await WaitForJobAsync(scopes, id, log, ct)) continue;

            using var scope = scopes.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var existing = await db.Exams.FirstOrDefaultAsync(e => e.OrganizationId == plan.OrganizationId && e.Title == assessment.Title, ct);
            if (existing is not null)
            {
                // Already created on an earlier run: add the assignments of demo students it doesn't have yet (as ClassroomSeeder does).
                var have = await db.ExamAssignments.Where(x => x.ExamId == existing.Id).Select(x => x.StudentUserId).ToListAsync(ct);
                foreach (var sid in new[] { plan.StudentId }.Concat(plan.OtherStudentIds ?? []).Distinct().Except(have))
                    db.ExamAssignments.Add(new ExamAssignment
                    {
                        OrganizationId = plan.OrganizationId, ExamId = existing.Id, StudentUserId = sid, AttemptsAllowed = 50,
                        AssignedByUserId = plan.TeacherId, AssignedAt = clock.GetUtcNow(),
                    });
                await db.SaveChangesAsync(ct);
                continue;
            }
            var map = await new SqlConceptMapStore(db, plan.OrganizationId).LoadMapAsync(collectionId.ToString(), ct);
            if (map is null || map.Concepts.Count == 0)
            {
                log.LogError("Demo assessment {Title} not created: the collection has no concept map.", assessment.Title);
                continue;
            }
            // Every demo student gets every demo assessment (demo.student is the one the tour drives).
            await CreateAssessmentAsync(db, clock, plan.OrganizationId, plan.TeacherId, [plan.StudentId, .. plan.OtherStudentIds ?? []], assessment, courseId, collectionId, map,
                "Demonstration assessment seeded by --seed-demo (GAD-T103).", ct);
            log.LogWarning("Demo assessment ready: {Title}.", assessment.Title);
        }
    }

    private static async Task<bool> WaitForJobAsync(IServiceScopeFactory scopes, Guid jobId, ILogger log, CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(20);
        while (DateTimeOffset.UtcNow < until)
        {
            using (var scope = scopes.CreateSystemScope())
            {
                var job = await scope.ServiceProvider.GetRequiredService<GadflyDbContext>().Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
                if (job is null) return false;
                if (job.State == JobState.Succeeded) return true;
                if (job.State is JobState.Failed or JobState.Cancelled)
                {
                    log.LogError("Demo source processing job {Job} ended {State}: {Error}", jobId, job.State, job.Error);
                    return false;
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        log.LogError("Demo source processing job {Job} did not finish in 20 minutes.", jobId);
        return false;
    }

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Creates a published, policy-locked assessment from a seeded collection and assigns it (also used by ClassroomSeeder).</summary>
    internal static async Task<Guid> CreateAssessmentAsync(GadflyDbContext db, TimeProvider clock, Guid org, Guid teacherId,
        IReadOnlyList<Guid> studentIds, DemoAssessment a, Guid courseId, Guid collectionId, ConceptMap map, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Each objective is grounded in the concept whose title matches it best (exact keys in mock mode, titles in live mode).
        var concepts = a.Objectives.Select(o => BestConcept(map, o.ConceptTitle)).ToList();
        var policy = new SessionPolicy
        {
            ConceptIds = concepts.Select(c => c.Key).Distinct().ToList(),
            TimeBudget = TimeSpan.FromMinutes(30),
            MaxTurns = 16,
            MaxTurnsPerConcept = 4,
            Difficulty = 2,
            Seed = 20261120,
        };
        policy.Validate();

        var exam = new Exam
        {
            OrganizationId = org, CollectionId = collectionId, CourseId = courseId, Subject = a.Subject, Topic = a.Topic,
            AssessmentMode = AssessmentMode.Examination, Title = a.Title, Instructions = a.Instructions,
            PolicyJson = JsonSerializer.Serialize(policy, Camel), Mode = a.Mode,
            PromptSetId = ExamPrompts.SetId, PromptSetVersion = ExamPrompts.Version,
            Status = ExamStatus.Published, CreatedByUserId = teacherId, CreatedAt = now, PublishedAt = now,
        };
        db.Exams.Add(exam);

        for (var i = 0; i < a.Objectives.Count; i++)
        {
            var o = a.Objectives[i];
            var objective = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = o.Key, Title = o.Title, Description = o.Description,
                ConceptKey = concepts[i].Key, Weight = 1m, MaxScore = o.MaxScore, Ordinal = i,
            };
            var criteria = new (AssessmentDimension Dimension, string Name, string Description, decimal Weight)[]
            {
                (AssessmentDimension.ConceptualUnderstanding, "Conceptual understanding", "Explains the idea accurately in their own words.", 2m),
                (AssessmentDimension.LogicalReasoning, "Reasoning", "Explains why, not only what, and keeps claims consistent.", 1m),
                (AssessmentDimension.Application, "Application", "Uses the idea correctly in a new situation or example.", 1m),
            };
            for (var c = 0; c < criteria.Length; c++)
            {
                var criterion = new RubricCriterion
                {
                    OrganizationId = org, ObjectiveId = objective.Id, Dimension = criteria[c].Dimension, Name = criteria[c].Name,
                    Description = criteria[c].Description, Weight = criteria[c].Weight, Ordinal = c,
                };
                criterion.Levels.AddRange(Enum.GetValues<MasteryLevel>().Select(level => new RubricLevelDescriptor
                {
                    OrganizationId = org, CriterionId = criterion.Id, Level = level, Descriptor = MasteryLevels.DefaultDescriptor(level),
                }));
                objective.Criteria.Add(criterion);
            }
            db.LearningObjectives.Add(objective);
        }

        db.GradingPolicies.Add(new GradingPolicy
        {
            ExamId = exam.Id, OrganizationId = org, AiWeight = 0.4m, HumanWeight = 0.6m, AdditionalReviewAt = 10m,
            MandatoryReconciliationAt = 20m, BlindHumanGrading = true, InconclusiveProcedure = InconclusiveProcedure.HumanReview,
            AllowAssistedEvidence = false, LockedAt = now, LockedByUserId = teacherId,
        });
        foreach (var studentId in studentIds.Distinct())
            db.ExamAssignments.Add(new ExamAssignment
            {
                OrganizationId = org, ExamId = exam.Id, StudentUserId = studentId, AttemptsAllowed = 50,
                AssignedByUserId = teacherId, AssignedAt = now,
            });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org, ActorUserId = teacherId, ActorRole = OrgRoles.Instructor, Action = SeededAction,
            EntityType = nameof(Exam), EntityId = exam.Id.ToString(), At = now,
            AfterJson = JsonSerializer.Serialize(new { exam.Title, objectives = a.Objectives.Select(o => o.Key), policy = "40/60, 10/20" }),
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return exam.Id;
    }

    internal static ConceptEntry BestConcept(ConceptMap map, string title)
    {
        static HashSet<string> Words(string s) => s.ToLowerInvariant().Split([' ', '-', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).ToHashSet();
        var want = Words(title);
        return map.Concepts.FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase))
            ?? map.Concepts.OrderByDescending(c => Words(c.Title + " " + c.Definition).Count(want.Contains)).First();
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<string?> EnsureApiKeyAsync(GadflyDbContext db, ApiKeyService keys, Guid org, Guid owner, string? configured,
        TimeProvider clock, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var key = configured.Trim();
            var parts = key.Split('_');
            if (!key.StartsWith(ApiKeyService.KeyPrefix, StringComparison.Ordinal) || parts.Length < 3 || parts[1].Length != 8)
                throw new InvalidOperationException($"{ApiKeySetting} must look like gfk_<8 hex>_<secret>.");
            var hash = ApiKeyService.Hash(key);
            if (!await db.ApiKeys.AnyAsync(k => k.KeyHash == hash && k.RevokedAt == null, ct))
            {
                db.ApiKeys.Add(new ApiKey
                {
                    OrganizationId = org, Name = "Demo tour", Prefix = parts[1], KeyHash = hash, Scopes = "exams",
                    CreatedByUserId = owner, CreatedAt = clock.GetUtcNow(),
                });
                await db.SaveChangesAsync(ct);
            }
            return null;   // the caller already knows it
        }
        if (await db.ApiKeys.AnyAsync(k => k.OrganizationId == org && k.Name == "Demo" && k.RevokedAt == null, ct)) return null;
        var (_, created) = await keys.CreateAsync(org, owner, "Demo", ct);
        return created;
    }

    internal static string MaterialPath(string contentRoot, string file)
    {
        foreach (var root in new[] { contentRoot, AppContext.BaseDirectory })
        {
            var path = Path.Combine(root, "Demo", "Material", file);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"Demo material {file} not found under Demo/Material (start the server with --contentRoot pointing at Gadfly.Server).", file);
    }

    internal static string GeneratePassword() =>
        "Demo-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(15)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=') + "-42!";

    private static void Print(ILogger log, string password, bool configured, string? apiKey)
    {
        var lines = new List<string>
        {
            $"Demo institution \"{OrganizationName}\" (development only):",
            $"  accounts: {string.Join(", ", Accounts.Select(a => $"{a.UserName} ({a.Role})"))}",
            configured ? $"  password: the configured {PasswordSetting}" : $"  password (shown once): {password}",
        };
        if (apiKey is not null) lines.Add($"  API key (shown once): {apiKey}");
        lines.Add("  assessments are created once the source job has processed the notes.");
        Console.WriteLine(string.Join(Environment.NewLine, lines));
        log.LogInformation("Demo institution seeded ({Org}).", OrganizationName);
    }
}

public sealed record DemoObjective(string Key, string Title, string Description, string ConceptTitle, decimal MaxScore);

public sealed record DemoAssessment(string Title, string Course, string Subject, string Topic, string Collection, string MaterialFile,
    string Instructions, IReadOnlyList<DemoObjective> Objectives, ExamMode Mode = ExamMode.Text);
