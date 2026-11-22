using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>What the audit log viewer filters by. Everything is optional; dates are UTC, <see cref="To"/> exclusive.</summary>
public sealed record AuditFilter
{
    public Guid? ActorUserId { get; init; }
    /// <summary>An exact action ("assessment.published"), or a family ending in a dot ("grade.").</summary>
    public string? Action { get; init; }
    /// <summary>Events by, about, or in a session of this student.</summary>
    public Guid? StudentUserId { get; init; }
    /// <summary>Events about this exam: the exam itself, its objectives and grading policy, its assignments and its sessions.</summary>
    public Guid? ExamId { get; init; }
    public Guid? SessionId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    /// <summary>Words to find in the action, the record, the reason or the before/after detail, or an actor's name.</summary>
    public string? Search { get; init; }

    public bool IsEmpty => ActorUserId is null && string.IsNullOrWhiteSpace(Action) && StudentUserId is null && ExamId is null && SessionId is null
                           && From is null && To is null && string.IsNullOrWhiteSpace(Search);
}

/// <summary>Whose log: one organization's, or (platform staff only) the platform-level events that belong to none.</summary>
public sealed record AuditTarget(Guid? OrganizationId)
{
    public bool IsPlatform => OrganizationId is null;
}

/// <summary>
/// Why an event's detail is gone. Audit rows are append-only, but the privacy jobs remove a student's words from them: after the
/// retention period (answers and clarifications) and on an erasure (everything the event carried about that student).
/// Who did what, and when, always stays.
/// </summary>
public enum AuditRedaction { None, RetentionPeriod, StudentErased }

public sealed record AuditRow(long Id, DateTimeOffset At, Guid? ActorUserId, string ActorName, string? ActorRole, string Action, string EntityType,
    string? EntityId, Guid? SessionId, string? Reason, bool HasDetail, AuditRedaction Redaction)
{
    public bool IsRedacted => Redaction != AuditRedaction.None;
}

public sealed record AuditPage(IReadOnlyList<AuditRow> Rows, int Total, int Page, int PageSize)
{
    public int Pages => Total == 0 ? 1 : (Total + PageSize - 1) / PageSize;
}

/// <summary>One event in full: its row, and its before/after JSON pretty-printed (null when it carried none or it was redacted).</summary>
public sealed record AuditDetail(AuditRow Row, string? Before, string? After, string? RedactionNote);

public sealed record AuditOption(Guid Id, string Label);

/// <summary>The choices the filters offer: who acted, which actions occur, students and exams, and (platform staff) organizations.</summary>
public sealed record AuditChoices(IReadOnlyList<AuditOption> People, IReadOnlyList<string> Actions, IReadOnlyList<AuditOption> Students,
    IReadOnlyList<AuditOption> Exams, IReadOnlyList<AuditOption> Organizations);

