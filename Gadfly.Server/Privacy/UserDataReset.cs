using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Notifications;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gadfly.Server.Privacy;

/// <summary>
/// Erases every user's exam activity across the platform (GAD-T789): every attempt and everything that hangs off one
/// (turns, transcripts, responses, judgments, grades, reports, integrity events, appeals, inbox messages, usage and call
/// timings), the job queue apart from source processing, webhook deliveries, emails still queued or about a result or appeal,
/// released-result marks, LTI grade passback state and voice calibrations. Kept: accounts with their passwords, MFA and
/// emails, organizations, memberships, courses, exams and the catalog, rubrics, assignments, settings, API keys and the audit
/// log, which gains one <see cref="ResetAction"/> entry holding the row counts. The platform stores no recordings or captures
/// in blob storage (the one container is the Data Protection key ring), so there is nothing to remove outside the database.
/// <para>
/// The session tables are read from the EF model, not listed by hand: <see cref="ExamSession"/>, every entity with a foreign key
/// to one already in the set, and every Gadfly entity with a <c>SessionId</c> column, the audit log excepted. They are deleted
/// children first, in one transaction. No model is called and no email is sent.
/// </para>
/// <para>
/// Run: <c>Gadfly.Server --reset-user-data --dry-run --then-exit</c> prints what would go, table by table, and changes nothing.
/// Without <c>--dry-run</c> it erases, and only with <c>Gadfly:Admin:AllowUserDataReset=true</c> set for that one run.
/// </para>
/// </summary>
public static class UserDataReset
{
    public const string ResetArgument = "--reset-user-data";
    public const string DryRunArgument = "--dry-run";
    public const string AllowSetting = "Gadfly:Admin:AllowUserDataReset";
    public const string ResetAction = "privacy.user-data-reset";

    private const string SessionIdName = "SessionId";

    /// <summary>Kept although it names a session: the audit log is append-only (GAD-T84).</summary>
    private static readonly Type[] Kept = [typeof(AuditEvent)];

    /// <summary>One table's part of the reset: <see cref="Action"/> is delete, update (the rows stay, their activity goes) or keep.</summary>
    public sealed record Line(string Table, string Action, int Rows);

