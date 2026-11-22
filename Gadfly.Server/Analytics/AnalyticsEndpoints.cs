using System.Globalization;
using System.Text;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Analytics;

/// <summary>
/// Who may see analytics (GAD-T102): <see cref="Permission.ViewAnalytics"/> from <see cref="PermissionMatrix"/>
/// with the caller's role in the active organization. A refusal is written to the append-only audit log as
/// <c>access.denied</c>.
/// </summary>
public sealed class AnalyticsAccess(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public bool Allowed => tenant.OrganizationId is not null && tenant.UserId is not null && PermissionMatrix.Has(tenant.Role, Permission.ViewAnalytics);

    /// <summary>True when allowed; otherwise records an <c>access.denied</c> audit event and returns false.</summary>
    public async Task<bool> RequireAsync(string attempted, CancellationToken ct = default)
    {
        if (Allowed) return true;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = tenant.Role is { Length: > 40 } r ? r[..40] : tenant.Role,
            Action = "access.denied",
            EntityType = "Analytics",
            Reason = $"{attempted} needs the {Permission.ViewAnalytics} permission; role {tenant.Role ?? "(none)"} does not have it.",
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return false;
    }
}

/// <summary>Analytics wiring: <c>services.AddAnalytics()</c> and <c>app.MapAnalytics()</c> (CSV export).</summary>
public static class AnalyticsEndpoints
{
    public static IServiceCollection AddAnalytics(this IServiceCollection services) => services.AddScoped<AnalyticsAccess>();

    /// <summary>The CSV export of a scope's analytics: every section, one table after another.</summary>
    public static string CsvPath(AnalyticsScope scope) =>
        scope.ExamId is { } e ? $"/analytics/export.csv?exam={e:D}" : scope.CourseId is { } c ? $"/analytics/export.csv?course={c:D}" : "/analytics/export.csv";

