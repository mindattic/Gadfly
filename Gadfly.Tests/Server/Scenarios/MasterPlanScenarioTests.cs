using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Socrates;
using Gadfly.Sources;
using Gadfly.Sources.Verification;
using Gadfly.Tests.Engine;
using Gadfly.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>
/// docs/MASTER-PLAN.md Part 20, scenarios A-J, as automated examination simulations (GAD-T101): the real engine,
/// planner, judge, verifier and deterministic grading rules with a scripted panel (no live LLM). Scenarios whose
/// evidence lives in SQL (transcript correction, revision, unauthorized change) are in
/// <see cref="MasterPlanPersistenceScenarioTests"/>.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class MasterPlanScenarioTests
{
    private static async Task<SourceExamEngine> Start(ScriptedPanel panel, SessionPolicy policy, ManualClock clock) =>
        await SourceExamEngine.StartAsync(panel, Biology(related: false), policy, clock);

    private static Answer Say(string text, ManualClock clock)
    {
        clock.Advance(TimeSpan.FromSeconds(40));
        return new Answer { Text = text, CompletedAt = clock.GetUtcNow() };
    }

    private const string Definition = "Photosynthesis is the process that converts light energy into chemical energy stored in glucose.";

    [Test]
    public async Task Scenario_A_CorrectAnswerWithoutUnderstanding()
    {
        // The student recites the definition, then cannot explain or apply it.
        var clock = new ManualClock(T0);
        var panel = new ScriptedPanel { JudgeScoreFor = p => ScriptedPanel.StudentAnswer(p) == Definition ? 9 : 2 };
        var policy = new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 5 };
        var engine = await Start(panel, policy, clock);

        var first = await engine.NextQuestionAsync();
        Assert.That(first, Is.Not.Null);
        var judged = await engine.SubmitAnswerAsync(Say(Definition, clock));
        Assert.That(judged.Judgment!.OverallScore, Is.GreaterThanOrEqualTo(0.8), "the definition itself is correct");

        // No mastery from the initial answer: the concept stays open and a targeted follow-up comes next.
        Assert.That(engine.Session.IsEnded, Is.False);
        Assert.That(QuestionPlanner.StatusOf(engine.Session, "photosynthesis"), Is.Not.EqualTo(ConceptStatus.Mastered));
        Assert.That(engine.Session.Mastery["photosynthesis"].Estimate, Is.LessThan(policy.MasteryThreshold));
        var followUp = await engine.NextQuestionAsync();
        Assert.That(followUp, Is.Not.Null, "the examiner keeps probing");
        Assert.That(followUp!.ConceptId, Is.EqualTo("photosynthesis"));
        Assert.That(followUp.Move, Is.Not.EqualTo(MoveKind.Elicit), "a follow-up, not a fresh opening");

        while (engine.Session.OpenTurn is not null)
        {
            await engine.SubmitAnswerAsync(Say("I memorized it for the test, I'm not sure why it works or how to use it.", clock));
            if (await engine.NextQuestionAsync() is null) break;
        }
        var report = engine.Report();
        Assert.That(report.Recommendation, Is.Not.EqualTo(Gadfly.Engine.SessionReport.Mastered));
        Assert.That(report.Concepts.Single().Mastery, Is.LessThan(policy.MasteryThreshold));
        Assert.That(report.TurnsJudged, Is.GreaterThanOrEqualTo(2), "mastery is never assigned on the initial answer alone");
    }

    [Test]
    public async Task Scenario_B_IncorrectInitialAnswerFollowedByStrongReasoning()
    {
        const string wrong = "Plants get their food from the soil through their roots.";
        var clock = new ManualClock(T0);
        var panel = new ScriptedPanel { JudgeScoreFor = p => ScriptedPanel.StudentAnswer(p) == wrong ? 1 : 9 };
        var engine = await Start(panel, new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 4 }, clock);

        await engine.NextQuestionAsync();
        var initial = await engine.SubmitAnswerAsync(Say(wrong, clock));
        var afterError = engine.Session.Mastery["photosynthesis"].Estimate;
        Assert.That(afterError, Is.LessThan(MasteryModel.Prior));

        var reasoning = new[]
        {
            "Actually no. The mass comes from carbon dioxide in the air; light drives building glucose from it, storing energy chemically.",
            "If you kept a plant in the dark it could not build glucose, because the light reactions supply the energy and the soil only gives water and minerals.",
            "So the energy in the sugar was light energy first: chlorophyll captures it and the plant stores it in chemical bonds.",
        };
        var i = 0;
        while (await engine.NextQuestionAsync() is not null && i < reasoning.Length)
            await engine.SubmitAnswerAsync(Say(reasoning[i++], clock));

        var report = engine.Report();
        var concept = report.Concepts.Single();
        Assert.That(engine.Session.Mastery["photosynthesis"].Estimate, Is.GreaterThan(afterError), "later independent reasoning counts");
        // The full body of evidence: the initial error is kept and shown beside the later reasoning.
        Assert.That(engine.Session.Turns[0].Judgment!.OverallScore, Is.EqualTo(initial.Judgment!.OverallScore));
        Assert.That(concept.Evidence.Any(e => e.TurnIndex == 0 && e.Score < 0.35), Is.True, "the initial error stays in the evidence");
        Assert.That(concept.Evidence.Any(e => e.TurnIndex > 0 && e.Score >= 0.8), Is.True, "the strong reasoning is in the evidence");
        Assert.That(report.TurnsJudged, Is.EqualTo(engine.Session.Turns.Count(t => t.Answer is not null)), "every answer is judged against the rubric");
    }

    [Test]
    public async Task Scenario_C_AmbiguousAnswer()
    {
        // 1. In the dialogue: an unclear answer gets a Clarify move, and a draft that would teach the answer is replaced.
        var clock = new ManualClock(T0);
        var panel = new ScriptedPanel
        {
            JudgeScoreFor = _ => 2,
            QuestionText = p => p.Contains("for this move: Clarify", StringComparison.Ordinal)
                ? "Do you mean that light energy becomes chemical energy stored in glucose?"
                : "In your own words, what is photosynthesis?",
            GuardVerdict = p => ScriptedPanel.After(p, "Candidate question:").Contains("chemical energy", StringComparison.OrdinalIgnoreCase) ? "LEAKS" : "SAFE",
        };
        var engine = await Start(panel, new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 6 }, clock);
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Say("It is when the plant does the thing with the sun and it makes stuff.", clock));
        var clarify = await engine.NextQuestionAsync();
        Assert.That(clarify!.Move, Is.EqualTo(MoveKind.Clarify));
        Assert.That(clarify.Question, Does.Not.Contain("chemical").And.Not.Contain("glucose").And.Not.Contain("Do you mean"));
        Assert.That(clarify.Question, Is.EqualTo(QuestionGenerator.Fallback(MoveKind.Clarify, "Photosynthesis", null)));

        // 2. In verification: a clarification that offers an interpretation is never asked; only the student's words are quoted.
        var verifierPanel = new ScriptedPanel
        {
            ClarificationText = _ => "Do you mean that the effect of one independent variable depends on the value of another independent variable?",
        };
        var input = new VerificationInput
        {
            Question = "How do the two variables in the model relate?",
            AnswerText = "I think these variables influence each other.",
            ConceptTitle = "Interaction effects",
        };
        var decision = new VerificationDecision
        {
            NeedsClarification = true, Trigger = ClarificationTrigger.AmbiguousReference, Phrase = "these variables influence each other", Reason = "test",
        };
        var question = await new ResponseVerifier(verifierPanel).WordAsync(input, decision,
            "- The effect of one independent variable depends on the value of another independent variable.", "");
        Assert.That(question.Text, Does.Not.Contain("depends on").And.Not.Contain("independent"));
        Assert.That(question.Text, Does.Contain("these variables influence each other"));
        Assert.That(question.Outcome, Does.StartWith("template:"));
        Assert.That(question.NonLeadingChecked, Is.True);
    }

    [Test]
    public void Scenario_F_AiHumanScoringAgreement()
    {
        var policy = new GradingPolicyRules();
        var result = CombinedScoring.Calculate(
        [
            new ObjectiveScores("Conceptual Understanding", 100, 1, AiScore: 85, HumanScore: 88),
            new ObjectiveScores("Application", 100, 1, AiScore: 78, HumanScore: 80),
        ], policy);
        Assert.That(result.RequiresReconciliation, Is.False);
        Assert.That(result.HasAdditionalReviewFlags, Is.False);
        Assert.That(result.Objectives.Select(o => o.Band), Is.All.EqualTo(DisagreementBand.Standard));
        Assert.That(result.Objectives[0].Combined, Is.EqualTo(86.8m));
        Assert.That(result.Objectives[1].Combined, Is.EqualTo(79.2m));
        Assert.That(result.FinalPercent, Is.EqualTo(83m));
        // Agreement requests final approval; nothing is finalized or published without it.
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.HumanReviewPending, GradeStatus.AwaitingFinalApproval), Is.True);
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.HumanReviewPending, GradeStatus.Finalized), Is.False);
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.HumanReviewPending, GradeStatus.Published), Is.False);
        Assert.That(GradeStatusMachine.IsVisibleToStudent(GradeStatus.AwaitingFinalApproval), Is.False);
    }

    [Test]
    public void Scenario_G_AiHumanScoringDisagreement()
    {
        var result = CombinedScoring.Calculate([new ObjectiveScores("Overall", 100, 1, AiScore: 95, HumanScore: 60)], new GradingPolicyRules());
        var o = result.Objectives.Single();
        Assert.That(o.DifferencePoints, Is.EqualTo(35m));
        Assert.That(o.Band, Is.EqualTo(DisagreementBand.MandatoryReconciliation), "flagged");
        Assert.That(result.RequiresReconciliation, Is.True, "reconciliation starts");
        // No automatic finalization: from human review the grade must go through reconciliation and approval.
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.HumanReviewPending, GradeStatus.ReconciliationRequired), Is.True);
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.ReconciliationRequired, GradeStatus.Finalized), Is.False);
        Assert.That(GradeStatusMachine.CanMove(GradeStatus.ReconciliationRequired, GradeStatus.Published), Is.False);
        Assert.Throws<InvalidStateTransitionException>(() => GradeStatusMachine.Move(GradeStatus.ReconciliationRequired, GradeStatus.Finalized));
        // Only an authorized reviewer resolves it.
        Assert.That(PermissionMatrix.Has(AssessmentRoles.SecondReviewer, Permission.ReconcileDisagreement), Is.True);
        Assert.That(PermissionMatrix.Has(AssessmentRoles.Student, Permission.ReconcileDisagreement), Is.False);
        Assert.That(PermissionMatrix.Has(AssessmentRoles.HumanExaminer, Permission.ReconcileDisagreement), Is.False);
    }

    [Test]
    public async Task Scenario_H_InsufficientEvidence()
    {
        // The turn limit arrives before the evidence settles: middling answers, and one answer the judges split on.
        var clock = new ManualClock(T0);
        var calls = 0;
        var panel = new ScriptedPanel
        {
            FanOut = (kind, _) => kind != ScriptedPanel.Kind.Judge ? null
                : ++calls == 2
                    ? [new PanelReply("alpha", ScriptedPanel.JudgeJson(10)), new PanelReply("beta", ScriptedPanel.JudgeJson(0))]
                    : [new PanelReply("alpha", ScriptedPanel.JudgeJson(5)), new PanelReply("beta", ScriptedPanel.JudgeJson(5))],
        };
        var policy = new SessionPolicy { ConceptIds = ["photosynthesis"], MaxTurns = 2 };
        var engine = await Start(panel, policy, clock);
        while (await engine.NextQuestionAsync() is not null)
            await engine.SubmitAnswerAsync(Say("Plants use sunlight somehow, and that helps them grow over time.", clock));

        Assert.That(engine.Session.EndReason, Is.EqualTo(EndReason.TurnLimit));
        var status = QuestionPlanner.StatusOf(engine.Session, "photosynthesis");
        Assert.That(status, Is.Not.EqualTo(ConceptStatus.Mastered).And.Not.EqualTo(ConceptStatus.Aporia), "no invented certainty either way");
        var report = engine.Report();
        Assert.That(report.Recommendation, Is.Not.EqualTo(Gadfly.Engine.SessionReport.Mastered));
        Assert.That(report.TurnsNeedingReview, Is.GreaterThanOrEqualTo(1), "the split judgment goes to a human");
        Assert.That(report.Concepts.Single().DisputedTurns, Is.Not.Empty);

        // The deterministic rules: level 0 is "insufficient evidence", never a conclusion, and the default
        // procedure for an inconclusive objective is human review.
        Assert.That(MasteryLevels.IsConclusive(MasteryLevel.InsufficientEvidence), Is.False);
        Assert.That(MasteryLevels.Label(MasteryLevel.InsufficientEvidence), Is.EqualTo("Insufficient Evidence"));
        Assert.That(new GradingPolicy().InconclusiveProcedure, Is.EqualTo(InconclusiveProcedure.HumanReview));
    }

    [Test]
    public async Task Scenario_H_TimeRunsOutBeforeEvidenceSettles()
    {
        var clock = new ManualClock(T0);
        var engine = await Start(new ScriptedPanel { JudgeScore = 5 }, new SessionPolicy { ConceptIds = ["photosynthesis"], TimeBudget = TimeSpan.FromMinutes(2) }, clock);
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Say("Light is involved somehow in how plants grow.", clock));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.That(await engine.NextQuestionAsync(), Is.Null);
        Assert.That(engine.Session.EndReason, Is.EqualTo(EndReason.TimeUp));
        Assert.That(engine.Report().Recommendation, Is.Not.EqualTo(Gadfly.Engine.SessionReport.Mastered));
    }

    [Test]
    public async Task Scenario_I_AiAccidentallyRevealsTheAnswer()
    {
        // Every draft reveals the rubric point: the guard catches it, a redraft is tried, and a plain replacement is used.
        var clock = new ManualClock(T0);
        var panel = new ScriptedPanel
        {
            QuestionText = _ => "Since light energy becomes chemical energy in glucose, why does that matter?",
            GuardVerdict = p => ScriptedPanel.After(p, "Candidate question:").Contains("chemical energy", StringComparison.OrdinalIgnoreCase) ? "LEAKS" : "SAFE",
        };
        var engine = await Start(panel, new SessionPolicy { ConceptIds = ["photosynthesis"] }, clock);
        var q = await engine.NextQuestionAsync();
        Assert.That(q!.Question, Does.Not.Contain("chemical energy"), "the leaking question is never asked");
        Assert.That(q.Question, Is.EqualTo(QuestionGenerator.Fallback(q.Move, "Photosynthesis", null)), "a replacement question is supported");
        Assert.That(panel.Count(ScriptedPanel.Kind.Guard), Is.EqualTo(2), "checked, redrafted, checked again");

        // A clarification that would hand over the answer is replaced the same way.
        var verifier = new ResponseVerifier(new ScriptedPanel
        {
            ClarificationText = _ => "When you say it makes stuff, what does the chemical energy in glucose do?",
            GuardVerdict = _ => "LEAKS",
        });
        var worded = await verifier.WordAsync(
            new VerificationInput { Question = "What is photosynthesis?", AnswerText = "The plant makes stuff with the sun.", ConceptTitle = "Photosynthesis" },
            new VerificationDecision { NeedsClarification = true, Trigger = ClarificationTrigger.MultipleInterpretations, Phrase = "makes stuff", Reason = "t" },
            "- Light energy becomes chemical energy in glucose", "");
        Assert.That(worded.Text, Does.Not.Contain("chemical").And.Not.Contain("glucose"));
        Assert.That(worded.Outcome, Does.StartWith("template:"));
    }

    [Test]
    public void Scenario_I_ContaminatedEvidence_IsNotTreatedAsIndependentMastery()
    {
        // A question that revealed the answer is flagged (by the analysis, the guard or a reviewer): every answer on
        // that objective from then on is contaminated, and only a leak-resistant replacement can count again.
        var s = Session();
        s.Judged("photosynthesis", MoveKind.Elicit, 0.95, 0.65);
        s.Judged("photosynthesis", MoveKind.Probe, 0.95, 0.75);
        s.Judged("photosynthesis", MoveKind.CausalReasoning, 0.95, 0.85);
        s.FlagLeak(0, new LeakFlag(LeakSource.Reviewer, "The opening question stated the definition.", T0));
        var objective = ExamObjectives.Implicit("photosynthesis", "Photosynthesis");
        var a = ObjectiveAssessor.Assess(s, objective);
        Assert.That(a.ContaminatedAnswers, Is.EqualTo(3));
        Assert.That(a.IndependentAnswers, Is.Zero, "contaminated evidence is never independent");
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence), "no mastery from contaminated answers");
        Assert.That(a.Explanation, Does.Contain("excluded"));

        // The planner asks a replacement question that needs reasoning the leak did not give away...
        var d = new QuestionPlanner([new PlannerConcept("photosynthesis")]).Next(s, T0.AddMinutes(1));
        Assert.That(d.IsReplacement, Is.True);
        Assert.That(ObjectiveAssessor.LeakResistantMoves, Does.Contain(d.Move));
        // ...whose answer counts again for the dimensions a leak can't supply.
        s.Ask("photosynthesis", d.Move, "Replacement?", T0.AddMinutes(1), isReplacement: true);
        s.RecordAnswer(new Answer { Text = "x", CompletedAt = T0.AddMinutes(2) });
        s.RecordJudgment(Judgment(0.9), 0.85);
        var replacement = s.Turns[^1];
        Assert.That(ObjectiveAssessor.IsContaminated(s, replacement, objective.Key, AssessmentDimension.Application), Is.False);
        Assert.That(ObjectiveAssessor.IsContaminated(s, replacement, objective.Key, AssessmentDimension.FactualKnowledge), Is.True);
        Assert.That(ObjectiveAssessor.Assess(s, objective).IndependentAnswers, Is.EqualTo(1));
    }
}