    /// <summary>A session table and, when only some of its rows hang off a session, the SQL condition that picks them.</summary>
    public sealed record SessionTable(IEntityType Entity, string? Filter)
    {
        public string Name => $"{Entity.GetSchema()}.{Entity.GetTableName()}";
        public string From => $"[{Entity.GetSchema()}].[{Entity.GetTableName()}]" + (Filter is null ? "" : $" WHERE {Filter}");
    }

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Reset");
        var dryRun = args.Contains(DryRunArgument);
        if (!dryRun && app.Configuration[AllowSetting] != "true")
        {
            log.LogError("{Arg} erases only with {Setting}=true (or run it with {DryRun}); nothing was changed.", ResetArgument, AllowSetting, DryRunArgument);
            return;
        }
        using var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var lines = dryRun ? await CountAsync(db, CancellationToken.None) : await ResetAsync(db, clock, CancellationToken.None);
        Console.WriteLine(dryRun ? "User data reset, dry run (nothing changed):" : "User data reset, done:");
        foreach (var l in lines) Console.WriteLine($"  {l.Action,-7} {l.Table,-40} {l.Rows,8}");
        log.LogWarning("{Arg}{DryRun}: {Rows} row(s) {Verb} across {Tables} table(s).", ResetArgument, dryRun ? " " + DryRunArgument : "",
            lines.Where(l => l.Action != "keep").Sum(l => l.Rows), dryRun ? "would change" : "changed", lines.Count(l => l.Action != "keep"));
    }

    /// <summary>The tables of exam activity, read from the model, in an order that deletes every child before its parent.</summary>
    public static IReadOnlyList<SessionTable> SessionTables(IModel model)
    {
        var types = model.GetEntityTypes().Where(t => !t.IsOwned() && t.GetTableName() is not null && !Kept.Contains(t.ClrType)).ToList();
        var erased = new HashSet<IEntityType> { model.FindEntityType(typeof(ExamSession))! };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var t in types.Where(t => !erased.Contains(t) && Links(t, erased).Count > 0).ToList())
                grew |= erased.Add(t);
        }
        var ordered = new List<IEntityType>();
        var left = erased.ToHashSet();
        while (left.Count > 0)
        {
            var leaves = left.Where(p => !left.Any(d => d != p && d.GetForeignKeys().Any(fk => fk.PrincipalEntityType == p)))
                .OrderBy(t => t.GetTableName(), StringComparer.Ordinal).ToList();
            if (leaves.Count == 0) throw new InvalidOperationException("The session tables reference each other in a cycle.");
            ordered.AddRange(leaves);
            left.ExceptWith(leaves);
        }
        return ordered.Select(t => new SessionTable(t, Filter(t, erased))).ToList();
    }

    /// <summary>The columns tying a row of <paramref name="t"/> to a session-side row: foreign keys into the set, and a Gadfly SessionId.</summary>
    private static List<IProperty> Links(IEntityType t, HashSet<IEntityType> erased)
    {
        var links = t.GetForeignKeys().Where(fk => fk.PrincipalEntityType != t && erased.Contains(fk.PrincipalEntityType))
            .SelectMany(fk => fk.Properties).ToList();
        if (t.GetSchema() == GadflyDbContext.Schema && t.FindProperty(SessionIdName) is { } sessionId) links.Add(sessionId);
        return links.Distinct().ToList();
    }

    /// <summary>Null when every row goes (a required link, or the session table itself); else the rows where some link is set.</summary>
    private static string? Filter(IEntityType t, HashSet<IEntityType> erased)
    {
        var links = Links(t, erased);
        if (links.Count == 0 || links.Any(p => !p.IsNullable)) return null;
        var table = StoreObjectIdentifier.Table(t.GetTableName()!, t.GetSchema());
        return string.Join(" OR ", links.Select(p => $"[{p.GetColumnName(table)}] IS NOT NULL"));
    }

    // ------------------------------------------------------------------ activity the model does not tie to a session

    private static readonly string[] SessionEmailKinds = [EmailKinds.ResultReleased, EmailKinds.AppealReceived, EmailKinds.AppealDecided, EmailKinds.AttemptReset];

    private sealed record Extra(Type Entity, string Action, Func<GadflyDbContext, CancellationToken, Task<int>> Count, Func<GadflyDbContext, CancellationToken, Task<int>> Apply);

    // Jobs: the grading, email, webhook and sweep queue; source processing belongs to the exams' material and stays.
    private static IQueryable<Job> ActivityJobs(GadflyDbContext db) => db.Jobs.Where(j => j.Kind != SourceProcessingJob.JobKind);
    // Emails: nothing still waiting goes out, and the log of result and appeal emails goes; the sent exam-assigned and reminder
    // rows stay, since they are what keeps a reminder for a still-open assignment from being sent twice.
    private static IQueryable<EmailLog> ActivityEmails(GadflyDbContext db) =>
        db.EmailLogs.Where(e => e.Status == EmailLogStatus.Queued || SessionEmailKinds.Contains(e.Kind));
    private static IQueryable<ExamAssignment> Released(GadflyDbContext db) => db.ExamAssignments.Where(a => a.ResultsReleasedAt != null);
    private static IQueryable<LtiAssignmentLink> Passback(GadflyDbContext db) => db.LtiAssignmentLinks.Where(l => l.PassbackSentAt != null
        || l.LastPassbackPercent != null || l.PassbackPendingPercent != null || l.PassbackAttempts != 0 || l.PassbackRetryAt != null
        || l.PassbackError != null || l.PassbackFailedAt != null);
    private static IQueryable<StudentLanguageProfile> Calibrated(GadflyDbContext db) =>
        db.StudentLanguageProfiles.Where(p => p.VoiceCalibrationJson != null || p.VoiceCalibratedAt != null);

    private static readonly Extra[] Extras =
    [
        new(typeof(Job), "delete", (db, ct) => ActivityJobs(db).CountAsync(ct), (db, ct) => ActivityJobs(db).ExecuteDeleteAsync(ct)),
        new(typeof(WebhookDelivery), "delete", (db, ct) => db.WebhookDeliveries.CountAsync(ct), (db, ct) => db.WebhookDeliveries.ExecuteDeleteAsync(ct)),
        new(typeof(EmailLog), "delete", (db, ct) => ActivityEmails(db).CountAsync(ct), (db, ct) => ActivityEmails(db).ExecuteDeleteAsync(ct)),
        new(typeof(ExamAssignment), "update", (db, ct) => Released(db).CountAsync(ct),
            (db, ct) => Released(db).ExecuteUpdateAsync(u => u.SetProperty(a => a.ResultsReleasedAt, (DateTimeOffset?)null), ct)),
        new(typeof(LtiAssignmentLink), "update", (db, ct) => Passback(db).CountAsync(ct),
            (db, ct) => Passback(db).ExecuteUpdateAsync(u => u
                .SetProperty(l => l.PassbackSentAt, (DateTimeOffset?)null).SetProperty(l => l.LastPassbackPercent, (decimal?)null)
                .SetProperty(l => l.PassbackPendingPercent, (decimal?)null).SetProperty(l => l.PassbackAttempts, 0)
                .SetProperty(l => l.PassbackRetryAt, (DateTimeOffset?)null).SetProperty(l => l.PassbackError, (string?)null)
                .SetProperty(l => l.PassbackFailedAt, (DateTimeOffset?)null), ct)),
        new(typeof(StudentLanguageProfile), "update", (db, ct) => Calibrated(db).CountAsync(ct),
            (db, ct) => Calibrated(db).ExecuteUpdateAsync(u => u
                .SetProperty(p => p.VoiceCalibrationJson, (string?)null).SetProperty(p => p.VoiceCalibratedAt, (DateTimeOffset?)null), ct)),
    ];

    /// <summary>What stays, counted alongside so a run shows the accounts and exams are still there.</summary>
    private static readonly (Type Entity, Func<GadflyDbContext, CancellationToken, Task<int>> Count)[] KeptCounts =
    [
        (typeof(MindAttic.Authentication.Entities.AuthUser), (db, ct) => db.AuthUsers.CountAsync(ct)),
        (typeof(MindAttic.Authentication.Entities.AuthOrganization), (db, ct) => db.Organizations.CountAsync(ct)),
        (typeof(MindAttic.Authentication.Entities.AuthMembership), (db, ct) => db.OrganizationMembers.CountAsync(ct)),
        (typeof(Course), (db, ct) => db.Courses.CountAsync(ct)),
        (typeof(Exam), (db, ct) => db.Exams.CountAsync(ct)),
        (typeof(ExamAssignment), (db, ct) => db.ExamAssignments.CountAsync(ct)),
        (typeof(AuditEvent), (db, ct) => db.AuditEvents.CountAsync(ct)),
    ];

    private static string Name(GadflyDbContext db, Type entity)
    {
        var t = db.Model.FindEntityType(entity)!;
        return $"{t.GetSchema()}.{t.GetTableName()}";
    }

