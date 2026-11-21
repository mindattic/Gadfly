using System.Security.Cryptography;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// The platform API for apps that integrate the Socratic method (GAD-T46), versioned under
/// <c>/api/v1</c>, authenticated by an organization API key (<c>Authorization: Bearer gfk_...</c>).
/// Every call is scoped to the key's organization by the tenant filter.
/// </summary>
public static class ApiEndpoints
{
    public sealed record CreateCollection(string Title);
    public sealed record ProcessRequest(bool RebuildConceptMap = false);
    public sealed record CreateExam(Guid CollectionId, string Title, IReadOnlyList<string>? ConceptKeys, int Minutes = 20,
        int Difficulty = 3, int MaxTurns = 30, string Mode = "Text", string? Instructions = null);
    public sealed record AssignStudent(string UserName, string? Email, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int AttemptsAllowed = 1);
    public sealed record CreateWebhook(string Url, IReadOnlyList<string> Events);

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(GadflyPolicies.Api).DisableAntiforgery();

        api.MapPost("/collections", async (CreateCollection body, ITenantContext t, GadflyDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest(new { error = "title is required" });
            var c = new SourceCollection
            {
                OrganizationId = t.OrganizationId!.Value, Title = body.Title.Trim(), CreatedAt = clock.GetUtcNow(), Status = ProcessingStatus.Pending,
            };
            db.SourceCollections.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/collections/{c.Id}", new { id = c.Id, c.Title });
        });

