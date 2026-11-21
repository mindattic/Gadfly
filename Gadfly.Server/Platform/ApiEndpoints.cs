using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace Gadfly.Server.Platform;

/// <summary>
/// The platform API for apps that integrate the Socratic method (GAD-T46), versioned under
/// <c>/api/v1</c>, authenticated by an organization API key (<c>Authorization: Bearer gfk_...</c>).
/// Every call is scoped to the key's organization by the tenant filter.
/// </summary>
public static class ApiEndpoints
{
    // ------------------------------------------------------------------ request shapes

    public sealed record CreateCollection(string? Title);
    public sealed record ProcessRequest(bool RebuildConceptMap = false);
    public sealed record CreateExam(Guid CollectionId, string? Title, IReadOnlyList<string>? ConceptKeys, int Minutes = 20,
        int Difficulty = 3, int MaxTurns = 30, string? Mode = "Text", string? Instructions = null);
    public sealed record AssignStudent(string? UserName, string? Email, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int? AttemptsAllowed = null);
    public sealed record CreateWebhook(string? Url, IReadOnlyList<string>? Events);

    // ------------------------------------------------------------------ response shapes

    /// <summary>Every 4xx with a body; <c>unknown</c> is present only for unknown concept keys.</summary>
    public sealed record ApiError(string Error,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Unknown = null);
    public sealed record CollectionCreated(Guid Id, string Title);
    public sealed record JobRef(Guid Id, string State, double Progress, string? ProgressMessage, string? Error,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
    public sealed record CollectionSummary(Guid Id, string Title, string Status, int DocumentCount, int ConceptCount,
        int ConceptMapVersion, DateTimeOffset CreatedAt, JobRef? LatestJob);
    public sealed record CollectionDocument(string FileName, long? SizeBytes, bool Uploaded, bool Ingested, bool ChangedSinceIngest,
        int PassageCount, DateTimeOffset? UploadedAt, DateTimeOffset? IngestedAt, string? Error);
    public sealed record CollectionDetail(Guid Id, string Title, string Status, int ConceptMapVersion, DateTimeOffset CreatedAt,
        IReadOnlyList<CollectionDocument> Documents, JobRef? LatestJob);
    public sealed record UploadResult(IReadOnlyList<string> Saved);
    public sealed record JobAccepted(Guid JobId);
    public sealed record JobStatus(Guid Id, string Kind, string State, double Progress, string? ProgressMessage, string? Error,
        DateTimeOffset? CompletedAt);
    public sealed record ConceptView(string Key, string Title, string Definition);
    public sealed record ConceptMapView(int Version, IReadOnlyList<ConceptView> Concepts, IReadOnlyList<Gadfly.Sources.ConceptRelationEntry> Relations);
    public sealed record ExamCreated(Guid Id, IReadOnlyList<string> ConceptKeys);
    public sealed record AssignmentResult(Guid AssignmentId, Guid StudentUserId, bool Created, string? TemporaryPassword);
    public sealed record LaunchLink(string Url, int ExpiresInSeconds);
    public sealed record AttemptView(Guid Id, int Attempt, string State, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, string? EndReason);
    public sealed record WebhookCreated(Guid Id, string Url, IReadOnlyList<string> Events, string Secret);
    public sealed record WebhookView(Guid Id, string Url, IReadOnlyList<string> Events, bool IsActive, DateTimeOffset CreatedAt);

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const long MaxUploadBytes = SourceLibraryService.MaxFileBytes;
    private const int MaxTitleLength = 300;
    /// <summary>The same cap the assessment editor puts on the instructions students see (GAD-T210).</summary>
    private const int MaxInstructionsLength = 4000;
    /// <summary>The account table's column limit for user names and emails (GAD-T211).</summary>
    private const int MaxAccountTextLength = 256;
    private const string SecuritySchemeId = "ApiKey";

    private static IResult Error(string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new ApiError(message), statusCode: status);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(GadflyPolicies.Api).DisableAntiforgery().RequireRateLimiting(RateLimits.Api)
            .WithTags("Platform API v1")
            .AddOpenApiOperationTransformer(DeclareApiKeySecurity);

        api.MapGet("/collections", async (ITenantContext t, SourceLibraryService library, CancellationToken ct) =>
            Results.Ok((await library.ListCollectionsAsync(t.OrganizationId!.Value, ct)).Select(c => new CollectionSummary(
                c.Id, c.Title, c.Status.ToString(), c.DocumentCount, c.ConceptCount, c.ConceptMapVersion, c.CreatedAt, ToJobRef(c.LatestJob))).ToList()))
            .Produces<IReadOnlyList<CollectionSummary>>();

        api.MapPost("/collections", async (CreateCollection body, ITenantContext t, GadflyDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var title = body.Title?.Trim() ?? "";
            if (title.Length == 0) return Error("title is required");
            if (title.Length > MaxTitleLength) return Error($"title must be at most {MaxTitleLength} characters");
            var c = new SourceCollection
            {
                OrganizationId = t.OrganizationId!.Value, Title = title, CreatedAt = clock.GetUtcNow(), Status = ProcessingStatus.Pending,
            };
            db.SourceCollections.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/collections/{c.Id}", new CollectionCreated(c.Id, c.Title));
        }).Produces<CollectionCreated>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest);

