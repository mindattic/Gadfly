using System.Text;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Admin;

/// <summary>
/// CSV downloads for the admin people pages (GAD-T296, GAD-T298): the same filters as the page, every matching row, owners and
/// admins only (the policy, then the service's own check). Wired with <c>app.MapPeopleExports()</c>.
/// </summary>
public static class PeopleExportEndpoints
{
    public const string StudentsPath = "/admin/export/students.csv";

    /// <summary>The students export address for a query (the page's link).</summary>
    public static string StudentsCsv(StudentQuery q) =>
        StudentsPath + Query(("q", q.Search), ("exam", q.ExamId?.ToString("D")), ("status", q.Status == StudentStatusFilter.All ? null : q.Status.ToString()),
            ("acc", q.Accommodation == StudentAccommodationFilter.Any ? null : q.Accommodation.ToString()),
            ("sort", q.Sort == StudentSort.Name ? null : q.Sort.ToString()), ("desc", q.Descending ? "1" : null));

    public const string AssignmentsPath = "/admin/export/assignments.csv";

    /// <summary>The address values of an assignments query (shared by the page's own address and its export link).</summary>
    public static (string Key, string? Value)[] AssignmentPairs(AssignmentQuery q) =>
    [
        ("exam", q.ExamId?.ToString("D")), ("student", q.Student),
        ("status", q.Progress == AssignmentProgressFilter.Any ? null : q.Progress.ToString()),
        ("acc", q.Accommodation == AssignmentAccommodationFilter.Any ? null : q.Accommodation.ToString()),
        ("from", Iso(q.DueFrom)), ("to", Iso(q.DueTo)),
        ("sort", q.Sort == AssignmentSort.Student ? null : q.Sort.ToString()), ("desc", q.Descending ? "1" : null),
    ];

    public static string AssignmentsCsv(AssignmentQuery q) => AssignmentsPath + Query(AssignmentPairs(q));

    private static string? Iso(DateTimeOffset? d) =>
        d?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An instant from an address value, or null.</summary>
    public static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>A query string from the pairs that have a value ("" when none do).</summary>
    public static string Query(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}").ToList();
        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }

    /// <summary>An enum from an address value, or <paramref name="fallback"/> when it is missing or not one of the names.</summary>
    public static T Enum<T>(string? value, T fallback) where T : struct, System.Enum =>
        !string.IsNullOrWhiteSpace(value) && !int.TryParse(value, out _) && System.Enum.TryParse<T>(value, ignoreCase: true, out var v) && System.Enum.IsDefined(v) ? v : fallback;

    internal static IResult Csv(string csv, string name) =>
        Results.File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)], "text/csv; charset=utf-8", name);

    public static IEndpointRouteBuilder MapPeopleExports(this IEndpointRouteBuilder app)
    {
        app.MapGet(StudentsPath, async (string? q, Guid? exam, string? status, string? acc, string? sort, string? desc,
                HttpContext http, TimeProvider clock, CancellationToken ct) =>
            {
                var svc = new StudentAdminService(http.RequestServices);
                var query = new StudentQuery
                {
                    Search = q, ExamId = exam, Status = Enum(status, StudentStatusFilter.All), Accommodation = Enum(acc, StudentAccommodationFilter.Any),
                    Sort = Enum(sort, StudentSort.Name), Descending = desc == "1",
                };
                try
                {
                    var csv = await svc.ExportCsvAsync(query, ct);
                    return Csv(csv, $"gadfly-students-{clock.GetUtcNow().UtcDateTime:yyyyMMdd-HHmm}.csv");
                }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
            })
            .RequireAuthorization(GadflyPolicies.OrgAdmin);

        app.MapGet(AssignmentsPath, async (Guid? exam, string? student, string? status, string? acc, string? from, string? to, string? sort, string? desc,
                HttpContext http, TimeProvider clock, CancellationToken ct) =>
            {
                var svc = new AssignmentAdminService(http.RequestServices);
                var query = new AssignmentQuery
                {
                    ExamId = exam, Student = student, Progress = Enum(status, AssignmentProgressFilter.Any),
                    Accommodation = Enum(acc, AssignmentAccommodationFilter.Any), DueFrom = ParseInstant(from), DueTo = ParseInstant(to),
                    Sort = Enum(sort, AssignmentSort.Student), Descending = desc == "1",
                };
                try
                {
                    var csv = await svc.ExportCsvAsync(query, ct);
                    return Csv(csv, $"gadfly-assignments-{clock.GetUtcNow().UtcDateTime:yyyyMMdd-HHmm}.csv");
                }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
            })
            .RequireAuthorization(GadflyPolicies.OrgAdmin);
        return app;
    }
}
