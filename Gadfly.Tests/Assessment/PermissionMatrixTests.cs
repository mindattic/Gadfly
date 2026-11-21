using Gadfly.Assessment;
using Gadfly.Server.Tenancy;
using NUnit.Framework;
using P = Gadfly.Assessment.Permission;
using R = Gadfly.Assessment.AssessmentRoles;

namespace Gadfly.Tests.Assessment;

/// <summary>
/// Part 16.2 role → permission matrix (Scenario J's basis). The expectation table is written out here, then
/// every role × permission pair is checked, so a widened or narrowed grant fails a named case.
/// </summary>
[TestFixture]
public class PermissionMatrixTests
{
    private static readonly P[] Staff =
    [
        P.ManageCourses, P.ManageAssessments, P.ApproveGradingPolicyChange, P.AssignExaminations, P.ViewTranscripts,
        P.ListenToRecordings, P.ReconcileDisagreement, P.ApproveFinalGrade, P.PublishGrade, P.AmendGrade, P.ResolveAppeal,
        P.ManageMembers, P.ManageInstitutionSettings, P.ViewAnalytics, P.ViewAuditLog,
    ];

    private static readonly Dictionary<string, P[]> Expected = new()
    {
        [R.Student] = [P.TakeExamination, P.ViewOwnPublishedResults, P.AppealOwnResult],
        [R.HumanExaminer] = [P.ViewTranscripts, P.ListenToRecordings, P.EnterHumanScore],
        [R.SecondReviewer] = [P.ViewTranscripts, P.ListenToRecordings, P.ActAsSecondReviewer, P.ReconcileDisagreement],
        [R.Teacher] =
        [
            P.ManageCourses, P.ManageAssessments, P.AssignExaminations, P.ViewTranscripts, P.ListenToRecordings,
            P.EnterHumanScore, P.ReconcileDisagreement, P.ApproveFinalGrade, P.PublishGrade, P.AmendGrade, P.ResolveAppeal,
            P.ViewAnalytics,
        ],
        [R.InstitutionAdmin] = Staff,
        [R.Owner] = Staff,
        [R.PlatformAdmin] = [P.ManagePlatform, P.ViewAuditLog],
    };

    public static IEnumerable<TestCaseData> Matrix() =>
        from role in Expected.Keys
        from p in Enum.GetValues<P>()
        let has = Expected[role].Contains(p)
        select new TestCaseData(role, p, has).SetName($"Role_{role}_{p}_{(has ? "Granted" : "Denied")}");

    [TestCaseSource(nameof(Matrix))]
    public void RoleHasExactlyTheExpectedPermissions(string role, P permission, bool granted) =>
        Assert.That(PermissionMatrix.Has(role, permission), Is.EqualTo(granted));

    [Test]
    public void Roles_AreExactlyTheSevenPart162Roles() =>
        Assert.That(PermissionMatrix.Roles, Is.EquivalentTo(Expected.Keys));

