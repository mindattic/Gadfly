using Gadfly.Assessment;
using NUnit.Framework;
using V = Gadfly.Assessment.VerificationState;
using G = Gadfly.Assessment.GradeStatus;

namespace Gadfly.Tests.Assessment;

/// <summary>
/// Part 4.6 response-verification and Part 11.2 grade-status machines. The expected transitions are written
/// out here independently of the product's table, then every (from, to) pair of each enum is checked against
/// them, so any added, dropped or widened transition fails a named case.
/// </summary>
[TestFixture]
public class VerificationMachineTests
{
    private static readonly Dictionary<V, V[]> Expected = new()
    {
        [V.Captured] = [V.Confirmed, V.VerificationRequired],
        [V.VerificationRequired] = [V.AwaitingStudentConfirmation, V.FlaggedForHumanReview, V.Unresolved],
        [V.AwaitingStudentConfirmation] = [V.Confirmed, V.Corrected, V.Clarified, V.Unresolved],
        [V.Unresolved] = [V.FlaggedForHumanReview, V.AwaitingStudentConfirmation],
        [V.Confirmed] = [V.FlaggedForHumanReview],
        [V.Corrected] = [V.FlaggedForHumanReview],
        [V.Clarified] = [V.FlaggedForHumanReview],
        [V.FlaggedForHumanReview] = [V.Confirmed, V.Corrected, V.Clarified],
    };

    public static IEnumerable<TestCaseData> AllPairs() =>
        from f in Enum.GetValues<V>()
        from t in Enum.GetValues<V>()
        let allowed = Expected[f].Contains(t)
        select new TestCaseData(f, t, allowed).SetName($"Verification_{f}_to_{t}_{(allowed ? "Allowed" : "Refused")}");

    [TestCaseSource(nameof(AllPairs))]
    public void Transition(V from, V to, bool allowed)
    {
        Assert.That(VerificationMachine.CanMove(from, to), Is.EqualTo(allowed));
        if (allowed)
            Assert.That(VerificationMachine.Move(from, to), Is.EqualTo(to));
        else
        {
            var ex = Assert.Throws<InvalidStateTransitionException>(() => VerificationMachine.Move(from, to));
            Assert.That(ex!.Message, Does.Contain(from.ToString()).And.Contain(to.ToString()));
        }
    }

    [Test]
    public void EveryStateHasARow() => Assert.That(Expected.Keys, Is.EquivalentTo(Enum.GetValues<V>()));

    [Test]
    public void NoStateMayStayInPlace()
    {
        foreach (var s in Enum.GetValues<V>())
            Assert.That(VerificationMachine.CanMove(s, s), Is.False, s.ToString());
    }

    [Test]
    public void InvalidStateTransition_IsAnInvalidOperation() =>
        Assert.That(new InvalidStateTransitionException("x"), Is.InstanceOf<InvalidOperationException>());

    [TestCase(V.Captured, true)]
    [TestCase(V.Confirmed, true)]
    [TestCase(V.Corrected, true)]
    [TestCase(V.Clarified, true)]
    [TestCase(V.VerificationRequired, false)]
    [TestCase(V.AwaitingStudentConfirmation, false)]
    [TestCase(V.Unresolved, false)]
    [TestCase(V.FlaggedForHumanReview, false)]
    public void IsVerified(V state, bool verified) => Assert.That(VerificationMachine.IsVerified(state), Is.EqualTo(verified));

    [TestCase(V.Unresolved, true)]
    [TestCase(V.FlaggedForHumanReview, true)]
    [TestCase(V.Captured, false)]
    [TestCase(V.VerificationRequired, false)]
    [TestCase(V.AwaitingStudentConfirmation, false)]
    [TestCase(V.Confirmed, false)]
    [TestCase(V.Corrected, false)]
    [TestCase(V.Clarified, false)]
    public void NeedsHuman(V state, bool needs) => Assert.That(VerificationMachine.NeedsHuman(state), Is.EqualTo(needs));