        api.MapPost("/collections/{id:guid}/documents", async (Guid id, HttpRequest request, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            if (!await db.SourceCollections.AnyAsync(c => c.Id == id, ct)) return Results.NotFound();
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "send multipart/form-data with one or more files" });
            var form = await request.ReadFormAsync(ct);
            var folder = SourceUploads.FolderFor(t.OrganizationId!.Value, id);
            Directory.CreateDirectory(folder);
            var saved = new List<string>();
            foreach (var file in form.Files)
            {
                if (file.Length is 0 or > MaxUploadBytes) return Results.BadRequest(new { error = $"{file.FileName}: files must be 1 byte to 20 MB" });
                var name = SafeFileName(file.FileName);
                await using var target = File.Create(Path.Combine(folder, name));
                await file.CopyToAsync(target, ct);
                saved.Add(name);
            }
            return Results.Ok(new { saved });
        });

        api.MapPost("/collections/{id:guid}/process", async (Guid id, ProcessRequest? body, ITenantContext t, GadflyDbContext db, JobQueue jobs, CancellationToken ct) =>
        {
            var c = await db.SourceCollections.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var jobId = await jobs.EnqueueAsync(SourceProcessingJob.JobKind,
                new SourceProcessingRequest(t.OrganizationId!.Value, id, Guid.Empty, c.Title, SourceUploads.FolderFor(t.OrganizationId!.Value, id), body?.RebuildConceptMap ?? false),
                t.OrganizationId, ct: ct);
            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId });
        });

        api.MapGet("/jobs/{id:guid}", async (Guid id, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            var j = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == t.OrganizationId, ct);
            return j is null ? Results.NotFound() : Results.Ok(new { j.Id, j.Kind, state = j.State.ToString(), j.Progress, j.ProgressMessage, j.Error, j.CompletedAt });
        });

        api.MapGet("/collections/{id:guid}/concepts", async (Guid id, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            var map = await new SqlConceptMapStore(db, t.OrganizationId!.Value).LoadMapAsync(id.ToString(), ct);
            return map is null ? Results.NotFound() : Results.Ok(new
            {
                map.Version,
                concepts = map.Concepts.Select(c => new { c.Key, c.Title, c.Definition }),
                map.Relations,
            });
        });

        api.MapPost("/exams", async (CreateExam body, ITenantContext t, GadflyDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var map = await new SqlConceptMapStore(db, t.OrganizationId!.Value).LoadMapAsync(body.CollectionId.ToString(), ct);
            if (map is null) return Results.BadRequest(new { error = "the collection has no concept map yet; process it first" });
            var keys = body.ConceptKeys is { Count: > 0 } k ? k.ToList() : map.Concepts.Select(c => c.Key).ToList();
            var unknown = keys.Where(x => map.Find(x) is null).ToList();
            if (unknown.Count > 0) return Results.BadRequest(new { error = "unknown concept keys", unknown });
            var policy = new SessionPolicy
            {
                ConceptIds = keys, TimeBudget = TimeSpan.FromMinutes(Math.Clamp(body.Minutes, 1, 240)),
                Difficulty = Math.Clamp(body.Difficulty, 1, 5), MaxTurns = Math.Clamp(body.MaxTurns, 1, 200),
            };
            try { policy.Validate(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            var exam = new Exam
            {
                OrganizationId = t.OrganizationId!.Value, CollectionId = body.CollectionId, Title = body.Title.Trim(),
                Instructions = body.Instructions, PolicyJson = JsonSerializer.Serialize(policy, Camel),
                Mode = string.Equals(body.Mode, "Voice", StringComparison.OrdinalIgnoreCase) ? ExamMode.Voice : ExamMode.Text,
                PromptSetId = ExamPrompts.SetId, PromptSetVersion = ExamPrompts.Version,
                Status = ExamStatus.Published, CreatedAt = clock.GetUtcNow(), PublishedAt = clock.GetUtcNow(),
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/exams/{exam.Id}", new { id = exam.Id, conceptKeys = keys });
        });

        // Provisions the student in the organization (new accounts get a temporary password, returned
        // once, which they must change at first sign-in) and assigns the exam.
        api.MapPost("/exams/{id:guid}/assignments", async (Guid id, AssignStudent body, ITenantContext t, GadflyDbContext db,
            IOrganizationDirectory directory, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Exams.AnyAsync(e => e.Id == id && e.Status == ExamStatus.Published, ct)) return Results.NotFound();
            var org = t.OrganizationId!.Value;
            var normalized = MindAttic.Authentication.Services.IUserStore.Normalize(body.UserName ?? "");
            var isNew = !await db.AuthUsers.AnyAsync(u => u.NormalizedUserName == normalized, ct);
            var temporary = isNew ? TemporaryPassword() : null;
            var member = await directory.AddMemberAsync(org, body.UserName ?? "", body.Email, OrgRoles.Student, temporary ?? TemporaryPassword(), ct);
            if (!member.Ok || member.Id is null) return Results.BadRequest(new { error = member.Message });
            var existing = await db.ExamAssignments.FirstOrDefaultAsync(a => a.ExamId == id && a.StudentUserId == member.Id, ct);
            if (existing is null)
            {
                existing = new ExamAssignment
                {
                    OrganizationId = org, ExamId = id, StudentUserId = member.Id.Value, OpensAt = body.OpensAt, ClosesAt = body.ClosesAt,
                    AttemptsAllowed = Math.Max(1, body.AttemptsAllowed), AssignedAt = clock.GetUtcNow(),
                };
                db.ExamAssignments.Add(existing);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { assignmentId = existing.Id, studentUserId = member.Id, temporaryPassword = temporary });
        });

        // A signed, expiring link (15 minutes) that brings the student to this assignment's start page
        // after signing in. One-click sign-in from the link needs M.AUTH trusted-launch support.
        api.MapPost("/assignments/{id:guid}/launch", async (Guid id, HttpRequest request, GadflyDbContext db, IDataProtectionProvider dp, CancellationToken ct) =>
        {
            var a = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var token = LaunchTokens.Protector(dp).Protect($"{a.Id:N}|{a.StudentUserId:N}", TimeSpan.FromMinutes(15));
            var url = $"{request.Scheme}://{request.Host}/launch?token={Uri.EscapeDataString(token)}";
            return Results.Ok(new { url, expiresInSeconds = 900 });
        });

        api.MapGet("/sessions/{id:guid}/result", async (Guid id, GadflyDbContext db, CancellationToken ct) =>
        {
            var s = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var report = await db.SessionReports.AsNoTracking().FirstOrDefaultAsync(r => r.SessionId == id, ct);
            var grade = await db.GradeDecisions.AsNoTracking().Where(g => g.SessionId == id).OrderByDescending(g => g.DecidedAt).FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                s.Id, s.AssignmentId, s.StudentUserId, state = s.State.ToString(), s.StartedAt, s.EndedAt, s.EndReason,
                recommendation = report?.Recommendation, overallScore = report?.OverallScore,
                report = report is null ? (object?)null : JsonDocument.Parse(report.ReportJson).RootElement,
                grade = grade is null ? null : new { grade.Grade, grade.Score, grade.Comment, grade.DecidedAt },
            });
        });

        api.MapGet("/assignments/{id:guid}/sessions", async (Guid id, GadflyDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ExamSessions.AsNoTracking().Where(s => s.AssignmentId == id).OrderBy(s => s.Attempt)
                .Select(s => new { s.Id, s.Attempt, state = s.State.ToString(), s.StartedAt, s.EndedAt, s.EndReason }).ToListAsync(ct)));

        api.MapPost("/webhooks", async (CreateWebhook body, ITenantContext t, WebhookService hooks, CancellationToken ct) =>
        {
            try
            {
                var (row, secret) = await hooks.CreateAsync(t.OrganizationId!.Value, body.Url, body.Events, ct);
                return Results.Created($"/api/v1/webhooks/{row.Id}", new { id = row.Id, row.Url, events = row.Events.Split(','), secret });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/webhooks", async (GadflyDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Webhooks.AsNoTracking().Select(w => new { w.Id, w.Url, w.Events, w.IsActive, w.CreatedAt }).ToListAsync(ct)));

        api.MapDelete("/webhooks/{id:guid}", async (Guid id, GadflyDbContext db, CancellationToken ct) =>
            await db.Webhooks.Where(w => w.Id == id).ExecuteUpdateAsync(u => u.SetProperty(w => w.IsActive, false), ct) > 0
                ? Results.NoContent() : Results.NotFound());

        // The student-facing end of a launch link: verify, then go to the assignment (signing in first if needed).
        app.MapGet("/launch", (string token, HttpContext http, IDataProtectionProvider dp, ITenantContext tenant) =>
        {
            string payload;
            try { payload = LaunchTokens.Protector(dp).Unprotect(token); }
            catch (CryptographicException) { return Results.Content("This exam link has expired or is not valid. Ask for a new one.", "text/plain"); }
            var parts = payload.Split('|');
            var assignmentId = Guid.Parse(parts[0]);
            var studentId = Guid.Parse(parts[1]);
            var target = $"/my-exams/{assignmentId}/start";
            if (tenant.UserId is null) return Results.LocalRedirect($"/login?returnUrl={Uri.EscapeDataString(target)}");
            if (tenant.UserId != studentId) return Results.Content("This exam link belongs to a different student. Sign out and sign in as the right person.", "text/plain");
            return Results.LocalRedirect(target);
        }).AllowAnonymous();
    }

    private static string SafeFileName(string name)
    {
        var clean = new string(Path.GetFileName(name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "upload.txt" : clean.Length > 150 ? clean[^150..] : clean;
    }

    private static string TemporaryPassword() =>
        "Gf-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).TrimEnd('=').Replace('+', 'x').Replace('/', 'y');
}

/// <summary>Time-limited, signed launch tokens (ASP.NET Core Data Protection).</summary>
public static class LaunchTokens
{
    public static ITimeLimitedDataProtector Protector(IDataProtectionProvider dp) =>
        dp.CreateProtector("Gadfly.Launch.v1").ToTimeLimitedDataProtector();
}
