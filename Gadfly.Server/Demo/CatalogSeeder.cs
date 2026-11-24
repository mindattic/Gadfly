using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Demo;

/// <summary>
/// The exam catalog (GAD-T659, goal GAD-T655): categories and published, open-to-all-students voice exams read from the
/// checked-in data under <c>Demo/Catalog</c> (<c>categories.json</c> and every <c>exams/*.json</c>). Each exam's source passage,
/// concept map and rubric are written straight from the data, so seeding makes no LLM calls; the data is human-authored and
/// the seeding admin is recorded as approving that concept map and rubric (GAD-T437).
/// <para>
/// Run: <c>Gadfly.Server --seed-catalog [--catalog-org "Name"] --then-exit</c>, in development or with
/// <c>Gadfly:Demo:AllowCatalogSeed=true</c> (switch it on for the run and off after). The target organization is
/// <c>--catalog-org</c>, else <c>Gadfly:Demo:CatalogOrganization</c>, else the one organization holding the most of the live
/// accounts admin, ryan and erin. Idempotent: categories are matched by slug and exams by title; an existing exam is left
/// alone (published exams are frozen) apart from its category and its approved translations.
/// </para>
/// </summary>
public static class CatalogSeeder
{
    public const string SeedArgument = "--seed-catalog";
    public const string OrgArgument = "--catalog-org";
    public const string AllowSetting = "Gadfly:Demo:AllowCatalogSeed";
    public const string OrgSetting = "Gadfly:Demo:CatalogOrganization";
    public const string SeededAction = "catalog.seeded";
    public static readonly string[] LiveAccounts = ["admin", "ryan", "erin"];