#pragma warning disable EF1002   // the table and column names come from the EF model, never from input
    private static Task<int> SqlCountAsync(GadflyDbContext db, SessionTable t, CancellationToken ct) =>
        db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {t.From}").SingleAsync(ct);
#pragma warning restore EF1002

    /// <summary>The dry run: what <see cref="ResetAsync"/> would change, table by table, then the kept tables' counts.</summary>
    public static async Task<IReadOnlyList<Line>> CountAsync(GadflyDbContext db, CancellationToken ct)
    {
        var lines = new List<Line>();
        foreach (var e in Extras) lines.Add(new(Name(db, e.Entity), e.Action, await e.Count(db, ct)));
        foreach (var t in SessionTables(db.Model)) lines.Add(new(t.Name, "delete", await SqlCountAsync(db, t, ct)));
        foreach (var (entity, count) in KeptCounts) lines.Add(new(Name(db, entity), "keep", await count(db, ct)));
        return lines;
    }

    /// <summary>Erases it all in one transaction and records the reset in the audit log. Returns the rows changed per table.</summary>
    public static async Task<IReadOnlyList<Line>> ResetAsync(GadflyDbContext db, TimeProvider clock, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var lines = new List<Line>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            lines.Clear();
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var e in Extras) lines.Add(new(Name(db, e.Entity), e.Action, await e.Apply(db, ct)));
            foreach (var t in SessionTables(db.Model))
