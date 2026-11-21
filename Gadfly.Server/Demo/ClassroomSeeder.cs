using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Demo;

/// <summary>
/// Classroom test accounts (GAD-T151): three teachers and twenty-six students in one organization, so the interface
/// can be tried with students who sit several exams. A Teacher (role Instructor) is the person who uploads the source
/// material the Socratic examiner draws meaning from; each teacher here owns one course, its notes, and a published,
/// policy-locked assessment built from them. Every student is assigned to the assessments of one, two or all three
/// teachers (9 / 9 / 8), chosen by a fixed random seed so the layout is the same every run.
///
/// Run: <c>Gadfly.Server --seed-classroom</c> (last argument). Source processing runs inside this process, so it also
/// works against a remote database (e.g. Azure SQL from a developer machine) without the server's job worker needing the
/// upload files. Uses whatever AI panel is configured: run it with <c>Gadfly:AiMode=mock</c> to spend nothing.
/// Re-running is safe: existing people, courses, collections and assessments are reused, missing assignments added.
/// </summary>
public static class ClassroomSeeder
{
    public const string SeedArgument = "--seed-classroom";
    public const string OrganizationName = "Gadfly Classroom";
    public const string OwnerUserName = "Classroom_Owner";
    public const string SeededAction = "classroom.seeded";
    public const int Seed = 20261121;

    /// <summary>Each teacher's course: the demo material it is built from.</summary>
    public static readonly IReadOnlyList<(string Teacher, DemoAssessment Assessment)> Teachers =
    [
        ("Teacher_A", new DemoAssessment(
            Title: "Teacher A: Photosynthesis", Course: "Teacher A: Biology", Subject: "Biology", Topic: "Photosynthesis",
            Collection: "Teacher A notes: Photosynthesis", MaterialFile: "photosynthesis.md",
            Instructions: "Explain the process in your own words. Say \"I don't know\" if you don't.",
            Objectives:
            [
                new DemoObjective("light-reactions", "Explain the light-dependent reactions", "Says where they happen, what light does, and where the oxygen comes from.", "Light-dependent reactions", 10m),
                new DemoObjective("calvin-cycle", "Explain the Calvin cycle", "Explains carbon fixation, its inputs from the light reactions, and why it stops in the dark.", "The Calvin cycle", 10m),
                new DemoObjective("limiting-factors", "Reason about limiting factors", "Predicts what happens to the rate when light, CO2 or temperature change, and why.", "Limiting factors", 10m),
            ])),
        ("Teacher_B", DemoSeeder.Assessments[0] with
        {
            Title = "Teacher B: Understanding multiplication", Course = "Teacher B: Mathematics", Collection = "Teacher B notes: Multiplication",
        }),
        ("Teacher_C", DemoSeeder.Assessments[1] with
        {
            Title = "Teacher C: Interaction effects in regression", Course = "Teacher C: Statistics", Collection = "Teacher C notes: Interaction effects",
        }),
    ];

    /// <summary>Student_A .. Student_Z.</summary>
    public static IReadOnlyList<string> Students => [.. Enumerable.Range('A', 26).Select(c => $"Student_{(char)c}")];