    [Test]
    public void Of_ListsEachRolesGrants()
    {
        foreach (var (role, perms) in Expected)
            Assert.That(PermissionMatrix.Of(role), Is.EquivalentTo(perms), role);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Api")]
    [TestCase("student")]
    [TestCase("OWNER")]
    [TestCase("Teacher")]
    [TestCase("Nobody")]
    public void UnknownOrMiscasedRoles_HaveNothing(string? role)
    {
        foreach (var p in Enum.GetValues<P>())
            Assert.That(PermissionMatrix.Has(role, p), Is.False, $"{role} {p}");
        if (role is not null) Assert.That(PermissionMatrix.Of(role), Is.Empty);
    }

    [Test]
    public void EveryPermission_IsGrantedToSomeone()
    {
        foreach (var p in Enum.GetValues<P>())
            Assert.That(PermissionMatrix.Roles.Any(r => PermissionMatrix.Has(r, p)), Is.True, p.ToString());
    }

    [Test]
    public void OnlyStudentsTakeExaminations() =>
        Assert.That(PermissionMatrix.Roles.Where(r => PermissionMatrix.Has(r, P.TakeExamination)), Is.EqualTo(new[] { R.Student }));

    [Test]
    public void StudentsHaveNoGradingOrTranscriptPowers()
    {
        foreach (var p in new[] { P.EnterHumanScore, P.ApproveFinalGrade, P.PublishGrade, P.AmendGrade, P.ReconcileDisagreement,
                     P.ViewTranscripts, P.ViewAuditLog, P.ManageMembers })
            Assert.That(PermissionMatrix.Has(R.Student, p), Is.False, p.ToString());
    }

    [Test]
    public void GradingSeparation_TheHumanExaminerCannotApproveOrPublish()
    {
        Assert.That(PermissionMatrix.Has(R.HumanExaminer, P.EnterHumanScore), Is.True);
        Assert.That(PermissionMatrix.Has(R.HumanExaminer, P.ApproveFinalGrade), Is.False);
        Assert.That(PermissionMatrix.Has(R.HumanExaminer, P.PublishGrade), Is.False);
        Assert.That(PermissionMatrix.Has(R.HumanExaminer, P.AmendGrade), Is.False);
    }

    [Test]
    public void SecondReviewer_ReconcilesButNeverScoresOrApproves()
    {
        Assert.That(PermissionMatrix.Has(R.SecondReviewer, P.ReconcileDisagreement), Is.True);
        Assert.That(PermissionMatrix.Has(R.SecondReviewer, P.EnterHumanScore), Is.False);
        Assert.That(PermissionMatrix.Has(R.SecondReviewer, P.ApproveFinalGrade), Is.False);
    }

    [Test]
    public void OnlyInstitutionLeadershipApprovesPolicyChanges() =>
        Assert.That(PermissionMatrix.Roles.Where(r => PermissionMatrix.Has(r, P.ApproveGradingPolicyChange)),
            Is.EquivalentTo(new[] { R.InstitutionAdmin, R.Owner }));

    [Test]
    public void PlatformAdmin_DoesNotGradeOrSeeTranscripts()
    {
        foreach (var p in Enum.GetValues<P>().Except([P.ManagePlatform, P.ViewAuditLog]))
            Assert.That(PermissionMatrix.Has(R.PlatformAdmin, p), Is.False, p.ToString());
    }

    [Test]
    public void OnlyPlatformAdminManagesThePlatform() =>
        Assert.That(PermissionMatrix.Roles.Where(r => PermissionMatrix.Has(r, P.ManagePlatform)), Is.EqualTo(new[] { R.PlatformAdmin }));

    [Test]
    public void AssessmentRoleNames_MatchTheServersOrgRoles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(R.Student, Is.EqualTo(OrgRoles.Student));
            Assert.That(R.Teacher, Is.EqualTo(OrgRoles.Instructor));
            Assert.That(R.HumanExaminer, Is.EqualTo(OrgRoles.Examiner));
            Assert.That(R.SecondReviewer, Is.EqualTo(OrgRoles.SecondReviewer));
            Assert.That(R.InstitutionAdmin, Is.EqualTo(OrgRoles.Admin));
            Assert.That(R.Owner, Is.EqualTo(OrgRoles.Owner));
        });
    }

    [Test]
    public void EveryOrgRoleAPersonCanHold_HasAMatrixRow()
    {
        foreach (var role in OrgRoles.All)
            Assert.That(PermissionMatrix.Roles, Does.Contain(role), role);
    }

    [Test]
    public void OrgRoles_ValidityAndMfa()
    {
        foreach (var role in OrgRoles.All) Assert.That(OrgRoles.IsValid(role), Is.True, role);
        Assert.That(OrgRoles.IsValid(OrgRoles.Api), Is.True);
        Assert.That(OrgRoles.All, Does.Not.Contain(OrgRoles.Api), "Api is never a person's role");
        Assert.That(OrgRoles.IsValid("owner"), Is.False);
        Assert.That(OrgRoles.IsValid("PlatformAdmin"), Is.False);
        Assert.That(OrgRoles.RequiresMfa(OrgRoles.Student), Is.False);
        foreach (var staff in new[] { OrgRoles.Owner, OrgRoles.Admin, OrgRoles.Instructor, OrgRoles.Examiner, OrgRoles.SecondReviewer })
            Assert.That(OrgRoles.RequiresMfa(staff), Is.True, staff);
        Assert.That(OrgRoles.RequiresMfa(OrgRoles.Api), Is.False);
    }
}
