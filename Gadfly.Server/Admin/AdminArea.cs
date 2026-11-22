using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Admin;

/// <summary>
/// The admin area's map (GAD-T300): every admin page, grouped, with who may open it. The navigation
/// (Components/Pages/Admin/AdminNav.razor) and the top bar (MainLayout) both read it, so there is one list to keep.
/// Adding an admin page means adding one <see cref="Link"/> here.
/// </summary>
public static class AdminArea
{
    /// <summary>Who may open a page. These mirror the pages' own [Authorize] policies; the page still enforces its policy.</summary>
    public enum Audience
    {
        /// <summary>Owner or Admin of the organization (GadflyPolicies.OrgAdmin).</summary>
        OrgAdmin,
        /// <summary>Owner, Admin or Instructor (GadflyPolicies.Instructor).</summary>
        Instructor,
        /// <summary>Anyone who grades: Owner, Admin, Instructor, Examiner or Second Reviewer.</summary>
        Grader,
        /// <summary>An account that may use the admin desk (DeskAccess.Policy).</summary>
        Desk,
        /// <summary>MindAttic platform staff (GadflyPolicies.SuperAdmin).</summary>
        Platform,
    }

    /// <param name="Href">Relative to the app root, no leading slash.</param>
    /// <param name="Exact">Highlight only on exactly this address (for a page that has pages beneath it in the same folder).</param>
    public sealed record Link(string Href, string Label, Audience Audience, bool Exact = false);

    public sealed record Group(string Name, IReadOnlyList<Link> Links);

    /// <summary>Every admin page, in the order the navigation shows them.</summary>
    public static readonly IReadOnlyList<Group> All =
    [
        new("Overview",
        [
            new("admin", "Dashboard", Audience.OrgAdmin, Exact: true),
            new("admin/live", "Live sessions", Audience.OrgAdmin),
        ]),
        new("People",
        [
            new("admin/students", "Students", Audience.OrgAdmin),
            new("org/members", "Members", Audience.OrgAdmin),
        ]),
        new("Exams",
        [
            new("admin/exams", "Exams", Audience.OrgAdmin),
            new("admin/assignments", "Assignments", Audience.OrgAdmin),
            new("admin/desk", "Desk", Audience.Desk),
            new("sources", "Source material", Audience.Instructor),
            new("assessments", "Assessments", Audience.Instructor),
            new("exams", "Exam builder", Audience.Instructor),
        ]),
        new("Results",
        [
            new("grading", "Grading", Audience.Grader),
            new("review", "Review", Audience.Instructor),
            new("analytics", "Analytics", Audience.Instructor),
        ]),
        new("Organization",
        [
            new("org/settings", "Settings", Audience.OrgAdmin),
            new("org/plan", "Plan & usage", Audience.OrgAdmin),
            new("org/usage", "Usage", Audience.OrgAdmin),
            new("org/api-keys", "API keys", Audience.OrgAdmin),
            new("org/webhooks", "Webhooks", Audience.OrgAdmin),
            new("admin/audit", "Audit log", Audience.OrgAdmin),
            new("admin/status", "System status", Audience.OrgAdmin),
        ]),
        new("Platform",
        [
            new("platform/organizations", "Organizations", Audience.Platform),
        ]),
    ];

    /// <summary>First path segments that belong to the admin area: the navigation is shown on these pages.</summary>
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "org", "sources", "assessments", "exams", "grading", "review", "analytics", "platform" };

    /// <summary>True for the Owner and Admin roles: the people the admin area is for.</summary>
    public static bool IsOrgAdmin(string? role) => role is OrgRoles.Owner or OrgRoles.Admin;

    /// <summary>Whether <paramref name="audience"/> includes a caller with this role, platform flag and desk access.</summary>
    public static bool Allows(Audience audience, string? role, bool isSuperAdmin, bool isDeskAdmin) => audience switch
    {
        Audience.OrgAdmin => IsOrgAdmin(role),
        Audience.Instructor => IsOrgAdmin(role) || role == OrgRoles.Instructor,
        Audience.Grader => IsOrgAdmin(role) || role is OrgRoles.Instructor or OrgRoles.Examiner or OrgRoles.SecondReviewer,
        Audience.Desk => isDeskAdmin,
        Audience.Platform => isSuperAdmin,
        _ => false,
    };

    /// <summary>The groups and links this caller may open; a group with nothing left is dropped.</summary>
    public static IReadOnlyList<Group> GroupsFor(string? role, bool isSuperAdmin, bool isDeskAdmin) =>
        All.Select(g => new Group(g.Name, g.Links.Where(l => Allows(l.Audience, role, isSuperAdmin, isDeskAdmin)).ToList()))
            .Where(g => g.Links.Count > 0)
            .ToList();

    /// <summary>
    /// True when <paramref name="relativePath"/> (the address after the app root, with or without a query) is an admin page.
    /// Student pages (my-exams, exam, my-results), the home page and the account pages are not.
    /// </summary>
    public static bool Contains(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return false;
        var path = relativePath.TrimStart('/');
        var end = path.IndexOfAny(['/', '?', '#']);
        return Roots.Contains(end < 0 ? path : path[..end]);
    }
}