    [Test]
    public void NoStateIsBothVerifiedAndNeedingAHuman()
    {
        foreach (var s in Enum.GetValues<V>())
            Assert.That(VerificationMachine.IsVerified(s) && VerificationMachine.NeedsHuman(s), Is.False, s.ToString());
    }

    [Test]
    public void TypicalTranscriptCorrectionPath_IsAllowed()
    {
        var s = V.Captured;
        s = VerificationMachine.Move(s, V.VerificationRequired);
        s = VerificationMachine.Move(s, V.AwaitingStudentConfirmation);
        s = VerificationMachine.Move(s, V.Corrected);
        Assert.That(VerificationMachine.IsVerified(s), Is.True);
    }

    [Test]
    public void UnresolvedResponse_ReachesAHuman_ThenCanBeResolved()
    {
        var s = VerificationMachine.Move(V.VerificationRequired, V.Unresolved);
        Assert.That(VerificationMachine.NeedsHuman(s), Is.True);
        s = VerificationMachine.Move(s, V.FlaggedForHumanReview);
        s = VerificationMachine.Move(s, V.Confirmed);
        Assert.That(VerificationMachine.IsVerified(s), Is.True);
    }

    [Test]
    public void ACapturedResponseCannotSkipStraightToCorrected() =>
        Assert.That(VerificationMachine.CanMove(V.Captured, V.Corrected), Is.False);
}

[TestFixture]
public class GradeStatusMachineTests
{
    private static readonly Dictionary<G, G[]> Expected = new()
    {
        [G.ExaminationInProgress] = [G.ExaminationCompleted],
        [G.ExaminationCompleted] = [G.TranscriptVerificationPending, G.AiGradingPending],
        [G.TranscriptVerificationPending] = [G.AiGradingPending],
        [G.AiGradingPending] = [G.HumanReviewPending],
        [G.HumanReviewPending] = [G.ReconciliationRequired, G.AwaitingFinalApproval],
        [G.ReconciliationRequired] = [G.AwaitingFinalApproval, G.HumanReviewPending],
        [G.AwaitingFinalApproval] = [G.Finalized, G.ReconciliationRequired, G.HumanReviewPending],
        [G.Finalized] = [G.Published],
        [G.Published] = [G.UnderAppeal, G.Amended],
        [G.UnderAppeal] = [G.Published, G.Amended],
        [G.Amended] = [G.Published, G.UnderAppeal],
    };

    public static IEnumerable<TestCaseData> AllPairs() =>
        from f in Enum.GetValues<G>()
        from t in Enum.GetValues<G>()
        let allowed = Expected[f].Contains(t)
        select new TestCaseData(f, t, allowed).SetName($"Grade_{f}_to_{t}_{(allowed ? "Allowed" : "Refused")}");

    [TestCaseSource(nameof(AllPairs))]
    public void Transition(G from, G to, bool allowed)
    {
        Assert.That(GradeStatusMachine.CanMove(from, to), Is.EqualTo(allowed));
        if (allowed)
            Assert.That(GradeStatusMachine.Move(from, to), Is.EqualTo(to));
        else
        {
            var ex = Assert.Throws<InvalidStateTransitionException>(() => GradeStatusMachine.Move(from, to));
            Assert.That(ex!.Message, Does.Contain(GradeStatusMachine.Label(from)).And.Contain(GradeStatusMachine.Label(to)));
        }
    }

    [Test]
    public void EveryStatusHasARow() => Assert.That(Expected.Keys, Is.EquivalentTo(Enum.GetValues<G>()));

    [TestCase(G.ExaminationInProgress, "Examination In Progress")]
    [TestCase(G.ExaminationCompleted, "Examination Completed")]
    [TestCase(G.TranscriptVerificationPending, "Transcript Verification Pending")]
    [TestCase(G.AiGradingPending, "AI Grading Pending")]
    [TestCase(G.HumanReviewPending, "Human Review Pending")]
    [TestCase(G.ReconciliationRequired, "Reconciliation Required")]
    [TestCase(G.AwaitingFinalApproval, "Awaiting Final Approval")]
    [TestCase(G.Finalized, "Finalized")]
    [TestCase(G.Published, "Published")]
    [TestCase(G.UnderAppeal, "Under Appeal")]
    [TestCase(G.Amended, "Amended")]
    public void Label_MatchesPart112(G status, string label) => Assert.That(GradeStatusMachine.Label(status), Is.EqualTo(label));