#pragma warning disable EF1002   // the table and column names come from the EF model, never from input
                lines.Add(new(t.Name, "delete", await db.Database.ExecuteSqlRawAsync($"DELETE FROM {t.From}", ct)));
#pragma warning restore EF1002
            db.AuditEvents.Add(new AuditEvent
            {
                ActorRole = "system",
                Action = ResetAction,
                EntityType = "Platform",
                AfterJson = JsonSerializer.Serialize(lines.Where(l => l.Rows > 0).ToDictionary(l => l.Table, l => l.Rows)),
                Reason = $"Every user's exam activity erased with {ResetArgument} (GAD-T789); accounts, organizations, courses and exams kept.",
                At = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        foreach (var (entity, count) in KeptCounts) lines.Add(new(Name(db, entity), "keep", await count(db, ct)));
        return lines;
    }

    // ------------------------------------------------------------------ one student's attempts at some exams (GAD-T790)

    /// <summary>
    /// The SQL condition, on rows of <paramref name="t"/> aliased <paramref name="alias"/>, that picks the rows hanging off the
    /// attempts in <paramref name="ids"/> (a list of quoted GUIDs): the attempts themselves, rows with one of them as their
    /// SessionId, and rows whose parent in the session tables is picked, followed up the same foreign keys <see cref="SessionTables"/> uses.
    /// </summary>
    private static string Scope(IEntityType t, string alias, HashSet<IEntityType> erased, string ids, ref int aliases)
    {
        var table = StoreObjectIdentifier.Table(t.GetTableName()!, t.GetSchema());
        if (t.ClrType == typeof(ExamSession)) return $"{alias}.[{t.FindPrimaryKey()!.Properties.Single().GetColumnName(table)}] IN ({ids})";
        var parts = new List<string>();
        foreach (var fk in t.GetForeignKeys().Where(fk => fk.PrincipalEntityType != t && erased.Contains(fk.PrincipalEntityType)))
        {
            var p = fk.PrincipalEntityType;
            var parent = StoreObjectIdentifier.Table(p.GetTableName()!, p.GetSchema());
            var a = $"p{++aliases}";
            var on = string.Join(" AND ", fk.Properties.Zip(fk.PrincipalKey.Properties,
                (c, k) => $"{a}.[{k.GetColumnName(parent)}] = {alias}.[{c.GetColumnName(table)}]"));
            parts.Add($"EXISTS (SELECT 1 FROM [{p.GetSchema()}].[{p.GetTableName()}] AS {a} WHERE {on} AND {Scope(p, a, erased, ids, ref aliases)})");
        }
        if (t.GetSchema() == GadflyDbContext.Schema && t.FindProperty(SessionIdName) is { } sessionId)
            parts.Add($"{alias}.[{sessionId.GetColumnName(table)}] IN ({ids})");
        return parts.Count == 0 ? "1 = 0" : "(" + string.Join(" OR ", parts) + ")";
    }

    /// <summary>The session tables, children first, each with the statement body (after DELETE/SELECT) scoped to the attempts.</summary>
    public static IReadOnlyList<(SessionTable Table, string From)> ScopedSessionTables(IModel model, IReadOnlyCollection<Guid> sessionIds)
    {
        if (sessionIds.Count == 0) return [];
        var tables = SessionTables(model);
        var erased = tables.Select(t => t.Entity).ToHashSet();
        var ids = string.Join(", ", sessionIds.Distinct().Select(id => $"'{id:D}'"));
        return tables.Select(t =>
        {
            var aliases = 0;
            return (t, $"[{t.Entity.GetSchema()}].[{t.Entity.GetTableName()}] AS t WHERE {Scope(t.Entity, "t", erased, ids, ref aliases)}");
        }).ToList();
    }

    /// <summary>
    /// Erases the attempts <paramref name="sessionIds"/> (one student's, at the exams of <paramref name="assignmentIds"/>) as if they
    /// were never taken, in one transaction: the same session tables as the platform-wide reset, scoped to those attempts and deleted
    /// children first; their grading jobs; the result and appeal emails about them or the assignments; and on those assignments only,
    /// the released-result mark and LTI grade passback state. Attempts used is counted from the attempts, so it goes back too.
    /// <paramref name="audit"/> adds the audit entry (it is saved in the same transaction). No model is called and no email is sent.
    /// </summary>
    public static async Task<IReadOnlyList<Line>> ResetAttemptsAsync(GadflyDbContext db, IReadOnlyCollection<Guid> sessionIds,
        IReadOnlyCollection<Guid> assignmentIds, Action<IReadOnlyList<Line>> audit, CancellationToken ct)
    {
        var sessions = sessionIds.Distinct().ToList();
        var assignments = assignmentIds.Distinct().ToList();
        var keys = sessions.Select(s => s.ToString()).ToList();
        var lines = new List<Line>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            lines.Clear();
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var jobs = 0;
            foreach (var key in keys)
                jobs += await db.Jobs.Where(j => j.Kind != SourceProcessingJob.JobKind && j.PayloadJson.Contains(key)).ExecuteDeleteAsync(ct);
            lines.Add(new(Name(db, typeof(Job)), "delete", jobs));
            var appeals = await db.GradeAppeals.Where(a => sessions.Contains(a.SessionId)).Select(a => a.Id).ToListAsync(ct);
            var about = sessions.Concat(assignments).Concat(appeals).Select(id => (Guid?)id).ToList();
            lines.Add(new(Name(db, typeof(EmailLog)), "delete", await db.EmailLogs
                .Where(e => SessionEmailKinds.Contains(e.Kind) && about.Contains(e.RelatedId)).ExecuteDeleteAsync(ct)));
            lines.Add(new(Name(db, typeof(ExamAssignment)), "update", await db.ExamAssignments
                .Where(a => assignments.Contains(a.Id) && a.ResultsReleasedAt != null)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.ResultsReleasedAt, (DateTimeOffset?)null), ct)));
            lines.Add(new(Name(db, typeof(LtiAssignmentLink)), "update", await Passback(db)
                .Where(l => assignments.Contains(l.AssignmentId))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(l => l.PassbackSentAt, (DateTimeOffset?)null).SetProperty(l => l.LastPassbackPercent, (decimal?)null)
                    .SetProperty(l => l.PassbackPendingPercent, (decimal?)null).SetProperty(l => l.PassbackAttempts, 0)
                    .SetProperty(l => l.PassbackRetryAt, (DateTimeOffset?)null).SetProperty(l => l.PassbackError, (string?)null)
                    .SetProperty(l => l.PassbackFailedAt, (DateTimeOffset?)null), ct)));
            foreach (var (t, from) in ScopedSessionTables(db.Model, sessions))
#pragma warning disable EF1002   // the table and column names come from the EF model and the ids are GUIDs, never input text
                lines.Add(new(t.Name, "delete", await db.Database.ExecuteSqlRawAsync($"DELETE t FROM {from}", ct)));
#pragma warning restore EF1002
            audit(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        return lines;
    }
}
