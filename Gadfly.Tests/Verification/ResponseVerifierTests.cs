using Gadfly.Engine;
using Gadfly.Sources.Verification;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Verification;

/// <summary>The response verification engine (GAD-T87): deterministic checks, the panel decision, neutral wording.</summary>
[TestFixture]
public sealed class ResponseVerifierTests
{
    private static VerificationInput Input(string answer, string modality = "text", IReadOnlyList<TimedWord>? words = null) => new()
    {
        Question = "How do the two variables in the model relate?",
        AnswerText = answer,
        Modality = modality,
        Words = words ?? [],
        ConceptTitle = "Interaction effects",
        Grounding = "An interaction effect occurs when the effect of one independent variable depends on the level of another.",
    };

    private const string Rubric = "- The effect of one independent variable depends on the value of another independent variable.";

    // ------------------------------------------------------------------ deterministic checks

    [TestCase("The variables affect each other because", true)]
    [TestCase("It changes when the other one is...", true)]
    [TestCase("It depends on the other variable —", true)]
    [TestCase("The variables affect each other.", false)]
    [TestCase("They interact (sometimes)", false)]
    [TestCase("They interact", false)]
    public void IsIncomplete_detects_answers_that_stop_partway(string text, bool expected) =>
        Assert.That(ResponseVerifier.IsIncomplete(text), Is.EqualTo(expected));

    [Test]
    public void Hints_find_an_explicit_request_to_clarify()
    {
        var hints = ResponseVerifier.Hints(Input("They interact. Can I clarify what I said?"));
        Assert.That(hints.Select(h => h.Trigger), Does.Contain(ClarificationTrigger.StudentRequest));
    }

    [Test]
    public void Hints_ignore_aporia_and_empty_answers()
    {
        Assert.That(ResponseVerifier.Hints(Input("aporia")), Is.Empty);
        Assert.That(ResponseVerifier.Hints(Input("   ")), Is.Empty);
    }

    [Test]
    public void Hints_report_low_speech_confidence_only_for_voice()
    {
        var words = new[] { new TimedWord("the", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.95), new TimedWord("eigenvalue", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0.3) };
        var voice = ResponseVerifier.Hints(Input("the eigenvalue", "voice", words));
        var text = ResponseVerifier.Hints(Input("the eigenvalue", "text", words));
        Assert.Multiple(() =>
        {
            Assert.That(voice.Single(h => h.Trigger == ClarificationTrigger.LowSpeechConfidence).Phrase, Is.EqualTo("eigenvalue"));
            Assert.That(text.Any(h => h.Trigger == ClarificationTrigger.LowSpeechConfidence), Is.False);
        });
    }

    // ------------------------------------------------------------------ panel decision