    /// <summary>
    /// Which teachers each student has: a fixed shuffle gives 9 students one teacher, 9 two and 8 all three; the teachers are
    /// then drawn at random for each student. Deterministic for a given <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Assign(int seed = Seed)
    {
        var rng = new Random(seed);
        var students = Students.OrderBy(_ => rng.Next()).ToList();
        var teachers = Teachers.Select(t => t.Teacher).ToList();
        var result = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        for (var i = 0; i < students.Count; i++)
        {
            var count = i < 9 ? 1 : i < 18 ? 2 : 3;
            result[students[i]] = teachers.OrderBy(_ => rng.Next()).Take(count).OrderBy(t => t, StringComparer.Ordinal).ToList();
        }
        return result;
    }

    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Classroom");
        var config = app.Configuration;
        if (!app.Environment.IsDevelopment() && config["Gadfly:Demo:AllowClassroomSeed"] != "true")
        {
            log.LogError("--seed-classroom runs in development, or with Gadfly:Demo:AllowClassroomSeed=true; nothing was seeded.");
            return;
        }
        var panel = app.Services.GetRequiredService<IPanelInfo>();
        log.LogWarning("Classroom seeding with the {Mode} AI panel.", panel.IsMock ? "MOCK" : "LIVE");
        await SeedAsync(app.Services.GetRequiredService<IServiceScopeFactory>(), config, app.Environment.ContentRootPath, log, CancellationToken.None);
    }

    public static async Task SeedAsync(IServiceScopeFactory scopes, IConfiguration config, string contentRoot, ILogger log, CancellationToken ct)
    {
        var configured = config[DemoSeeder.PasswordSetting];
        var password = string.IsNullOrWhiteSpace(configured) ? DemoSeeder.GeneratePassword() : configured.Trim();

        // ---- organization and people
        Guid orgId;
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        using (var scope = scopes.CreateSystemScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<GadflyDbContext>();
            var directory = sp.GetRequiredService<IOrganizationDirectory>();
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Name == OrganizationName, ct);
            if (org is null)
            {
                var created = await directory.CreateOrganizationAsync(OrganizationName, OwnerUserName, $"{OwnerUserName.ToLowerInvariant()}@demo.gadfly.invalid",
                    password, Guid.Empty, ct);
                if (!created.Ok || created.Id is null) throw new InvalidOperationException("Classroom organization not created: " + created.Message);
                org = await db.Organizations.FirstAsync(o => o.Id == created.Id, ct);
            }
            orgId = org.Id;
            var people = new List<(string, string)> { (OwnerUserName, OrgRoles.Owner) };
            people.AddRange(Teachers.Select(t => (t.Teacher, OrgRoles.Instructor)));
            people.AddRange(Students.Select(s => (s, OrgRoles.Student)));
            foreach (var (userName, role) in people)
                ids[userName] = await EnsurePersonAsync(sp, db, directory, orgId, userName, role, password, ct);
        }

        // ---- each teacher's course, notes (processed here) and assessment
        var assignment = Assign();
        var newAssignments = 0;
        foreach (var (teacher, a) in Teachers)
        {
            using var scope = scopes.CreateSystemScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<GadflyDbContext>();
            var clock = sp.GetRequiredService<TimeProvider>();
            var teacherId = ids[teacher];
            var studentIds = assignment.Where(kv => kv.Value.Contains(teacher)).Select(kv => ids[kv.Key]).ToList();

            var course = await db.Courses.FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Title == a.Course, ct);
            if (course is null)
            {
                course = new Course
                {
                    OrganizationId = orgId, Title = a.Course, Subject = a.Subject, CreatedByUserId = teacherId, CreatedAt = clock.GetUtcNow(),
                    Description = $"{teacher}'s course (classroom test data, GAD-T151).",
                };
                db.Courses.Add(course);
                await db.SaveChangesAsync(ct);
            }
            var collection = await db.SourceCollections.FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Title == a.Collection, ct);
            if (collection is null)
            {
                collection = new SourceCollection
                {
                    OrganizationId = orgId, CourseId = course.Id, Title = a.Collection, CreatedByUserId = teacherId,
                    CreatedAt = clock.GetUtcNow(), Status = ProcessingStatus.Pending, Description = $"Source notes uploaded by {teacher}.",
                };
                db.SourceCollections.Add(collection);
                await db.SaveChangesAsync(ct);
            }

            var maps = new SqlConceptMapStore(db, orgId);
            var map = await maps.LoadMapAsync(collection.Id.ToString(), ct);
            if (map is not { Concepts.Count: > 0 })
            {
                var folder = SourceUploads.FolderFor(orgId, collection.Id);
                Directory.CreateDirectory(folder);
                File.Copy(DemoSeeder.MaterialPath(contentRoot, a.MaterialFile), Path.Combine(folder, a.MaterialFile), overwrite: true);
                log.LogWarning("Processing {Teacher}'s notes ({File})...", teacher, a.MaterialFile);
                var job = new SourceProcessingJob(db, sp.GetRequiredService<ISocraticVotingClient>(), sp.GetServices<IDocumentTextExtractor>(),
                    sp.GetRequiredService<Platform.UsageMeter>());
                await job.RunAsync(new Job
                {
                    Kind = SourceProcessingJob.JobKind, OrganizationId = orgId,
                    PayloadJson = JsonSerializer.Serialize(new SourceProcessingRequest(orgId, collection.Id, teacherId, collection.Title, folder)),
                }, new NoProgress(), ct);
                db.ChangeTracker.Clear();
                map = await maps.LoadMapAsync(collection.Id.ToString(), ct);
            }
            if (map is not { Concepts.Count: > 0 })
            {
                log.LogError("{Teacher}'s assessment not created: no concept map.", teacher);
                continue;
            }

            var exam = await db.Exams.FirstOrDefaultAsync(e => e.OrganizationId == orgId && e.Title == a.Title, ct);
            if (exam is null)
            {
                await DemoSeeder.CreateAssessmentAsync(db, clock, orgId, teacherId, studentIds, a, course.Id, collection.Id, map,
                    $"Classroom test assessment seeded by {SeedArgument} (GAD-T151).", ct);
                newAssignments += studentIds.Count;
            }
            else
            {
                var have = await db.ExamAssignments.Where(x => x.ExamId == exam.Id).Select(x => x.StudentUserId).ToListAsync(ct);
                foreach (var sid in studentIds.Except(have))
                {
                    db.ExamAssignments.Add(new ExamAssignment
                    {
                        OrganizationId = orgId, ExamId = exam.Id, StudentUserId = sid, AttemptsAllowed = 50,
                        AssignedByUserId = teacherId, AssignedAt = clock.GetUtcNow(),
                    });
                    newAssignments++;
                }
                await db.SaveChangesAsync(ct);
            }
            log.LogWarning("{Teacher}: \"{Title}\" assigned to {Count} students.", teacher, a.Title, studentIds.Count);
        }

        using (var scope = scopes.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = orgId, Action = SeededAction, EntityType = "Organization", EntityId = orgId.ToString(),
                At = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(), Reason = $"Classroom test data seeded by {SeedArgument} (GAD-T151).",
                AfterJson = JsonSerializer.Serialize(new { teachers = Teachers.Select(t => t.Teacher), students = Students.Count, newAssignments }),
            });
            await db.SaveChangesAsync(ct);
        }

        var lines = assignment.Select(kv => $"  {kv.Key,-10} {string.Join(", ", kv.Value)}");
        log.LogWarning("Classroom seeded in \"{Org}\" ({New} new assignments). Sign in as {Owner}, Teacher_A..C or Student_A..Z with password {Password}{Note}.\n{Lines}",
            OrganizationName, newAssignments, OwnerUserName, password,
            string.IsNullOrWhiteSpace(configured) ? " (generated this run; set Gadfly:Demo:Password to choose one)" : "", string.Join("\n", lines));
    }

    private static async Task<Guid> EnsurePersonAsync(IServiceProvider sp, GadflyDbContext db, IOrganizationDirectory directory, Guid orgId,
        string userName, string role, string password, CancellationToken ct)
    {
        var normalized = MindAttic.Authentication.Services.IUserStore.Normalize(userName);
        var existing = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        Guid id;
        if (existing is not null && await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == orgId && m.UserId == existing.Id, ct))
        {
            id = existing.Id;
        }
        else
        {
            var added = await directory.AddMemberAsync(orgId, userName, $"{userName.ToLowerInvariant()}@demo.gadfly.invalid", role, password, ct);
            if (!added.Ok || added.Id is null) throw new InvalidOperationException($"{userName} not created: {added.Message}");
            id = added.Id.Value;
        }
        // Test accounts sign straight in with the classroom password: no forced change, no MFA enrollment.
        await sp.GetRequiredService<IUserAdminService>().ResetPasswordAsync(id, password, false, ct);
        await db.AuthUsers.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.MustChangePassword, false)
            .SetProperty(u => u.MustEnrollMfa, false), ct);
        // The update above bypassed the change tracker; drop the stale tracked copy so a later save can't trip its concurrency token.
        db.ChangeTracker.Clear();
        return id;
    }

    private sealed class NoProgress : IJobProgress
    {
        public Task ReportAsync(double fraction, string? message = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
