using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Concept map and rubric approval before summative use (GAD-T437): an examination is not published on an unapproved or
/// changed version, a changed approved version stops new attempts, and Learning Mode and older exams are not blocked.
/// </summary>
[TestFixture]
public class RubricApprovalRulesTests
{
    private static SessionPolicy Approved(int version) =>
        new() { RubricApproval = new RubricApproval(version, Guid.NewGuid(), DateTimeOffset.UnixEpoch) };

    [Test]
    public void Examination_NeedsTheCurrentVersionApproved()
    {
        Assert.That(RubricApprovalService.PublishProblem(AssessmentMode.Examination, new SessionPolicy(), 3), Does.Contain("approve"));
        Assert.That(RubricApprovalService.PublishProblem(AssessmentMode.Examination, Approved(2), 3), Does.Contain("version 2"));
        Assert.That(RubricApprovalService.PublishProblem(AssessmentMode.Examination, Approved(3), 3), Is.Null);
    }

    [Test]
    public void LearningMode_IsNotBlocked()
    {
        Assert.That(RubricApprovalService.PublishProblem(AssessmentMode.Learning, new SessionPolicy(), 3), Is.Null);
        Assert.That(RubricApprovalService.AttemptRefusal(AssessmentMode.Learning, Approved(1), 3), Is.Null);
    }

    [Test]
    public void ChangedApprovedVersion_StopsNewAttempts_ButOlderExamsKeepRunning()
    {
        Assert.That(RubricApprovalService.AttemptRefusal(AssessmentMode.Examination, Approved(2), 3), Is.Not.Null);
        Assert.That(RubricApprovalService.AttemptRefusal(AssessmentMode.Examination, Approved(3), 3), Is.Null);
        Assert.That(RubricApprovalService.AttemptRefusal(AssessmentMode.Examination, new SessionPolicy(), 3), Is.Null,
            "an exam published before approval was required is not stopped");
    }
}