    [Test]
    public async Task Aporia_never_asks_and_calls_no_panel()
    {
        var panel = new FakeSocraticVotingClient([]);
        var d = await new ResponseVerifier(panel).DecideAsync(Input("aporia"));
        Assert.Multiple(() =>
        {
            Assert.That(d.NeedsClarification, Is.False);
            Assert.That(panel.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task A_student_request_always_asks_without_the_panel()
    {
        var panel = new FakeSocraticVotingClient([]);
        var d = await new ResponseVerifier(panel).DecideAsync(Input("They interact. Let me clarify that."));
        Assert.Multiple(() =>
        {
            Assert.That(d.NeedsClarification, Is.True);
            Assert.That(d.Trigger, Is.EqualTo(ClarificationTrigger.StudentRequest));
            Assert.That(panel.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task A_material_ambiguity_vote_asks_and_keeps_the_students_own_phrase()
    {
        var panel = new FakeSocraticVotingClient([
            """{"clarify": true, "trigger": "multiple-interpretations", "phrase": "affect each other", "material": true, "reason": "Could mean correlation or interaction."}""",
        ]);
        var d = await new ResponseVerifier(panel).DecideAsync(Input("I think these variables Affect Each Other in the model."));
        Assert.Multiple(() =>
        {
            Assert.That(d.NeedsClarification, Is.True);
            Assert.That(d.Trigger, Is.EqualTo(ClarificationTrigger.MultipleInterpretations));
            Assert.That(d.Phrase, Is.EqualTo("Affect Each Other"), "the phrase is copied from the answer in the student's casing");
            Assert.That(d.Voters, Is.EqualTo(1));
            Assert.That(panel.Calls.Single().Context, Does.Contain("<student_text>"), "student text is fenced");
        });
    }

    [Test]
    public async Task A_non_material_vote_does_not_interrupt()
    {
        var panel = new FakeSocraticVotingClient([
            """{"clarify": true, "trigger": "ambiguous-reference", "phrase": "it", "material": false, "reason": "Either reading grades the same."}""",
        ]);
        var d = await new ResponseVerifier(panel).DecideAsync(Input("It goes up when the other one goes up."));
        Assert.That(d.NeedsClarification, Is.False);
    }

    [Test]
    public async Task A_phrase_the_student_never_said_is_dropped()
    {
        var panel = new FakeSocraticVotingClient([
            """{"clarify": true, "trigger": "ambiguous-reference", "phrase": "the interaction term", "material": true, "reason": "x"}""",
        ]);
        var d = await new ResponseVerifier(panel).DecideAsync(Input("It goes up when the other one goes up."));
        Assert.Multiple(() =>
        {
            Assert.That(d.NeedsClarification, Is.True);
            Assert.That(d.Phrase, Is.Null);
        });
    }

    [Test]
    public async Task Malformed_votes_fall_back_to_the_deterministic_checks()
    {
        var incomplete = await new ResponseVerifier(new FakeSocraticVotingClient(["not json"])).DecideAsync(Input("The effect changes because"));
        var complete = await new ResponseVerifier(new FakeSocraticVotingClient(["{\"clarify\": \"maybe\"}"])).DecideAsync(Input("The effect changes with the other variable."));
        Assert.Multiple(() =>
        {
            Assert.That(incomplete.NeedsClarification, Is.True);
            Assert.That(incomplete.Trigger, Is.EqualTo(ClarificationTrigger.IncompleteSentence));
            Assert.That(complete.NeedsClarification, Is.False);
        });
    }

    [Test]
    public void ParseVote_requires_a_boolean_clarify_and_a_known_trigger()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResponseVerifier.ParseVote("{\"clarify\": \"yes\"}", "a"), Is.Null);
            Assert.That(ResponseVerifier.ParseVote("{\"clarify\": true, \"trigger\": \"made-up\", \"material\": true}", "a")!.Trigger,
                Is.EqualTo(ClarificationTrigger.None));
        });
    }

    // ------------------------------------------------------------------ neutral wording (Part 4.4)

    [Test]
    public void IntroducesNewTerms_flags_technical_content_the_student_did_not_say()
    {
        const string answer = "The two variables affect each other.";
        var leading = ResponseVerifier.IntroducesNewTerms(
            "Do you mean that the effect of one independent variable depends on the value of another independent variable?", answer, "How do they relate?");
        var neutral = ResponseVerifier.IntroducesNewTerms(
            "When you say the variables affect each other, what relationship are you describing?", answer, "How do they relate?");
        Assert.Multiple(() =>
        {
            Assert.That(leading, Does.Contain("independent").And.Contain("depends"));
            Assert.That(neutral, Is.Empty);
        });
    }

    [Test]
    public async Task A_neutral_draft_that_passes_both_guards_is_used()
    {
        var panel = new FakeSocraticVotingClient([
            "When you say the variables affect each other, what relationship are you describing?",
            "SAFE",
        ]);
        var q = await new ResponseVerifier(panel).WordAsync(Input("The two variables affect each other."),
            new VerificationDecision { NeedsClarification = true, Trigger = ClarificationTrigger.MultipleInterpretations, Phrase = "affect each other", Reason = "r" },
            Rubric, "");
        Assert.Multiple(() =>
        {
            Assert.That(q.Text, Is.EqualTo("When you say the variables affect each other, what relationship are you describing?"));
            Assert.That(q.NonLeadingChecked, Is.True);
            Assert.That(q.Outcome, Is.EqualTo("drafted"));
        });
    }

    [Test]
    public async Task The_prohibited_example_from_the_master_plan_falls_back_to_the_template()
    {
        var panel = new FakeSocraticVotingClient([
            "Do you mean that the effect of one independent variable depends on the value of another independent variable?",
        ]);
        var q = await new ResponseVerifier(panel).WordAsync(Input("The two variables affect each other."),
            new VerificationDecision { NeedsClarification = true, Trigger = ClarificationTrigger.MultipleInterpretations, Phrase = "affect each other", Reason = "r" },
            Rubric, "");
        Assert.Multiple(() =>
        {
            Assert.That(q.Text, Is.EqualTo("When you say \"affect each other\", what do you mean?"));
            Assert.That(q.Outcome, Does.StartWith("template"));
        });
    }

    [Test]
    public async Task A_draft_the_leak_guard_rejects_falls_back_to_the_template()
    {
        var panel = new FakeSocraticVotingClient([
            "When you say the variables affect each other, what relationship are you describing?",
            "LEAKS",
        ]);
        var q = await new ResponseVerifier(panel).WordAsync(Input("The two variables affect each other."),
            new VerificationDecision { NeedsClarification = true, Trigger = ClarificationTrigger.AmbiguousReference, Phrase = "affect each other", Reason = "r" },
            Rubric, "");
        Assert.That(q.Outcome, Does.Contain("leak guard"));
        Assert.That(q.Text, Is.EqualTo(ResponseVerifier.Template(ClarificationTrigger.AmbiguousReference, "affect each other")));
    }

    [Test]
    public void Templates_only_quote_the_student()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResponseVerifier.Template(ClarificationTrigger.IncompleteSentence, "changes because"),
                Is.EqualTo("Your answer seems to stop after \"changes because\". How would you finish that thought?"));
            Assert.That(ResponseVerifier.Template(ClarificationTrigger.MultipleInterpretations, null),
                Is.EqualTo("Could you say a little more about what you meant, in your own words?"));
        });
    }

    [Test]
    public void The_exam_prompt_set_carries_the_verification_prompts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExamPrompts.Set[ExamPrompts.VerificationKey], Does.Contain(ExamPrompts.StudentTextRule.Trim()));
            Assert.That(ExamPrompts.Set[ExamPrompts.ClarificationKey], Does.Contain("Do you mean ...?\" is forbidden"));
            Assert.That(ExamPrompts.Version, Is.GreaterThanOrEqualTo(3));
        });
    }
}