        api.MapGet("/collections/{id:guid}", async (Guid id, ITenantContext t, SourceLibraryService library, CancellationToken ct) =>
        {
            var org = t.OrganizationId!.Value;
            var c = await library.GetCollectionAsync(org, id, ct);
            if (c is null) return Results.NotFound();
            var documents = (await library.ListDocumentsAsync(org, id, ct)).Select(d => new CollectionDocument(d.FileName, d.SizeBytes, d.Uploaded,
                d.Ingested, d.ChangedSinceIngest, d.PassageCount, d.UploadedAt, d.IngestedAt, d.Error)).ToList();
            var job = await library.LatestJobAsync(org, id, ct);
            return Results.Ok(new CollectionDetail(c.Id, c.Title, c.Status.ToString(), c.ConceptMapVersion, c.CreatedAt, documents, ToJobRef(job)));
        }).Produces<CollectionDetail>().Produces(StatusCodes.Status404NotFound);

        // Validates every file before saving any; each is written to a temporary name in the folder
        // (a name processing never reads) and then moved over the final name.
        api.MapPost("/collections/{id:guid}/documents", async (Guid id, HttpRequest request, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            if (!await db.SourceCollections.AnyAsync(c => c.Id == id, ct)) return Results.NotFound();
            if (!request.HasFormContentType) return Error("send multipart/form-data with one or more files");
            IFormCollection form;
            try { form = await request.ReadFormAsync(ct); }
            catch (InvalidDataException ex) { return Error($"the upload could not be read: {ex.Message}"); }
            if (form.Files.Count == 0) return Error("send multipart/form-data with one or more files");

            var accepted = new List<(IFormFile File, string Name)>();
            foreach (var file in form.Files)
            {
                var (name, problem) = CheckFileName(file.FileName);
                if (problem is not null) return Error(problem);
                if (file.Length is 0 or > MaxUploadBytes) return Error($"{file.FileName}: files must be 1 byte to 20 MB");
                accepted.Add((file, name!));
            }

            var folder = SourceUploads.FolderFor(t.OrganizationId!.Value, id);
            Directory.CreateDirectory(folder);
            var saved = new List<string>();
            foreach (var (file, name) in accepted)
            {
                var temp = Path.Combine(folder, $".upload-{Guid.NewGuid():N}.part");
                try
                {
                    await using (var target = File.Create(temp))
                        await file.CopyToAsync(target, ct);
                    File.Move(temp, Path.Combine(folder, name), overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                saved.Add(name);
            }
            return Results.Ok(new UploadResult(saved));
        }).Produces<UploadResult>().Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        api.MapPost("/collections/{id:guid}/process", async (Guid id, ProcessRequest? body, ITenantContext t, GadflyDbContext db,
            SourceLibraryService library, JobQueue jobs, CancellationToken ct) =>
        {
            var org = t.OrganizationId!.Value;
            var c = await db.SourceCollections.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await library.LatestJobAsync(org, id, ct) is { IsActive: true } running)
                return Results.Json(new ApiError($"this collection is already being processed (job {running.Id}, {running.State})"),
                    statusCode: StatusCodes.Status409Conflict);
            var folder = SourceUploads.FolderFor(org, id);
            if (!HasReadableFiles(folder))
                return Error($"upload at least one document first ({string.Join(", ", SourceLibraryService.AcceptedExtensions)})");
            var jobId = await jobs.EnqueueAsync(SourceProcessingJob.JobKind,
                new SourceProcessingRequest(org, id, Guid.Empty, c.Title, folder, body?.RebuildConceptMap ?? false), org, ct: ct);
            return Results.Accepted($"/api/v1/jobs/{jobId}", new JobAccepted(jobId));
        }).Produces<JobAccepted>(StatusCodes.Status202Accepted).Produces<ApiError>(StatusCodes.Status400BadRequest)
          .Produces<ApiError>(StatusCodes.Status409Conflict).Produces(StatusCodes.Status404NotFound);

        api.MapGet("/jobs/{id:guid}", async (Guid id, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            var j = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == t.OrganizationId, ct);
            return j is null ? Results.NotFound()
                : Results.Ok(new JobStatus(j.Id, j.Kind, j.State.ToString(), j.Progress, j.ProgressMessage, j.Error, j.CompletedAt));
        }).Produces<JobStatus>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/collections/{id:guid}/concepts", async (Guid id, ITenantContext t, GadflyDbContext db, CancellationToken ct) =>
        {
            var map = await new SqlConceptMapStore(db, t.OrganizationId!.Value).LoadMapAsync(id.ToString(), ct);
            return map is null ? Results.NotFound() : Results.Ok(new ConceptMapView(map.Version,
                map.Concepts.Select(c => new ConceptView(c.Key, c.Title, c.Definition)).ToList(), map.Relations));
        }).Produces<ConceptMapView>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/exams", async (CreateExam body, ITenantContext t, GadflyDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var title = body.Title?.Trim() ?? "";
            if (title.Length == 0) return Error("title is required");
            if (title.Length > MaxTitleLength) return Error($"title must be at most {MaxTitleLength} characters");
            if (body.CollectionId == Guid.Empty) return Error("collectionId is required");
            if (body.Instructions?.Length > MaxInstructionsLength) return Error($"instructions must be at most {MaxInstructionsLength} characters");
            if (body.ConceptKeys is not null && body.ConceptKeys.Any(string.IsNullOrWhiteSpace)) return Error("conceptKeys must not contain blank keys");
            var mode = body.Mode?.Trim();
            if (!string.IsNullOrEmpty(mode) && !mode.Equals("Text", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Voice", StringComparison.OrdinalIgnoreCase))
                return Error("mode must be \"Text\" or \"Voice\"");

            var map = await new SqlConceptMapStore(db, t.OrganizationId!.Value).LoadMapAsync(body.CollectionId.ToString(), ct);
            if (map is null) return Error("the collection has no concept map yet; process it first");
            var keys = body.ConceptKeys is { Count: > 0 } k ? k.Select(x => x.Trim()).Distinct().ToList() : map.Concepts.Select(c => c.Key).ToList();
            var unknown = keys.Where(x => map.Find(x) is null).ToList();
            if (unknown.Count > 0) return Results.BadRequest(new ApiError("unknown concept keys", unknown));
            var policy = new SessionPolicy
            {
                ConceptIds = keys, TimeBudget = TimeSpan.FromMinutes(Math.Clamp(body.Minutes, 1, 240)),
                Difficulty = Math.Clamp(body.Difficulty, 1, 5), MaxTurns = Math.Clamp(body.MaxTurns, 1, 200),
            };
            try { policy.Validate(); }
            catch (ArgumentException ex) { return Error(ex.Message); }
            // The exam is owned by the person who created the API key, so assignments made from it name a real user.
            var keyId = Guid.TryParse(http.User.FindFirst(GadflyClaims.ApiKeyId)?.Value, out var kid) ? kid : Guid.Empty;
            var createdBy = await db.ApiKeys.AsNoTracking().Where(k => k.Id == keyId).Select(k => k.CreatedByUserId).FirstOrDefaultAsync(ct);
            var exam = new Exam
            {
                OrganizationId = t.OrganizationId!.Value, CollectionId = body.CollectionId, Title = title,
                Instructions = body.Instructions, PolicyJson = JsonSerializer.Serialize(policy, Camel),
                Mode = string.Equals(mode, "Voice", StringComparison.OrdinalIgnoreCase) ? ExamMode.Voice : ExamMode.Text,
                PromptSetId = ExamPrompts.SetId, PromptSetVersion = ExamPrompts.Version,
                Status = ExamStatus.Published, CreatedByUserId = createdBy, CreatedAt = clock.GetUtcNow(), PublishedAt = clock.GetUtcNow(),
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/exams/{exam.Id}", new ExamCreated(exam.Id, keys));
        }).Produces<ExamCreated>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest);

        // Assigns the exam to a student of this organization. A new user name is created as a Student with
        // a temporary password (returned once; changed at first sign-in). An existing account is never
        // pulled into the organization and an existing member's role is never changed: both answer 409.
        // Assigning again updates the window and attempts of the existing assignment.
        api.MapPost("/exams/{id:guid}/assignments", async (Guid id, AssignStudent body, ITenantContext t, GadflyDbContext db,
            IOrganizationDirectory directory, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Exams.AnyAsync(e => e.Id == id && e.Status == ExamStatus.Published, ct)) return Results.NotFound();
            var org = t.OrganizationId!.Value;
            var userName = body.UserName?.Trim() ?? "";
            if (userName.Length == 0) return Error("userName is required");
            if (userName.Length > MaxAccountTextLength) return Error($"userName must be at most {MaxAccountTextLength} characters");
            var email = body.Email?.Trim();
            if (!string.IsNullOrEmpty(email) && (email.Length > MaxAccountTextLength || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
                return Error($"email must be an email address of at most {MaxAccountTextLength} characters");
            if (body.AttemptsAllowed is < 1) return Error("attemptsAllowed must be at least 1");
            if (body.OpensAt is { } opens && body.ClosesAt is { } closes && closes <= opens) return Error("closesAt must be after opensAt");

            var normalized = MindAttic.Authentication.Services.IUserStore.Normalize(userName);
            var account = await db.AuthUsers.AsNoTracking().Where(u => u.NormalizedUserName == normalized).Select(u => new { u.Id }).FirstOrDefaultAsync(ct);
            Guid studentId;
            string? temporary = null;
            if (account is null)
            {
                temporary = TemporaryPassword();
                var member = await directory.AddMemberAsync(org, userName, body.Email, OrgRoles.Student, temporary, ct);
                if (!member.Ok || member.Id is null) return Error(member.Message);
                studentId = member.Id.Value;
            }
            else
            {
                var membership = await db.OrganizationMembers.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.OrganizationId == org && m.UserId == account.Id, ct);
                if (membership is null)
                    return Error($"the user name '{userName}' belongs to an existing Gadfly account that is not a member of your organization; " +
                        "use a distinct user name (accounts are shared across organizations, and membership is granted by your organization's admins)",
                        StatusCodes.Status409Conflict);
                if (membership.Role != OrgRoles.Student)
                    return Error($"'{userName}' has the {membership.Role} role in your organization; only students can be assigned exams, and the API never changes a member's role", StatusCodes.Status409Conflict);
                if (!membership.IsActive)
                    return Error($"'{userName}' is a deactivated member of your organization; an admin must reactivate them first", StatusCodes.Status409Conflict);
                studentId = account.Id;
            }

            var existing = await db.ExamAssignments.FirstOrDefaultAsync(a => a.ExamId == id && a.StudentUserId == studentId, ct);
            var created = existing is null;
            if (existing is null)
            {
                existing = new ExamAssignment
                {
                    OrganizationId = org, ExamId = id, StudentUserId = studentId, OpensAt = body.OpensAt, ClosesAt = body.ClosesAt,
                    AttemptsAllowed = body.AttemptsAllowed ?? 1, AssignedAt = clock.GetUtcNow(),
                };
                db.ExamAssignments.Add(existing);
            }
            else
            {
                existing.OpensAt = body.OpensAt;
                existing.ClosesAt = body.ClosesAt;
                if (body.AttemptsAllowed is { } attempts) existing.AttemptsAllowed = attempts;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new AssignmentResult(existing.Id, studentId, created, temporary));
        }).Produces<AssignmentResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
          .Produces<ApiError>(StatusCodes.Status409Conflict).Produces(StatusCodes.Status404NotFound);

        // A signed, expiring link (15 minutes) that brings the student to this assignment's start page
        // after signing in. One-click sign-in from the link needs M.AUTH trusted-launch support.
        api.MapPost("/assignments/{id:guid}/launch", async (Guid id, HttpRequest request, GadflyDbContext db, IDataProtectionProvider dp, CancellationToken ct) =>
        {
            var a = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var token = LaunchTokens.Protector(dp).Protect($"{a.Id:N}|{a.StudentUserId:N}", TimeSpan.FromMinutes(15));
            var url = $"{request.Scheme}://{request.Host}/launch?token={Uri.EscapeDataString(token)}";
            return Results.Ok(new LaunchLink(url, 900));
        }).Produces<LaunchLink>().Produces(StatusCodes.Status404NotFound);

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
            Results.Ok((await db.ExamSessions.AsNoTracking().Where(s => s.AssignmentId == id).OrderBy(s => s.Attempt)
                .Select(s => new { s.Id, s.Attempt, s.State, s.StartedAt, s.EndedAt, s.EndReason }).ToListAsync(ct))
                .Select(s => new AttemptView(s.Id, s.Attempt, s.State.ToString(), s.StartedAt, s.EndedAt, s.EndReason)).ToList()))
            .Produces<IReadOnlyList<AttemptView>>();

        api.MapPost("/webhooks", async (CreateWebhook body, ITenantContext t, WebhookService hooks, CancellationToken ct) =>
        {
            try
            {
                var (row, secret) = await hooks.CreateAsync(t.OrganizationId!.Value, body.Url ?? "", body.Events ?? [], ct);
                return Results.Created($"/api/v1/webhooks/{row.Id}", new WebhookCreated(row.Id, row.Url, WebhookService.EventList(row.Events), secret));
            }
            catch (ArgumentException ex)
            {
                return Error(ex.Message);
            }
        }).Produces<WebhookCreated>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest);

        api.MapGet("/webhooks", async (GadflyDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Webhooks.AsNoTracking().OrderBy(w => w.CreatedAt).ToListAsync(ct))
                .Select(w => new WebhookView(w.Id, w.Url, WebhookService.EventList(w.Events), w.IsActive, w.CreatedAt)).ToList()))
            .Produces<IReadOnlyList<WebhookView>>();

        api.MapDelete("/webhooks/{id:guid}", async (Guid id, GadflyDbContext db, CancellationToken ct) =>
            await db.Webhooks.Where(w => w.Id == id).ExecuteUpdateAsync(u => u.SetProperty(w => w.IsActive, false), ct) > 0
                ? Results.NoContent() : Results.NotFound())
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // The student-facing end of a launch link: verify, then go to the assignment (signing in first if needed).
        app.MapGet("/launch", async (string token, HttpContext http, IDataProtectionProvider dp, ITenantContext tenant, GadflyDbContext db) =>
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
            // A student in several organizations lands in the one this assignment belongs to.
            var org = await db.ExamAssignments.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Id == assignmentId && a.StudentUserId == studentId).Select(a => (Guid?)a.OrganizationId).FirstOrDefaultAsync(http.RequestAborted);
            if (org is { } o && tenant.Memberships.Any(m => m.OrganizationId == o) && tenant.OrganizationId != o)
                http.Response.Cookies.Append(ITenantContext.ActiveOrgCookie, o.ToString("N"), new CookieOptions
                {
                    HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(30),
                });
            return Results.LocalRedirect(target);
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Anonymous).ExcludeFromDescription();
    }

    /// <summary>
    /// Declares the bearer API-key scheme in the OpenAPI document's components (once) and requires it on
    /// every <c>/api/v1</c> operation.
    /// </summary>
    private static Task DeclareApiKeySecurity(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var document = context.Document;
        if (document is null) return Task.CompletedTask;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        if (!document.Components.SecuritySchemes.ContainsKey(SecuritySchemeId))
        {
            document.Components.SecuritySchemes[SecuritySchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "gfk_<8 hex>_<secret>",
                Description = "The organization's API key: Authorization: Bearer gfk_...",
            };
        }
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeId, document)] = [] });
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing, unknown or revoked API key, or a deactivated organization." });
        return Task.CompletedTask;
    }

    private static JobRef? ToJobRef(SourceJobStatus? j) =>
        j is null ? null : new(j.Id, j.State.ToString(), j.Progress, j.Message, j.Error, j.CreatedAt, j.CompletedAt);

    private static bool HasReadableFiles(string folder) =>
        Directory.Exists(folder) && new DirectoryInfo(folder).EnumerateFiles()
            .Any(f => f.Length > 0 && SourceLibraryService.AcceptedExtensions.Contains(f.Extension.ToLowerInvariant()));

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The cleaned file name to store an upload under, or the reason it is refused: only readable
    /// extensions, no reserved device names (whatever the extension), no path parts.
    /// </summary>
    private static (string? Name, string? Problem) CheckFileName(string? original)
    {
        var raw = Path.GetFileName((original ?? "").Replace('\\', '/').Split('/').Last());
        var clean = new string(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        var ext = Path.GetExtension(clean).ToLowerInvariant();
        if (!SourceLibraryService.AcceptedExtensions.Contains(ext))
            return (null, $"{original}: only {string.Join(", ", SourceLibraryService.AcceptedExtensions)} files are accepted");
        var device = clean.Split('.')[0].TrimEnd(' ');
        if (ReservedDeviceNames.Contains(device))
            return (null, $"{original}: {device.ToUpperInvariant()} is a reserved device name; rename the file");
        if (Path.GetFileNameWithoutExtension(clean).Trim().Length == 0)
            return (null, $"{original}: the file needs a name");
        if (clean.Length > 150) clean = clean[^150..];
        return (clean, null);
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