/// <summary>Scenarios D, E and J, whose evidence is the persisted record (LocalDB).</summary>
[TestFixture]
[Category("Scenario")]
public class MasterPlanPersistenceScenarioTests : ServerTest
{
    private async Task<(Guid Org, Guid SessionId, Guid TurnId, Guid Student)> Session()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync();
        return (w.Org, sessionId, turnId, w.StudentId);
    }

    [Test]
    public async Task Scenario_D_TranscriptError()
    {
        const string recorded = "The coeficient changes depending on another varible.";
        const string said = "The coefficient changes depending on another variable.";
        var (org, sessionId, turnId, student) = await Session();
        Guid responseId;
        await using (var db = OrgDb(org))
        {
            var ledger = new ResponseLedger(db, Clock);
            var response = await ledger.CaptureAsync(org, sessionId, turnId, student,
                new Answer { Text = recorded, Mode = "voice", CompletedAt = Clock.GetUtcNow(), Words = [new TimedWord("coeficient", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.4)] });
            await db.SaveChangesAsync();
            responseId = response.Id;

            // The student can correct the transcript: a transcription fix, within the correction rules.
            var check = CorrectionRules.Check(recorded, said, voice: true);
            Assert.That(check.Allowed, Is.True, check.Reason);
            ledger.MovePath(response, "Low speech confidence.", null, "System",
                VerificationState.VerificationRequired, VerificationState.AwaitingStudentConfirmation);
            ledger.AddVersion(response, said, CorrectionKind.Transcription, "student", student);
            ledger.Move(response, VerificationState.Corrected, "The student corrected the transcript.", student, OrgRoles.Student);
            await db.SaveChangesAsync();
        }

        await using var check2 = SystemDb();
        var stored = await check2.StudentResponses.AsNoTracking().Include(r => r.Versions).SingleAsync(r => r.Id == responseId);
        Assert.That(stored.OriginalText, Is.EqualTo(recorded), "the original version remains preserved");
        Assert.That(stored.VerifiedText, Is.EqualTo(said), "scoring uses the verified evidence");
        Assert.That(stored.VerificationState, Is.EqualTo(VerificationState.Corrected));
        Assert.That(VerificationMachine.IsVerified(stored.VerificationState), Is.True);
        Assert.That(stored.Versions.OrderBy(v => v.Version).Select(v => (v.Text, v.CorrectionKind)),
            Is.EqualTo(new[] { (recorded, (CorrectionKind?)null), (said, (CorrectionKind?)CorrectionKind.Transcription) }));
        var history = await check2.ResponseVerifications.AsNoTracking().Where(v => v.ResponseId == responseId).Select(v => v.To).ToListAsync();
        Assert.That(history, Is.EquivalentTo(new[] { VerificationState.VerificationRequired, VerificationState.AwaitingStudentConfirmation, VerificationState.Corrected }));
        var actions = await check2.AuditEvents.AsNoTracking().Where(a => a.EntityId == responseId.ToString()).Select(a => a.Action).ToListAsync();
        Assert.That(actions, Does.Contain("response.captured").And.Contain("response.corrected").And.Contain("response.verification.changed"));

        // Nobody can overwrite the original afterwards.
        await using var db2 = OrgDb(org);
        var again = await db2.StudentResponses.SingleAsync(r => r.Id == responseId);
        again.OriginalText = said;
        Assert.ThrowsAsync<InvalidOperationException>(() => db2.SaveChangesAsync());
    }

    [Test]
    public async Task Scenario_D_ATypoFixCannotRewriteTheAnswer()
    {
        var check = CorrectionRules.Check("The coeficient changes depending on another varible in the model.",
            "Interaction effects occur when one variable's effect depends on another.", voice: false);
        Assert.That(check.Allowed, Is.False, "a rewrite dressed as a typo fix is refused; it must be a revision");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Scenario_E_SubstantiveAnswerRevision()
    {
        const string original = "The coefficient changes depending on another variable in the model.";
        const string revised = "The coefficient changes depending on another factor in the model.";
        var (org, sessionId, turnId, student) = await Session();
        Assert.That(CorrectionRules.Check(original, revised, voice: false).Allowed, Is.False, "changing a word's meaning is not a transcription fix");
        Guid originalId, revisionId;
        await using (var db = OrgDb(org))
        {
            var ledger = new ResponseLedger(db, Clock);
            var first = await ledger.CaptureAsync(org, sessionId, turnId, student, new Answer { Text = original, CompletedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            Clock.Advance(TimeSpan.FromSeconds(30));
            var second = await ledger.CaptureAsync(org, sessionId, turnId, student, new Answer { Text = revised, CompletedAt = Clock.GetUtcNow() },
                ResponseKind.SubstantiveRevision, first.Id);
            await db.SaveChangesAsync();
            (originalId, revisionId) = (first.Id, second.Id);
        }
        await using var check = SystemDb();
        var rows = await check.StudentResponses.AsNoTracking().Where(r => r.SessionId == sessionId).OrderBy(r => r.Sequence).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2), "the revision is recorded separately");
        Assert.That(rows[0].Id, Is.EqualTo(originalId));
        Assert.That(rows[0].OriginalText, Is.EqualTo(original), "the earlier evidence is not silently replaced");
        Assert.That(rows[0].Kind, Is.EqualTo(ResponseKind.Original));
        Assert.That(rows[1].Id, Is.EqualTo(revisionId));
        Assert.That(rows[1].Kind, Is.EqualTo(ResponseKind.SubstantiveRevision));
        Assert.That(rows[1].RevisesResponseId, Is.EqualTo(originalId));
        var revisedAudit = await check.AuditEvents.AsNoTracking().SingleAsync(a => a.EntityId == revisionId.ToString() && a.Action == "response.revised");
        Assert.That(revisedAudit.Reason, Does.Contain("change their answer"));
        // The configured examination rule is stated: the original stays on record, the change is a new answer.
        Assert.That(ResponseLedger.RevisionRule(AssessmentMode.Examination), Does.Contain("original answer stays on record"));
        Assert.That(ResponseLedger.RevisionRule(AssessmentMode.Learning), Does.Contain("practice"));
    }

    [Test]
    public async Task Scenario_J_UnauthorizedGradeModification()
    {
        var (org, sessionId, _, student) = await Session();
        await using (var db = OrgDb(org))
        {
            db.FinalGrades.Add(new FinalGrade { OrganizationId = org, SessionId = sessionId, Status = GradeStatus.Published, FinalPercent = 72m, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }

        foreach (var role in new[] { OrgRoles.Student, OrgRoles.Examiner, OrgRoles.SecondReviewer })
        {
            var user = role == OrgRoles.Student ? student : Guid.NewGuid();
            await using var db = Db(Member(org, user, role));
            var guard = new AssessmentGuard(db, new FixedTenant(Member(org, user, role)), Clock);
            var allowed = await guard.RequireAsync(Permission.AmendGrade, "Change the final score to 95", nameof(FinalGrade), sessionId.ToString());
            Assert.That(allowed, Is.False, $"{role} must not change a final score");
        }

        await using var check = SystemDb();
        // The action is rejected: the grade is unchanged.
        Assert.That((await check.FinalGrades.AsNoTracking().SingleAsync(g => g.SessionId == sessionId)).FinalPercent, Is.EqualTo(72m));
        // And logged: one access.denied event per attempt, naming the actor's role and the permission.
        var denied = await check.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.Action == "access.denied" && a.EntityId == sessionId.ToString()).ToListAsync();
        Assert.That(denied.Select(d => d.ActorRole), Is.EquivalentTo(new[] { OrgRoles.Student, OrgRoles.Examiner, OrgRoles.SecondReviewer }));
        Assert.That(denied.All(d => d.Reason!.Contains(nameof(Permission.AmendGrade))), Is.True);
        Assert.That(AssessmentGuard.Denied(Permission.AmendGrade), Does.Contain("recorded"));

        // An authorized role passes the same check (and writes no denial).
        await using var teacherDb = Db(Member(org, Guid.NewGuid(), OrgRoles.Instructor));
        var teacher = new AssessmentGuard(teacherDb, new FixedTenant(Member(org, Guid.NewGuid(), OrgRoles.Instructor)), Clock);
        Assert.That(await teacher.RequireAsync(Permission.AmendGrade, "Amend after appeal", nameof(FinalGrade), sessionId.ToString()), Is.True);
    }

    [Test]
    public async Task Scenario_J_ACallerWithoutAnOrganization_IsRefusedAndLogged()
    {
        var (_, sessionId, _, _) = await Session();
        await using var db = Db(TenantSnapshot.Nobody);
        var guard = new AssessmentGuard(db, new FixedTenant(TenantSnapshot.Nobody), Clock);
        Assert.That(guard.Has(Permission.AmendGrade), Is.False);
        Assert.That(await guard.RequireAsync(Permission.AmendGrade, "anonymous change", nameof(FinalGrade), sessionId.ToString()), Is.False);
        await using var check = SystemDb();
        Assert.That(await check.AuditEvents.AnyAsync(a => a.Action == "access.denied" && a.EntityId == sessionId.ToString() && a.OrganizationId == null), Is.True);
    }

    [Test]
    public async Task Scenario_J_AnotherOrganizationCannotTouchTheGrade()
    {
        var (org, sessionId, _, _) = await Session();
        await using (var db = OrgDb(org))
        {
            db.FinalGrades.Add(new FinalGrade { OrganizationId = org, SessionId = sessionId, Status = GradeStatus.Published, FinalPercent = 72m, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var intruder = await Seed.OrganizationAsync();
        await using var other = Db(Member(intruder, Guid.NewGuid(), OrgRoles.Owner));
        Assert.That(await other.FinalGrades.AnyAsync(g => g.SessionId == sessionId), Is.False, "not even visible");
        var forged = new FinalGrade { OrganizationId = org, SessionId = sessionId, Status = GradeStatus.Amended, FinalPercent = 99m };
        other.FinalGrades.Attach(forged);
        other.Entry(forged).Property(g => g.FinalPercent).IsModified = true;
        Assert.ThrowsAsync<InvalidOperationException>(() => other.SaveChangesAsync());
    }
}