    public static IEndpointRouteBuilder MapAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/analytics/export.csv", async (Guid? exam, Guid? course, GadflyDbContext db, ITenantContext tenant,
                HttpContext http, TimeProvider clock, CancellationToken ct) =>
            {
                var access = http.RequestServices.GetService<AnalyticsAccess>() ?? ActivatorUtilities.CreateInstance<AnalyticsAccess>(http.RequestServices);
                if (!await access.RequireAsync("Export analytics", ct)) return Results.Forbid();
                var scope = new AnalyticsScope(exam, course);
                var report = await new AnalyticsService(db, tenant.OrganizationId!.Value, clock, tenant.Role).BuildAsync(scope, ct);
                var csv = ToCsv(report);
                // GAD-T463: the fairness views, for Owners and Admins only (as on the page), grouped by accommodation.
                if (GroupFairness.ViewerAllowed(tenant.Role))
                    csv += FairnessCsv.ToCsv(await FairnessCsv.BuildAsync(db, tenant.OrganizationId!.Value, scope, ct));
                var name = $"gadfly-analytics-{report.GeneratedAt.UtcDateTime:yyyyMMdd-HHmm}.csv";
                return Results.File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)], "text/csv; charset=utf-8", name);
            })
            .RequireAuthorization(GadflyPolicies.Member);
        return app;
    }

    public static string ToCsv(AnalyticsReport r)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string N(double? d, string f = "0.###") => d is { } x ? x.ToString(f, ci) : "";
        void Row(params object?[] cells) => sb.AppendJoin(',', cells.Select(c => Csv(c switch
        {
            null => "",
            double d => d.ToString("0.###", ci),
            IFormattable f => f.ToString(null, ci),
            _ => c.ToString(),
        }))).Append("\r\n");
        void Section(string title) { if (sb.Length > 0) sb.Append("\r\n"); Row("section", title); }

        Row("scope", r.ScopeTitle);
        Row("generated_at_utc", r.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", ci));
        Row("caveat", AnalyticsService.AgreementCaveat);

        Section("completion");
        Row("assessment", "assigned", "started", "completed", "in_progress", "failed_sessions", "completion_rate");
        foreach (var c in r.Completion) Row(c.ExamTitle, c.Assigned, c.Started, c.Completed, c.InProgress, c.Failed, N(c.Rate));

        Section("mastery_by_objective");
        Row("assessment", "objective", "source", "level_0_insufficient_evidence", "level_1", "level_2", "level_3", "level_4", "level_5",
            "mean_conclusive_level", "share_levels_1_2");
        foreach (var m in r.ObjectiveMastery)
            Row(m.ExamTitle, m.Objective, m.Source == LevelSource.Human ? "human" : "ai", m.Counts[0], m.Counts[1], m.Counts[2], m.Counts[3],
                m.Counts[4], m.Counts[5], N(m.MeanConclusiveLevel), N(m.LowShare));

        Section("engine_concept_mastery");
        Row("assessment", "concept", "sessions", "mean_estimate", "mastered", "open", "aporia", "exhausted", "not_started");
        foreach (var c in r.ConceptMastery) Row(c.ExamTitle, c.Concept, c.Sessions, N(c.MeanEstimate), c.Mastered, c.Open, c.Aporia, c.Exhausted, c.NotStarted);

        Section("recurring_misconceptions");
        Row("misconception", "objective", "sessions", "mentions", "sources");
        foreach (var m in r.Misconceptions) Row(m.Text, m.Objective, m.Sessions, m.Mentions, m.Sources);

        Section("class_wide_gaps");
        Row("area", "assessment", "basis", "measure", "grades_or_sessions");
        foreach (var g in r.Gaps) Row(g.Area, g.ExamTitle, g.Basis, g.Measure, g.Evidence);

        var a = r.Agreement;
        Section("ai_human_agreement");
        Row("objective", "pairs", "exact_level_agreement", "within_one_level", "mean_ai_minus_human_points", "mean_absolute_difference_points");
        Row("(all)", a.Pairs, N(a.ExactLevel), N(a.WithinOneLevel), N(a.MeanSignedPoints), N(a.MeanAbsPoints));
        foreach (var o in a.ByObjective) Row(o.Objective, o.Pairs, N(o.ExactLevel), N(o.WithinOneLevel), N(o.MeanSignedPoints), N(o.MeanAbsPoints));
        Row("disagreements", a.Disagreements);
        Row("additional_review_band", a.AdditionalReview);
        Row("mandatory_reconciliation_band", a.MandatoryReconciliation);
        Row("disagreements_resolved", a.Resolved);
        Row("reconciliations", a.Reconciliations);

        var c2 = r.Corrections;
        Section("transcript_corrections");
        Row("measure", "count");
        Row("responses", c2.Responses);
        Row("responses_with_corrections", c2.ResponsesWithCorrections);
        foreach (var (k, v) in c2.VersionsByKind) Row(k, v);
        foreach (var (k, v) in c2.ClarificationsByType) Row("clarification: " + k, v);
        Row("clarifications_answered", c2.ClarificationsAnswered);
        foreach (var (k, v) in c2.VerificationStates) Row("verification: " + k, v);

        var rel = r.Reliability;
        Section("reliability");
        Row("measure", "value");
        Row("ai_grades", rel.AiGrades);
        Row("ai_inconclusive_rate", N(rel.AiInconclusiveRate));
        Row("human_grades", rel.HumanGrades);
        Row("human_inconclusive_rate", N(rel.HumanInconclusiveRate));
        Row("mock_ai_grades_excluded", rel.MockAiGradesExcluded);
        Row("ai_human_pairs", rel.Pairs);
        Row("reconciliation_rate", N(rel.ReconciliationRate));
        Row("judged_answers", rel.JudgedAnswers);
        Row("mean_judge_agreement", N(rel.MeanJudgeAgreement));
        Row("judge_disagreement_rate", N(rel.JudgeDisagreementRate));
        Row("sessions_ended", rel.SessionsEnded);
        foreach (var (k, v) in rel.EndReasons) Row("end_reason: " + k, v);
        return sb.ToString();
    }

    /// <summary>RFC 4180 quoting, with a leading apostrophe on cells a spreadsheet would run as a formula.</summary>
    internal static string Csv(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            v = "'" + v;
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
