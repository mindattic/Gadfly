namespace Gadfly.Assessment;

/// <summary>Part 16.2 roles. Platform Administrator is MindAttic staff, outside any organization.</summary>
public static class AssessmentRoles
{
    public const string Student = "Student";
    public const string Teacher = "Instructor";
    public const string HumanExaminer = "Examiner";
    public const string SecondReviewer = "SecondReviewer";
    public const string InstitutionAdmin = "Admin";
    public const string Owner = "Owner";
    public const string PlatformAdmin = "PlatformAdmin";
}

/// <summary>Every action whose permission the platform checks (Part 16.2, Scenario J).</summary>
public enum Permission
{
    TakeExamination,
    ViewOwnPublishedResults,
    AppealOwnResult,
    ManageCourses,
    ManageAssessments,
    ApproveGradingPolicyChange,
    AssignExaminations,
    ViewTranscripts,
    ListenToRecordings,
    EnterHumanScore,
    ReconcileDisagreement,
    ActAsSecondReviewer,
    ApproveFinalGrade,
    PublishGrade,
    AmendGrade,
    ResolveAppeal,
    ManageMembers,
    ManageInstitutionSettings,
    ViewAnalytics,
    ViewAuditLog,
    ManagePlatform,
}

/// <summary>
/// The explicit role → permission matrix (Part 16.2). Deterministic and exhaustive: a role not listed for
/// a permission does not have it. Grading separation: the person who enters the human score for an
/// examination is not, by role alone, allowed to approve its final grade (approval is a Teacher/Admin act).
/// </summary>
public static class PermissionMatrix
{
    private static readonly IReadOnlyDictionary<string, Permission[]> Grants = new Dictionary<string, Permission[]>(StringComparer.Ordinal)
    {
        [AssessmentRoles.Student] = [Permission.TakeExamination, Permission.ViewOwnPublishedResults, Permission.AppealOwnResult],
        [AssessmentRoles.HumanExaminer] = [Permission.ViewTranscripts, Permission.ListenToRecordings, Permission.EnterHumanScore],
        [AssessmentRoles.SecondReviewer] = [Permission.ViewTranscripts, Permission.ListenToRecordings, Permission.ActAsSecondReviewer, Permission.ReconcileDisagreement],
        [AssessmentRoles.Teacher] =
        [
            Permission.ManageCourses, Permission.ManageAssessments, Permission.AssignExaminations, Permission.ViewTranscripts,
            Permission.ListenToRecordings, Permission.EnterHumanScore, Permission.ReconcileDisagreement, Permission.ApproveFinalGrade,
            Permission.PublishGrade, Permission.AmendGrade, Permission.ResolveAppeal, Permission.ViewAnalytics,
        ],
        [AssessmentRoles.InstitutionAdmin] =
        [
            Permission.ManageCourses, Permission.ManageAssessments, Permission.ApproveGradingPolicyChange, Permission.AssignExaminations,
            Permission.ViewTranscripts, Permission.ListenToRecordings, Permission.ReconcileDisagreement, Permission.ApproveFinalGrade,
            Permission.PublishGrade, Permission.AmendGrade, Permission.ResolveAppeal, Permission.ManageMembers,
            Permission.ManageInstitutionSettings, Permission.ViewAnalytics, Permission.ViewAuditLog,
        ],
        [AssessmentRoles.Owner] =
        [
            Permission.ManageCourses, Permission.ManageAssessments, Permission.ApproveGradingPolicyChange, Permission.AssignExaminations,
            Permission.ViewTranscripts, Permission.ListenToRecordings, Permission.ReconcileDisagreement, Permission.ApproveFinalGrade,
            Permission.PublishGrade, Permission.AmendGrade, Permission.ResolveAppeal, Permission.ManageMembers,
            Permission.ManageInstitutionSettings, Permission.ViewAnalytics, Permission.ViewAuditLog,
        ],
        [AssessmentRoles.PlatformAdmin] = [Permission.ManagePlatform, Permission.ViewAuditLog],
    };

    public static bool Has(string? role, Permission permission) =>
        role is not null && Grants.TryGetValue(role, out var p) && p.Contains(permission);

    public static IReadOnlyList<Permission> Of(string role) => Grants.TryGetValue(role, out var p) ? p : [];

    public static IReadOnlyList<string> Roles => [.. Grants.Keys];
}
