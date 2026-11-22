using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>
/// An invoice-ready usage export for the organization's own owners and admins (GAD-T371): one row per month,
/// sessions and metered cost, the same numbers org/usage shows. Wired with <c>app.MapPlanExport()</c>.
/// </summary>
public static class PlanExportEndpoints
{
    public const string Path = "/admin/export/plan-usage.csv";
    private const int Months = 24;

    public static IEndpointRouteBuilder MapPlanExport(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (ITenantContext tenant, GadflyDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                if (tenant.OrganizationId is not { } org) return Results.Forbid();
                var now = clock.GetUtcNow();
                var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var start = thisMonth.AddMonths(-(Months - 1));

                var usage = await db.UsageEntries.AsNoTracking()
                    .Where(u => u.OrganizationId == org && u.CreatedAt >= start)
                    .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Cost = g.Sum(u => u.CostUsd) })
                    .ToListAsync(ct);
                var sessions = await db.ExamSessions.AsNoTracking()
                    .Where(s => s.OrganizationId == org && s.CreatedAt >= start)
                    .GroupBy(s => new { s.CreatedAt.Year, s.CreatedAt.Month, s.State })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.State, Count = g.Count() })
                    .ToListAsync(ct);
                var settings = await db.OrganizationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == org, ct);
                var plan = settings?.Plan ?? "Trial";
                var allowance = settings?.MonthlyExamAllowance;

                var sb = new System.Text.StringBuilder();
                sb.Append(StudentCsv.Line("month", "plan", "sessions_started", "sessions_ended", "exam_allowance", "metered_cost_usd"));
                for (var i = 0; i < Months; i++)
                {
                    var m = thisMonth.AddMonths(-i);
                    var cost = usage.Where(u => u.Year == m.Year && u.Month == m.Month).Sum(u => u.Cost);
                    var inMonth = sessions.Where(s => s.Year == m.Year && s.Month == m.Month).ToList();
                    var started = inMonth.Sum(s => s.Count);
                    var ended = inMonth.Where(s => s.State == SessionState.Ended).Sum(s => s.Count);
                    if (started == 0 && cost == 0) continue;   // an invoice export skips months with nothing to bill
                    sb.Append(StudentCsv.Line(m.ToString("yyyy-MM"), plan, started, ended, allowance, cost));
                }

                var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"gadfly-usage-{clock.GetUtcNow().UtcDateTime:yyyyMMdd-HHmm}.csv");
            })
            .RequireAuthorization(GadflyPolicies.OrgAdmin);
        return app;
    }
}