    private static readonly JsonSerializerOptions DataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Catalog");
        if (!app.Environment.IsDevelopment() && app.Configuration[AllowSetting] != "true")
        {
            log.LogError("{Arg} runs in development, or with {Setting}=true; nothing was seeded.", SeedArgument, AllowSetting);
            return;
        }
        var i = Array.IndexOf(args, OrgArgument);
        var orgName = i >= 0 && i + 1 < args.Length ? args[i + 1] : app.Configuration[OrgSetting];
        var catalog = Load(app.Environment.ContentRootPath);
        await SeedAsync(app.Services.GetRequiredService<IServiceScopeFactory>(), catalog, orgName, log, CancellationToken.None);
    }

    // ------------------------------------------------------------------ data

    public sealed record CatalogData(IReadOnlyList<CatalogCategory> Categories, IReadOnlyList<CatalogExam> Exams);

    /// <summary>A category; <see cref="DisplayNames"/> maps a language tag ("ko", "es") to its name there.</summary>
    public sealed record CatalogCategory(string Slug, string Name, int SortOrder, Dictionary<string, string>? DisplayNames);

    /// <summary>One exam. <see cref="Passage"/> is the source material, one paragraph per entry (each becomes a passage).</summary>
    public sealed record CatalogExam(string Title, string Category, string Subject, string Topic, string? Instructions,
        IReadOnlyList<string> Passage, IReadOnlyList<CatalogObjective> Objectives, Dictionary<string, CatalogTranslation>? Translations);

    /// <summary>
    /// One learning objective and the concept it is grounded in: <see cref="Facts"/> are what a full answer shows (the rubric's
    /// understanding points), <see cref="Misconceptions"/> what the judge watches for.
    /// </summary>
    public sealed record CatalogObjective(string Key, string Title, string Description, string Concept, string Definition,
        IReadOnlyList<string> Facts, IReadOnlyList<string>? Misconceptions);

    /// <summary>
    /// The student-facing texts in one other language (GAD-T661 adds ko and es), seeded as an approved <see cref="ExamLocalization"/>.
    /// <see cref="Objectives"/> maps an objective key to its title in that language.
    /// </summary>
    public sealed record CatalogTranslation(string Title, string? Instructions, Dictionary<string, string>? Objectives);

    public const string DefaultInstructions =
        "The examiner will talk with you about this topic. Answer in your own words and explain why, not only what. Say \"I don't know\" if you don't.";

    public static CatalogData Load(string contentRoot)
    {
        var dir = new[] { contentRoot, AppContext.BaseDirectory }.Select(r => Path.Combine(r, "Demo", "Catalog")).FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException("Demo/Catalog not found (start the server with --contentRoot pointing at Gadfly.Server).");
        var categories = JsonSerializer.Deserialize<List<CatalogCategory>>(File.ReadAllText(Path.Combine(dir, "categories.json")), DataJson) ?? [];
        var exams = Directory.EnumerateFiles(Path.Combine(dir, "exams"), "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => JsonSerializer.Deserialize<List<CatalogExam>>(File.ReadAllText(f), DataJson) ?? []).ToList();
        var problems = Validate(categories, exams);
        if (problems.Count > 0) throw new InvalidDataException("Catalog data is invalid:\n" + string.Join("\n", problems));
        return new CatalogData(categories, exams);
    }

    public const int MaxObjectives = 4;

    public static List<string> Validate(IReadOnlyList<CatalogCategory> categories, IReadOnlyList<CatalogExam> exams)
    {
        var problems = new List<string>();
        var slugs = categories.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);
        if (slugs.Count != categories.Count) problems.Add("Category slugs must be unique.");
        foreach (var dup in exams.GroupBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"Duplicate exam title \"{dup.Key}\".");
        foreach (var e in exams)
        {
            if (!slugs.Contains(e.Category)) problems.Add($"\"{e.Title}\": unknown category {e.Category}.");
            if (e.Passage is not { Count: > 0 }) problems.Add($"\"{e.Title}\": no passage.");
            if (e.Objectives is not { Count: > 0 }) problems.Add($"\"{e.Title}\": no objectives.");
            else if (e.Objectives.Count > MaxObjectives) problems.Add($"\"{e.Title}\": {e.Objectives.Count} objectives; a catalog exam has at most {MaxObjectives} so the 16-turn session has room to cross-examine.");
            else if (e.Objectives.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count() != e.Objectives.Count) problems.Add($"\"{e.Title}\": objective keys repeat.");
            foreach (var o in e.Objectives ?? []) if (o.Facts is not { Count: > 0 }) problems.Add($"\"{e.Title}\" / {o.Key}: no facts.");
        }
        return problems;
    }

    // ------------------------------------------------------------------ seeding

    public static async Task SeedAsync(IServiceScopeFactory scopes, CatalogData catalog, string? orgName, ILogger log, CancellationToken ct)
    {
        Guid orgId, actorId;
        var categoryIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var courseIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        using (var scope = scopes.CreateSystemScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<GadflyDbContext>();
            var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
            (orgId, actorId) = await TargetAsync(db, orgName, ct);
            foreach (var c in catalog.Categories)
            {
                var row = await db.ExamCategories.FirstOrDefaultAsync(x => x.OrganizationId == orgId && x.Slug == c.Slug, ct);
                if (row is null)
                {
                    row = new ExamCategory { OrganizationId = orgId, Slug = c.Slug, CreatedByUserId = actorId, CreatedAt = now };
                    db.ExamCategories.Add(row);
                }
                row.Name = c.Name;
                row.SortOrder = c.SortOrder;
                row.DisplayNamesJson = JsonSerializer.Serialize(c.DisplayNames ?? []);
                categoryIds[c.Slug] = row.Id;

                var courseTitle = $"Catalog: {c.Name}";
                var course = await db.Courses.FirstOrDefaultAsync(x => x.OrganizationId == orgId && x.Title == courseTitle, ct);
                if (course is null)
                {
                    course = new Course
                    {
                        OrganizationId = orgId, Title = courseTitle, Subject = c.Name, CreatedByUserId = actorId, CreatedAt = now,
                        Description = $"Open exam catalog, {c.Name} (seeded by {SeedArgument}, GAD-T659).",
                    };
                    db.Courses.Add(course);
                }
                courseIds[c.Slug] = course.Id;
            }
            await db.SaveChangesAsync(ct);
        }

        int created = 0, existing = 0;
        foreach (var e in catalog.Exams)
        {
            using var scope = scopes.CreateSystemScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<GadflyDbContext>();
            var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
            var exam = await db.Exams.FirstOrDefaultAsync(x => x.OrganizationId == orgId && x.Title == e.Title, ct);
            if (exam is null)
            {
                exam = await CreateExamAsync(db, now, orgId, actorId, e, courseIds[e.Category], ct);
                created++;
            }
            else existing++;
            exam.CategoryId = categoryIds[e.Category];
            await UpsertTranslationsAsync(db, now, orgId, actorId, exam.Id, e, ct);
            await db.SaveChangesAsync(ct);
        }

        using (var scope = scopes.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = orgId, ActorUserId = actorId, Action = SeededAction, EntityType = "Organization", EntityId = orgId.ToString(),
                At = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(), Reason = $"Exam catalog seeded by {SeedArgument} (GAD-T659).",
                AfterJson = JsonSerializer.Serialize(new { categories = catalog.Categories.Count, exams = catalog.Exams.Count, created, existing }),
            });
            await db.SaveChangesAsync(ct);
        }
        log.LogWarning("Exam catalog: {Categories} categories, {Created} exams created and {Existing} already there.", catalog.Categories.Count, created, existing);
    }

    /// <summary>The target organization and the member who seeds as its author (admin if a member, else an Owner or Admin).</summary>
    private static async Task<(Guid Org, Guid Actor)> TargetAsync(GadflyDbContext db, string? orgName, CancellationToken ct)
    {
        var normalized = LiveAccounts.Select(IUserStore.Normalize).ToList();
        var live = await db.AuthUsers.AsNoTracking().Where(u => normalized.Contains(u.NormalizedUserName))
            .Select(u => new { u.Id, u.NormalizedUserName }).ToListAsync(ct);
        Guid orgId;
        if (!string.IsNullOrWhiteSpace(orgName))
        {
            orgId = await db.Organizations.AsNoTracking().Where(o => o.Name == orgName.Trim()).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException($"No organization named \"{orgName}\".");
        }
        else
        {
            var ids = live.Select(u => u.Id).ToList();
            var counts = await db.OrganizationMembers.AsNoTracking().Where(m => ids.Contains(m.UserId) && m.IsActive)
                .GroupBy(m => m.OrganizationId).Select(g => new { Org = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
            if (counts.Count == 0) throw new InvalidOperationException($"None of {string.Join(", ", LiveAccounts)} is in an organization; pass {OrgArgument} \"Name\".");
            if (counts.Count > 1 && counts[0].Count == counts[1].Count)
                throw new InvalidOperationException($"Several organizations hold the same number of {string.Join(", ", LiveAccounts)}; pass {OrgArgument} \"Name\" or set {OrgSetting}.");
            orgId = counts[0].Org;
        }
        var members = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == orgId && m.IsActive)
            .Select(m => new { m.UserId, m.Role }).ToListAsync(ct);
        var admin = live.FirstOrDefault(u => u.NormalizedUserName == IUserStore.Normalize("admin") && members.Any(m => m.UserId == u.Id));
        var actor = admin?.Id ?? members.Where(m => m.Role is OrgRoles.Owner or OrgRoles.Admin).Select(m => (Guid?)m.UserId).FirstOrDefault()
            ?? throw new InvalidOperationException("The target organization has no Owner or Admin to seed as.");
        return (orgId, actor);
    }

    private static async Task<Exam> CreateExamAsync(GadflyDbContext db, DateTimeOffset now, Guid org, Guid actor, CatalogExam e, Guid courseId, CancellationToken ct)
    {
        var slug = Slug(e.Title);
        var collection = new SourceCollection
        {
            OrganizationId = org, CourseId = courseId, Title = $"Catalog: {e.Title}", CreatedByUserId = actor, CreatedAt = now,
            Status = ProcessingStatus.Processing, Description = $"Catalog source passage (seeded by {SeedArgument}, GAD-T659).",
        };
        db.SourceCollections.Add(collection);
        await db.SaveChangesAsync(ct);

        // Source passage, concept map and rubric straight from the data: no extraction, no LLM.
        var cid = collection.Id.ToString();
        var fileName = slug + ".md";
        var docId = SourceIngestor.DocumentId(fileName);
        var text = string.Join("\n\n", e.Passage);
        await new SqlSourceStore(db, org, actor).SaveDocumentAsync(cid, new SourceDocumentInfo
        {
            Id = docId, FileName = fileName, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))),
            PassageCount = e.Passage.Count, IngestedAt = now,
        }, e.Passage.Select((p, i) => new SourcePassage { Id = $"{docId}:{i}", DocumentId = docId, Ordinal = i, Text = p, Location = $"{fileName} > {e.Topic}" }).ToList(), ct);
        var passageIds = (await new SqlSourceStore(db, org, actor).GetPassagesAsync(cid, ct)).Select(p => p.Id).ToList();
        var maps = new SqlConceptMapStore(db, org);
        await maps.SaveMapAsync(new ConceptMap
        {
            CollectionId = cid,
            Concepts = e.Objectives.Select(o => new ConceptEntry
            {
                Key = o.Key, Title = o.Concept, Definition = o.Definition, KeyFacts = o.Facts, Misconceptions = o.Misconceptions ?? [],
                PassageIds = passageIds, EditedByInstructor = true,
            }).ToList(),
        }, ct);
        await maps.SaveRubricsAsync(cid, e.Objectives.Select(o => new ConceptRubric
        {
            ConceptKey = o.Key, EditedByInstructor = true,
            Understanding = o.Facts.Select(f => new RubricItem(f, 1, passageIds)).ToList(),
            Misconceptions = (o.Misconceptions ?? []).Select(m => new RubricItem(m, 1, passageIds)).ToList(),
        }).ToList(), ct);
        db.ChangeTracker.Clear();
        var version = await db.SourceCollections.Where(c => c.Id == collection.Id).Select(c => c.ConceptMapVersion).FirstAsync(ct);

        var exam = new Exam
        {
            OrganizationId = org, CollectionId = collection.Id, CourseId = courseId, Subject = e.Subject, Topic = e.Topic,
            AssessmentMode = AssessmentMode.Examination, Title = e.Title, Instructions = e.Instructions ?? DefaultInstructions,
            Mode = ExamMode.Voice, PromptSetId = ExamPrompts.SetId, PromptSetVersion = ExamPrompts.Version,
            Status = ExamStatus.Published, CreatedByUserId = actor, CreatedAt = now, PublishedAt = now, OpenToAllStudents = true,
        };
        var objectives = new List<LearningObjective>();
        for (var i = 0; i < e.Objectives.Count; i++)
        {
            var o = e.Objectives[i];
            var objective = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = o.Key, Title = o.Title, Description = o.Description,
                ConceptKey = o.Key, Weight = 1m, MaxScore = 10m, Ordinal = i,
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
            objectives.Add(objective);
        }
        // The policy the publish step would write (AssessmentService.BuildSessionPolicy), with the approval of the data's concept map
        // and rubric. A catalog exam has three or four objectives, so all of them examined is what makes a session summative.
        var basis = new SessionPolicy
        {
            TimeBudget = TimeSpan.FromMinutes(20), MaxTurns = 16, Difficulty = 2, Seed = 20261123,
            MinSummativeObjectives = Math.Min(ExamObjectives.DefaultMinSummativeObjectives, objectives.Count),
            RubricApproval = new RubricApproval(version, actor, now),
        };
        exam.PolicyJson = JsonSerializer.Serialize(basis, Camel);
        var policy = AssessmentService.BuildSessionPolicy(exam, objectives);
        policy.Validate();
        exam.PolicyJson = JsonSerializer.Serialize(policy, Camel);
        db.Exams.Add(exam);
        db.LearningObjectives.AddRange(objectives);
        db.GradingPolicies.Add(new GradingPolicy
        {
            ExamId = exam.Id, OrganizationId = org, AiWeight = 0.4m, HumanWeight = 0.6m, AdditionalReviewAt = 10m,
            MandatoryReconciliationAt = 20m, BlindHumanGrading = true, InconclusiveProcedure = InconclusiveProcedure.HumanReview,
            AllowAssistedEvidence = false, LockedAt = now, LockedByUserId = actor,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org, ActorUserId = actor, ActorRole = OrgRoles.Admin, Action = SeededAction, EntityType = nameof(Exam),
            EntityId = exam.Id.ToString(), At = now, Reason = $"Catalog exam seeded by {SeedArgument} (GAD-T659).",
            AfterJson = JsonSerializer.Serialize(new { exam.Title, e.Category, objectives = e.Objectives.Select(o => o.Key), conceptMapVersion = version }),
        });
        return exam;
    }

    private static async Task UpsertTranslationsAsync(GadflyDbContext db, DateTimeOffset now, Guid org, Guid actor, Guid examId, CatalogExam e, CancellationToken ct)
    {
        foreach (var (language, t) in e.Translations ?? [])
        {
            var tag = language.Trim().ToLowerInvariant();
            if (tag == "en") continue;
            var row = await db.ExamLocalizations.FirstOrDefaultAsync(x => x.OrganizationId == org && x.ExamId == examId && x.Language == tag, ct);
            if (row is null)
            {
                row = new ExamLocalization { OrganizationId = org, ExamId = examId, Language = tag };
                db.ExamLocalizations.Add(row);
            }
            row.Title = t.Title;
            row.Instructions = t.Instructions;
            row.ObjectiveTitlesJson = JsonSerializer.Serialize(t.Objectives ?? []);
            row.Status = ExamLocalizationStatus.Approved;
            row.UpdatedByUserId = row.ApprovedByUserId = actor;
            row.UpdatedAt = now;
            row.ApprovedAt = now;
        }
    }

    internal static string Slug(string title)
    {
        var chars = title.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray();
        var s = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return s.Length > 80 ? s[..80] : s;
    }
}
