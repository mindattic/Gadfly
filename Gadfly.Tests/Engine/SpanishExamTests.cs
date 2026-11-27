using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;
using Gadfly.Sources.Verification;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Exams in Spanish (GAD-T610), mirroring <see cref="KoreanExamTests"/>: the exam-es prompt set, the Spanish heuristics (sentence
/// splitting with ¿ and ¡, unfinished-sentence connectives, "no lo sé", hedges, vague words, reasons, rephrase requests, fillers), and
/// a Spanish session end to end on the mock panel. English text keeps exactly the English rules.
/// </summary>
[TestFixture]
public class SpanishExamTests
{
    private static readonly AttemptLanguage Spanish = new(ExamLanguage.Spanish, ExamLanguage.Spanish);
    private static readonly AttemptLanguage EnglishBySpanishSpeaker = new(ExamLanguage.English, ExamLanguage.Spanish);

    // ------------------------------------------------------------------ prompts

    [Test]
    public void ASpanishAttempt_HasItsOwnPromptSet_ThatOnlyAppendsToTheEnglishWording()
    {
        var es = ExamPromptLanguages.For(ExamPrompts.Set, Spanish);
        Assert.That(es.Id, Is.EqualTo("exam-es"));
        Assert.That(es.Version, Is.EqualTo(ExamPrompts.Set.Version));
        Assert.That(es.Sha256, Is.Not.EqualTo(ExamPrompts.Set.Sha256).And.Not.EqualTo(ExamPromptLanguages.For(ExamPrompts.Set, new AttemptLanguage("ko", "ko")).Sha256));
        foreach (var (key, english) in ExamPrompts.Set.Prompts)
            Assert.That(es[key], Does.StartWith(english), key);
        Assert.That(es[ExamPrompts.QuestionKey], Does.Contain("Exam language: es (Spanish)").And.Contain("usted").And.Contain("Latin American"));
        Assert.That(es[ExamPrompts.JudgeKey], Does.Contain("English gloss"));
        Assert.That(es[ExamPrompts.AporiaKey], Does.Contain("no lo sé").And.Contain("ni idea"));
        Assert.That(es[ExamPrompts.ConceptMapKey], Is.EqualTo(ExamPrompts.Set[ExamPrompts.ConceptMapKey]));
        Assert.That(ExamPromptLanguages.For(ExamPrompts.Set, new AttemptLanguage("es", "en")).Id, Is.EqualTo("exam-es-plain"));
    }

    [Test]
    public void ASpanishSpeakerInAnEnglishExam_GetsThePlainExaminer_AndTheJudgesAreNeverTold()
    {
        var plain = ExamPromptLanguages.For(ExamPrompts.Set, EnglishBySpanishSpeaker);
        Assert.That(plain.Id, Is.EqualTo("exam-en-plain"));
        foreach (var text in plain.Prompts.Values)
            Assert.That(text, Does.Not.Contain("Spanish").And.Not.Contain("first language"));
    }

    // ------------------------------------------------------------------ heuristics

    [Test]
    public void SentenceSplitting_StartsASentenceAtAnInvertedMark_AndKeepsSpanishAbbreviations()
    {
        var es = new SentenceSplitter();
        var parts = es.Add("Muy bien.¿Por qué ocurre la fotosíntesis? ¡Explíquelo, por favor! Por ej. la Dra. López lo midió. ")
            .Concat([es.Flush()]).Where(s => s.Length > 0).ToList();
        Assert.That(parts, Is.EqualTo(new[] { "Muy bien.", "¿Por qué ocurre la fotosíntesis?", "¡Explíquelo, por favor!", "Por ej. la Dra. López lo midió." }));

        var en = new SentenceSplitter();
        Assert.That(en.Add("Dr. Smith measured 3.5 grams. Why?Then stop. ").ToList(), Is.EqualTo(new[] { "Dr. Smith measured 3.5 grams.", "Why?Then stop." }));
    }

    [TestCase("La fotosíntesis convierte la luz en energía química.", false)]
    [TestCase("La planta usa la luz y", true)]
    [TestCase("Produce glucosa pero", true)]
    [TestCase("Es importante porque", true)]
    [TestCase("La clorofila absorbe la luz, o sea", true)]
    [TestCase("Creo que la energía viene del sol, entonces", true)]
    [TestCase("El cloroplasto es donde ocurre, este...", true)]
    [TestCase("Las plantas producen oxígeno,", true)]
    [TestCase("¿Por qué?", false)]
    [TestCase("La planta produce glucosa", null)]
    public void SpanishConnectives_DecideWhetherAnAnswerStoppedMidSentence(string answer, bool? incomplete)
    {
        Assert.That(LanguageText.IsIncompleteSpanish(answer), Is.EqualTo(incomplete));
        Assert.That(ResponseVerifier.IsIncomplete(answer), Is.EqualTo(incomplete ?? false));
    }

    [Test]
    public void EnglishText_NeverLooksSpanish_AndKeepsTheEnglishRules()
    {
        Assert.That(LanguageText.LooksSpanish("light becomes energy because"), Is.False);
        Assert.That(LanguageText.LooksSpanish("I updated my résumé at the café,"), Is.False, "an English loanword with an accent");
        Assert.That(LanguageText.LooksSpanish("x and y are variables"), Is.False);
        Assert.That(LanguageText.IsIncompleteSpanish("light becomes energy because"), Is.Null);
        Assert.That(ResponseVerifier.IsIncomplete("light becomes energy because"), Is.True);
        Assert.That(ResponseVerifier.IsIncomplete("I updated my résumé at the café,"), Is.False, "as before");
        Assert.That(LanguageText.LooksSpanish("que la planta"), Is.True);
        Assert.That(LanguageText.LooksSpanish("no sé"), Is.True);
    }

    [TestCase("No lo sé.", true)]
    [TestCase("no sé", true)]
    [TestCase("La verdad, no me acuerdo", true)]
    [TestCase("Ni idea, lo siento", true)]
    [TestCase("No tengo idea", true)]
    [TestCase("no lo se", true)]
    [TestCase("No se puede producir glucosa sin luz", false)]
    [TestCase("La fotosíntesis convierte la luz en energía química", false)]
    public void SpanishIDontKnow_IsRecognized(string answer, bool dontKnow)
    {
        Assert.That(LanguageText.SaysDontKnowSpanish(answer), Is.EqualTo(dontKnow));
        Assert.That(MockVotingClient.SaysDontKnow(answer), Is.EqualTo(dontKnow));
    }

    [Test]
    public void PressureCues_ReadSpanishHedgesVagueWordsAndReasons_WithoutChangingEnglish()
    {
        const string hedged = "Creo que tal vez es por la luz, o algo así.";
        Assert.That(AnswerDelivery.HedgesIn(hedged), Does.Contain("creo que").And.Contain("tal vez"));
        Assert.That(AnswerDelivery.VagueIn(hedged), Does.Contain("o algo así"));
        Assert.That(AnswerDelivery.HasReasonOrExample("Se produce glucosa porque la clorofila absorbe la luz"), Is.True);
        Assert.That(AnswerDelivery.HasReasonOrExample("La planta produce glucosa"), Is.False);
        Assert.That(AnswerDelivery.HedgesIn("I think maybe it is light"), Is.EqualTo(new[] { "i think", "maybe" }));
    }

    [Test]
    public void RephraseAndClarifyRequests_AreHeardInSpanish()
    {
        Assert.That(LanguageText.AsksToRephrase("¿Puede repetir la pregunta?"), Is.True);
        Assert.That(LanguageText.AsksToRephrase("Perdón, no entiendo la pregunta"), Is.True);
        Assert.That(LanguageText.AsksToRephrase("¿Me lo puede decir de otra manera?"), Is.True);
        Assert.That(LanguageText.AsksToRephrase("La planta produce glucosa"), Is.False);
        var hints = ResponseVerifier.Hints(new VerificationInput { AnswerText = "Quiero aclarar lo que dije antes sobre la clorofila", Question = "q", ConceptTitle = "c" });
        Assert.That(hints.Select(h => h.Trigger), Does.Contain(ClarificationTrigger.StudentRequest));
    }

    [Test]
    public void TheJudgesContentView_DropsSpanishFillers_OnlyInSpanishText()
    {
        Assert.That(LanguageText.ContentView("Eh, la la planta usa, em, la energía."), Is.EqualTo("la planta usa, la energía."));
        Assert.That(LanguageText.ContentView("Plants make glucose, eh."), Is.EqualTo("Plants make glucose, eh."), "English unchanged");
    }

    [Test]
    public void SpanishTemplatesAndFallbacks_UseUsted_AndOnlyQuoteTheStudent()
    {
        var t = ResponseVerifier.Template(ClarificationTrigger.AmbiguousReference, "esa cosa", ExamLanguage.Spanish);
        Assert.That(t, Does.Contain("esa cosa").And.Contain("¿qué quiere decir?"));
        Assert.That(ResponseVerifier.Template(ClarificationTrigger.IncompleteSentence, "y luego", "es"), Does.Contain("y luego"));
        Assert.That(UncertainWords.Question("clorofil", "es"), Does.Contain("clorofil").And.StartWith("Quiero asegurarme"));
        foreach (var move in Enum.GetValues<MoveKind>())
        {
            var q = QuestionGenerator.Fallback(move, "Fotosíntesis", null, ExamLanguage.Spanish);
            Assert.That(LanguageText.LooksSpanish(q), Is.True, move.ToString());
            Assert.That(q, Does.Contain("?"), move.ToString());
            Assert.That((" " + q.ToLowerInvariant() + " ").Contains(" tú ", StringComparison.Ordinal), Is.False, "usted, never tú");
        }
        Assert.That(QuestionGenerator.Fallback(MoveKind.Elicit, "A", null), Is.EqualTo("In your own words, what is A?"));
        Assert.That(QuestionGenerator.IsIntrospective("¿Cómo llegó a esa conclusión?"), Is.True);
        Assert.That(QuestionGenerator.IsIntrospective("¿Por qué piensa que eso es cierto?"), Is.False);
    }

    // ------------------------------------------------------------------ a Spanish session on the mock panel

    private static async Task<SourceExamEngine> StartAsync(AttemptLanguage language)
    {
        var engine = await SourceExamEngine.StartAsync(new MockVotingClient(), EngineKit.Biology().ForLanguage(language),
            new SessionPolicy { ConceptIds = ["photosynthesis"] }, new ManualClock(EngineKit.T0));
        engine.AssessTextStyle = false;
        engine.LiveTurnModel = new MockLiveTurnModel();
        return engine;
    }

    private static Answer Said(string text, int second = 30) => new() { Text = text, CompletedAt = EngineKit.T0.AddSeconds(second) };

    [Test]
    public async Task ASpanishSession_AsksInSpanish_JudgesSpanishAnswers_AndNeedsAHumanForEveryScore()
    {
        var engine = await StartAsync(Spanish);
        Assert.That(engine.Session.PromptSetSha256, Is.EqualTo(ExamPromptLanguages.For(ExamPrompts.Set, Spanish).Sha256));

        var first = await engine.NextQuestionAsync();
        Assert.That(first, Is.Not.Null);
        Assert.That(LanguageText.LooksSpanish(first!.Question), Is.True, first.Question);

        var judged = await engine.SubmitAnswerAsync(Said("La fotosíntesis es el proceso en el que la planta usa la energía de la luz para producir glucosa a partir de dióxido de carbono y agua, porque la clorofila absorbe la luz."));
        Assert.That(judged.Judgment, Is.Not.Null);
        Assert.That(judged.Judgment!.OverallScore, Is.GreaterThan(0));
        Assert.That(judged.Judgment.NeedsHumanReview, Is.True, "cross-lingual judging is not yet calibrated");
        Assert.That(judged.Judgment.Summary, Does.StartWith(PanelAnswerJudge.CrossLingualNote));

        var second = await engine.NextQuestionAsync();
        Assert.That(second, Is.Not.Null);
        Assert.That(LanguageText.LooksSpanish(second!.Question), Is.True, second.Question);
        Assert.That(second.Question, Is.Not.EqualTo(first.Question));

        var shrug = await engine.SubmitAnswerAsync(Said("No lo sé, la verdad", 90));
        Assert.That(ObjectiveAssessor.IsAporia(shrug.Judgment!), Is.True, "a Spanish \"I don't know\" is an honest aporia");
    }

    [Test]
    public async Task SayItAnotherWay_RewordsTheOpenQuestionInSpanish()
    {
        var engine = await StartAsync(Spanish);
        var open = await engine.NextQuestionAsync();
        var reworded = await engine.RephraseAsync();
        Assert.That(reworded, Is.Not.Null.And.Not.EqualTo(open!.Question));
        Assert.That(LanguageText.LooksSpanish(reworded), Is.True, reworded);
        Assert.That(engine.Session.OpenTurn!.Rephrasings, Is.EqualTo(new[] { reworded }));
    }
}