/// <summary>
/// The audit log viewer (GAD-T301): filters, full-text search and paging done in SQL over the indexed columns (organization and
/// time first, then actor, action, record and session), a detail view, and streamed CSV and JSON exports. Owners and admins see
/// their own organization's log (the ViewAuditLog permission); MindAttic platform staff may open any organization's, or the
/// platform-level events. Rows a privacy job redacted are shown as redacted. The caller's own scope supplies
/// <paramref name="db"/>: tenant-filtered for an organization's own staff, a system scope only when platform staff look at
/// another organization (every query here also filters by organization explicitly, so the result is the same either way).
/// </summary>
public sealed class AuditQueryService(GadflyDbContext db, TenantSnapshot caller, TimeProvider clock)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    /// <summary>Most rows one export streams; a bigger log is exported a date range at a time.</summary>
    public const int MaxExportRows = 250_000;
    public const int MaxSearchLength = 200;

    /// <summary>The text the retention job puts in place of what it removes.</summary>
    public const string RetentionPlaceholder = "[redacted after the retention period]";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>True when the caller may read <paramref name="target"/>'s log.</summary>
    public static bool MayRead(TenantSnapshot caller, AuditTarget target) =>
        caller.IsSuperAdmin
        || (!target.IsPlatform && caller.OrganizationId == target.OrganizationId && caller.UserId is not null
            && PermissionMatrix.Has(caller.Role, Permission.ViewAuditLog));

    /// <summary>True when reading <paramref name="target"/> needs a system scope (platform staff outside their own organization).</summary>
    public static bool NeedsSystemScope(TenantSnapshot caller, AuditTarget target) =>
        caller.IsSuperAdmin && (target.IsPlatform || target.OrganizationId != caller.OrganizationId);

    private void Require(AuditTarget target)
    {
        if (!MayRead(caller, target)) throw new UnauthorizedAccessException("The audit log is for the organization's owners and admins.");
    }

    // ================================================================== querying

    /// <summary>The query for a target and filter, newest first. Public for tests and the export.</summary>
    public IQueryable<AuditEvent> Query(AuditTarget target, AuditFilter f)
    {
        var org = target.OrganizationId;
        var q = org is { } o ? db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == o) : db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == null);
        if (f.From is { } from) q = q.Where(a => a.At >= from);
        if (f.To is { } to) q = q.Where(a => a.At < to);
        if (f.ActorUserId is { } actor) q = q.Where(a => a.ActorUserId == actor);
        if (!string.IsNullOrWhiteSpace(f.Action))
        {
            var action = f.Action.Trim();
            q = action.EndsWith('.') ? q.Where(a => a.Action.StartsWith(action)) : q.Where(a => a.Action == action);
        }
        if (f.SessionId is { } sid)
        {
            var text = sid.ToString();
            q = q.Where(a => a.SessionId == sid || a.EntityId == text);
        }
        if (org is { } scoped && f.StudentUserId is { } student)
        {
            var text = student.ToString();
            var sessions = db.ExamSessions.Where(s => s.OrganizationId == scoped && s.StudentUserId == student).Select(s => (Guid?)s.Id);
            var assignments = db.ExamAssignments.Where(x => x.OrganizationId == scoped && x.StudentUserId == student).Select(x => x.Id.ToString());
            q = q.Where(a => a.ActorUserId == student || a.EntityId == text || sessions.Contains(a.SessionId) || assignments.Contains(a.EntityId!));
        }
        if (org is { } scopedExam && f.ExamId is { } exam)
        {
            var text = exam.ToString();
            var sessions = db.ExamSessions.Where(s => s.OrganizationId == scopedExam && s.ExamId == exam).Select(s => (Guid?)s.Id);
            var assignments = db.ExamAssignments.Where(x => x.OrganizationId == scopedExam && x.ExamId == exam).Select(x => x.Id.ToString());
            var objectives = db.LearningObjectives.Where(x => x.OrganizationId == scopedExam && x.ExamId == exam).Select(x => x.Id.ToString());
            q = q.Where(a => a.EntityId == text || sessions.Contains(a.SessionId) || assignments.Contains(a.EntityId!) || objectives.Contains(a.EntityId!));
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim();
            if (term.Length > MaxSearchLength) term = term[..MaxSearchLength];
            var named = db.AuthUsers.Where(u => u.UserName.Contains(term)).Select(u => (Guid?)u.Id);
            q = q.Where(a => a.Action.Contains(term) || a.EntityType.Contains(term) || (a.EntityId != null && a.EntityId.Contains(term))
                             || (a.Reason != null && a.Reason.Contains(term)) || (a.ActorRole != null && a.ActorRole.Contains(term))
                             || (a.BeforeJson != null && a.BeforeJson.Contains(term)) || (a.AfterJson != null && a.AfterJson.Contains(term))
                             || named.Contains(a.ActorUserId));
        }
        return q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id);
    }

    /// <summary>One page of events, newest first, with the total that match.</summary>
    public async Task<AuditPage> PageAsync(AuditTarget target, AuditFilter filter, int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        Require(target);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(1, page);
        var q = Query(target, filter);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new RawRow(a.Id, a.At, a.ActorUserId, a.ActorRole, a.Action, a.EntityType, a.EntityId, a.SessionId, a.Reason,
                a.BeforeJson != null || a.AfterJson != null))
            .ToListAsync(ct);
        return new AuditPage(await ShapeAsync(target, rows, ct), total, page, pageSize);
    }

    /// <summary>One event in full, or null when it isn't in this target's log.</summary>
    public async Task<AuditDetail?> DetailAsync(AuditTarget target, long id, CancellationToken ct = default)
    {
        Require(target);
        var org = target.OrganizationId;
        var a = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == org, ct);
        if (a is null) return null;
        var row = (await ShapeAsync(target, [new RawRow(a.Id, a.At, a.ActorUserId, a.ActorRole, a.Action, a.EntityType, a.EntityId, a.SessionId, a.Reason,
            a.BeforeJson != null || a.AfterJson != null)], ct))[0];
        var note = row.Redaction switch
        {
            AuditRedaction.RetentionPeriod => "The student's words were removed from this event when the organization's retention period ended. Who did what, and when, is kept.",
            AuditRedaction.StudentErased => "This event's detail was removed: the student's data was erased at their request, or the attempt it belongs to was removed. Who did what, and when, is kept.",
            _ => null,
        };
        return new AuditDetail(row, PrettyJson(a.BeforeJson), PrettyJson(a.AfterJson), note);
    }

    /// <summary>The choices the filter controls offer.</summary>
    public async Task<AuditChoices> ChoicesAsync(AuditTarget target, CancellationToken ct = default)
    {
        Require(target);
        IReadOnlyList<AuditOption> orgs = [];
        if (caller.IsSuperAdmin)
            orgs = await db.Organizations.AsNoTracking().OrderBy(o => o.Name).Select(o => new AuditOption(o.Id, o.Name)).ToListAsync(ct);
        if (target.OrganizationId is not { } org)
        {
            var platformActions = await db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == null).Select(a => a.Action).Distinct().OrderBy(a => a).Take(500).ToListAsync(ct);
            return new AuditChoices([], platformActions, [], [], orgs);
        }
        var members = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == org)
            .Join(db.AuthUsers.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.UserName, m.Role, m.IsActive })
            .ToListAsync(ct);
        var people = members.Where(m => m.Role != OrgRoles.Student).GroupBy(m => m.Id)
            .Select(g => new AuditOption(g.Key, $"{g.First().UserName} ({string.Join(", ", g.Select(m => m.Role).Distinct())}){(g.Any(m => m.IsActive) ? "" : ", former")}"))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var students = members.Where(m => m.Role == OrgRoles.Student).GroupBy(m => m.Id)
            .Select(g => new AuditOption(g.Key, g.First().UserName + (g.Any(m => m.IsActive) ? "" : " (former)")))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var actions = await db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == org).Select(a => a.Action).Distinct().OrderBy(a => a).Take(500).ToListAsync(ct);
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org).OrderBy(e => e.Title).Select(e => new AuditOption(e.Id, e.Title)).ToListAsync(ct);
        return new AuditChoices(people, actions, students, exams, orgs);
    }

    // ================================================================== export

    /// <summary>
    /// Streams every matching event (newest first, at most <see cref="MaxExportRows"/>) as CSV or JSON to <paramref name="output"/>,
    /// a page of rows at a time so a large log never sits in memory. Audited as audit.exported, with the filter used.
    /// </summary>
    public async Task ExportAsync(AuditTarget target, AuditFilter filter, bool json, Stream output, CancellationToken ct = default)
    {
        Require(target);
        await RecordAsync(target, "audit.exported", new { Format = json ? "json" : "csv", Filter = filter }, ct);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: !json), bufferSize: 16 * 1024, leaveOpen: true);
        if (json) await writer.WriteAsync("[\n");
        else await writer.WriteLineAsync("id,at_utc,actor,actor_user_id,actor_role,action,record_type,record_id,session_id,reason,redacted,before_json,after_json");
        var first = true;
        await foreach (var batch in BatchesAsync(target, filter, ct))
        {
            foreach (var (row, before, after) in batch)
            {
                if (json)
                {
                    if (!first) await writer.WriteAsync(",\n");
                    await writer.WriteAsync(JsonSerializer.Serialize(new
                    {
                        row.Id, row.At, Actor = row.ActorName, row.ActorUserId, row.ActorRole, row.Action, RecordType = row.EntityType, RecordId = row.EntityId,
                        row.SessionId, row.Reason, Redacted = row.IsRedacted ? row.Redaction.ToString() : null,
                        Before = ParseOrNull(before), After = ParseOrNull(after),
                    }, ExportJson));
                }
                else
                {
                    await writer.WriteLineAsync(string.Join(',',
                        row.Id.ToString(CultureInfo.InvariantCulture), row.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Csv(row.ActorName), row.ActorUserId?.ToString() ?? "", Csv(row.ActorRole), Csv(row.Action), Csv(row.EntityType), Csv(row.EntityId),
                        row.SessionId?.ToString() ?? "", Csv(row.Reason), row.IsRedacted ? row.Redaction.ToString() : "", Csv(before), Csv(after)));
                }
                first = false;
            }
            await writer.FlushAsync(ct);
        }
        if (json) await writer.WriteAsync("\n]\n");
        await writer.FlushAsync(ct);
    }

    private static readonly JsonSerializerOptions ExportJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A file name for an export: the organization's slug-like stem and today's date.</summary>
    public static string ExportFileName(bool json, DateTimeOffset now) => $"gadfly-audit-{now.UtcDateTime:yyyyMMdd-HHmm}.{(json ? "json" : "csv")}";

    private async IAsyncEnumerable<IReadOnlyList<(AuditRow Row, string? Before, string? After)>> BatchesAsync(AuditTarget target, AuditFilter filter,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int batch = 1000;
        var q = Query(target, filter);
        long? lastId = null;
        DateTimeOffset? lastAt = null;
        var sent = 0;
        while (sent < MaxExportRows)
        {
            // Keyset paging (At, Id) descending: stable while new events arrive, and each batch is one indexed seek.
            var page = q;
            if (lastAt is { } at && lastId is { } id) page = page.Where(a => a.At < at || (a.At == at && a.Id < id));
            var raw = await page.Take(Math.Min(batch, MaxExportRows - sent))
                .Select(a => new { a.Id, a.At, a.ActorUserId, a.ActorRole, a.Action, a.EntityType, a.EntityId, a.SessionId, a.Reason, a.BeforeJson, a.AfterJson })
                .ToListAsync(ct);
            if (raw.Count == 0) yield break;
            var shaped = await ShapeAsync(target, raw.Select(a => new RawRow(a.Id, a.At, a.ActorUserId, a.ActorRole, a.Action, a.EntityType, a.EntityId,
                a.SessionId, a.Reason, a.BeforeJson != null || a.AfterJson != null)).ToList(), ct);
            yield return shaped.Select((r, i) => (r, raw[i].BeforeJson, raw[i].AfterJson)).ToList();
            sent += raw.Count;
            lastAt = raw[^1].At;
            lastId = raw[^1].Id;
            if (raw.Count < batch) yield break;
        }
    }

    // ================================================================== platform access

    /// <summary>Platform staff opening another organization's log (or the platform events) leaves a trace in that log.</summary>
    public Task RecordPlatformViewAsync(AuditTarget target, CancellationToken ct = default) =>
        NeedsSystemScope(caller, target) ? RecordAsync(target, "audit.viewed.platform", new { Organization = target.OrganizationId }, ct) : Task.CompletedTask;

    private async Task RecordAsync(AuditTarget target, string action, object after, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = target.OrganizationId,
            ActorUserId = caller.UserId,
            ActorRole = caller.IsSuperAdmin && target.OrganizationId != caller.OrganizationId ? "PlatformAdmin" : caller.Role,
            Action = action,
            EntityType = "AuditLog",
            EntityId = target.OrganizationId?.ToString(),
            AfterJson = JsonSerializer.Serialize(after, ExportJson),
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ================================================================== helpers

    private sealed record RawRow(long Id, DateTimeOffset At, Guid? ActorUserId, string? ActorRole, string Action, string EntityType, string? EntityId,
        Guid? SessionId, string? Reason, bool HasJson);

    /// <summary>Names the actors and works out which rows a privacy job redacted (two small lookups per page).</summary>
    private async Task<IReadOnlyList<AuditRow>> ShapeAsync(AuditTarget target, IReadOnlyList<RawRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var actorIds = rows.Where(r => r.ActorUserId is not null).Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var names = actorIds.Count == 0 ? [] : await db.AuthUsers.AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        var sessionIds = rows.Where(r => r.SessionId is not null).Select(r => r.SessionId!.Value).Distinct().ToList();
        var sessions = sessionIds.Count == 0 || target.OrganizationId is not { } org ? []
            : await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && sessionIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.RedactedAt, ct);
        return rows.Select(r => new AuditRow(r.Id, r.At, r.ActorUserId,
            r.ActorUserId is { } a ? names.GetValueOrDefault(a, "(removed account)") : "System", r.ActorRole, r.Action, r.EntityType, r.EntityId,
            r.SessionId, r.Reason, r.HasJson, target.IsPlatform ? AuditRedaction.None : RedactionOf(r.Action, r.SessionId, r.HasJson, r.Reason, sessions))).ToList();
    }

    /// <summary>
    /// Whether a privacy job removed this event's detail: its session was erased (the session row is gone, so its events lost
    /// their detail), or the retention job removed the words from an answer or clarification event of a redacted session.
    /// </summary>
    public static AuditRedaction RedactionOf(string action, Guid? sessionId, bool hasJson, string? reason, IReadOnlyDictionary<Guid, DateTimeOffset?> sessions)
    {
        if (reason == RetentionPlaceholder) return AuditRedaction.StudentErased;
        if (sessionId is not { } sid || hasJson || !CarriesStudentDetail(action)) return AuditRedaction.None;
        if (!sessions.TryGetValue(sid, out var redactedAt)) return AuditRedaction.StudentErased;
        if (redactedAt is not null && (action.StartsWith("response.", StringComparison.Ordinal) || action.StartsWith("clarification.", StringComparison.Ordinal)
                                       || action == "desk.scores-archived"))
            return AuditRedaction.RetentionPeriod;
        return AuditRedaction.None;
    }

    /// <summary>Event families that are always written with before/after detail about a student's attempt: an empty one was redacted.</summary>
    private static bool CarriesStudentDetail(string action) =>
        action.StartsWith("response.", StringComparison.Ordinal) || action.StartsWith("clarification.", StringComparison.Ordinal)
        || action.StartsWith("grade.", StringComparison.Ordinal) || action == "desk.scores-archived";

    private static string? PrettyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Pretty);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonElement? ParseOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(json)).RootElement.Clone();
        }
    }

    /// <summary>A CSV field: quoted when needed, and never starting with a character a spreadsheet would run as a formula.</summary>
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