    [TestCase(G.Published, true)]
    [TestCase(G.UnderAppeal, true)]
    [TestCase(G.Amended, true)]
    [TestCase(G.ExaminationInProgress, false)]
    [TestCase(G.ExaminationCompleted, false)]
    [TestCase(G.TranscriptVerificationPending, false)]
    [TestCase(G.AiGradingPending, false)]
    [TestCase(G.HumanReviewPending, false)]
    [TestCase(G.ReconciliationRequired, false)]
    [TestCase(G.AwaitingFinalApproval, false)]
    [TestCase(G.Finalized, false)]
    public void IsVisibleToStudent(G status, bool visible) => Assert.That(GradeStatusMachine.IsVisibleToStudent(status), Is.EqualTo(visible));

    [Test]
    public void PublicationOnlyFromFinalized()
    {
        var into = Enum.GetValues<G>().Where(f => f is not (G.UnderAppeal or G.Amended) && GradeStatusMachine.CanMove(f, G.Published)).ToList();
        Assert.That(into, Is.EqualTo(new[] { G.Finalized }));
    }

    [Test]
    public void FinalizationOnlyFromAwaitingFinalApproval()
    {
        var into = Enum.GetValues<G>().Where(f => GradeStatusMachine.CanMove(f, G.Finalized)).ToList();
        Assert.That(into, Is.EqualTo(new[] { G.AwaitingFinalApproval }));
    }

    [Test]
    public void ReconciliationCannotJumpToFinalized() =>
        Assert.That(GradeStatusMachine.CanMove(G.ReconciliationRequired, G.Finalized), Is.False);

    [Test]
    public void AiGradingNeverSkipsHumanReview()
    {
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.AwaitingFinalApproval), Is.False);
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.Finalized), Is.False);
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.Published), Is.False);
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.Finalized, GradingMode.HumanOnly), Is.False);
    }

    [Test]
    public void AiOnlyGrading_FinalizesWithoutHumanReview()
    {
        // Owner decision 2026-11-23 (GAD-T703): under AI-only grading the AI's grade is final.
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.Finalized, GradingMode.AiOnly), Is.True);
        Assert.That(GradeStatusMachine.CanMove(G.AiGradingPending, G.Published, GradingMode.AiOnly), Is.False);
    }

    [Test]
    public void FullHappyPath_WithReconciliationAndAppeal()
    {
        G[] path =
        [
            G.ExaminationInProgress, G.ExaminationCompleted, G.TranscriptVerificationPending, G.AiGradingPending,
            G.HumanReviewPending, G.ReconciliationRequired, G.AwaitingFinalApproval, G.Finalized, G.Published,
            G.UnderAppeal, G.Amended, G.Published,
        ];
        var s = path[0];
        foreach (var next in path.Skip(1)) s = GradeStatusMachine.Move(s, next);
        Assert.That(s, Is.EqualTo(G.Published));
    }

    [Test]
    public void NothingReturnsToInProgress()
    {
        foreach (var f in Enum.GetValues<G>())
            Assert.That(GradeStatusMachine.CanMove(f, G.ExaminationInProgress), Is.False, f.ToString());
    }

    [Test]
    public void StudentVisibilityIsReachableOnlyThroughPublication()
    {
        // Every status a student can see is only entered from Finalized (publication) or from another visible status.
        foreach (var to in Enum.GetValues<G>().Where(GradeStatusMachine.IsVisibleToStudent))
        foreach (var from in Enum.GetValues<G>().Where(f => GradeStatusMachine.CanMove(f, to)))
            Assert.That(from == G.Finalized || GradeStatusMachine.IsVisibleToStudent(from), Is.True, $"{from} -> {to}");
    }
}
