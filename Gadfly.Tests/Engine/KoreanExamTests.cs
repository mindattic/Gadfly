using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;
using Gadfly.Sources.Verification;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Exams in Korean (GAD-T533..T537) and the non-native-speaker adaptations (GAD-T555, grading half): the prompt set per language,
/// the language-aware heuristics, and a Korean session end to end on the mock panel. Above all: an English exam by an
/// English-primary student is exactly what it was.
/// </summary>
[TestFixture]
public class KoreanExamTests
{
    private static readonly AttemptLanguage Korean = new(ExamLanguage.Korean, ExamLanguage.Korean);
    private static readonly AttemptLanguage EnglishByKoreanSpeaker = new(ExamLanguage.English, ExamLanguage.Korean);

    // ------------------------------------------------------------------ prompts

    [Test]
    public void AnEnglishAttempt_RunsOnTheEnglishPromptSetItself()
    {
        Assert.That(ExamPromptLanguages.For(ExamPromptsV3.Set, AttemptLanguage.Default), Is.SameAs(ExamPromptsV3.Set));
        Assert.That(ExamPromptLanguages.For(ExamPromptsV3.Set, new AttemptLanguage("en", "en")), Is.SameAs(ExamPromptsV3.Set));
        Assert.That(ExamPromptLanguages.For(ExamPromptsV3.Set, new AttemptLanguage("EN-us", null)), Is.SameAs(ExamPromptsV3.Set));
        Assert.That(EngineKit.Biology().PromptSet.Sha256, Is.EqualTo(ExamPromptsV3.Set.Sha256));
        Assert.That(EngineKit.Biology().ForLanguage(AttemptLanguage.Default).PromptSet, Is.SameAs(ExamPromptsV3.Set));
    }

    [Test]
    public void AKoreanAttempt_HasItsOwnPromptSet_ThatOnlyAppendsToTheEnglishWording()
    {
        var ko = ExamPromptLanguages.For(ExamPromptsV3.Set, Korean);
        Assert.That(ko.Id, Is.EqualTo("exam-ko"));
        Assert.That(ko.Version, Is.EqualTo(ExamPromptsV3.Set.Version));
        Assert.That(ko.Sha256, Is.Not.EqualTo(ExamPromptsV3.Set.Sha256));
        foreach (var (key, english) in ExamPromptsV3.Set.Prompts)
            Assert.That(ko[key], Does.StartWith(english), key);
        Assert.That(ko[ExamPrompts.QuestionKey], Does.Contain("Exam language: ko").And.Contain("해요체"));
        Assert.That(ko[ExamPrompts.JudgeKey], Does.Contain("English gloss"));
        Assert.That(ko[ExamPrompts.QuestionGuardKey], Does.Contain("LEAKS or SAFE"));
        Assert.That(ko[ExamPrompts.ConceptMapKey], Is.EqualTo(ExamPromptsV3.Set[ExamPrompts.ConceptMapKey]), "the source is not re-mapped per language");
        Assert.That(ExamPromptLanguages.For(ExamPromptsV3.Set, Korean), Is.SameAs(ko), "one set per language, cached");
    }

    [Test]
    public void ANonNativeSpeaker_GetsThePlainLanguageExaminer_ButTheJudgesAreNeverTold()
    {
        var plain = ExamPromptLanguages.For(ExamPromptsV3.Set, EnglishByKoreanSpeaker);
        Assert.That(plain.Id, Is.EqualTo("exam-en-plain"));
        Assert.That(plain[ExamPrompts.QuestionKey], Does.Contain("at most about 20 words").And.Contain("negative questions"));
        foreach (var key in new[] { ExamPrompts.JudgeKey, ExamPrompts.AnalysisKey, ExamPrompts.ClaimsKey, ExamPrompts.AporiaKey, ExamPrompts.VerificationKey })
            Assert.That(plain[key], Is.EqualTo(ExamPromptsV3.Set[key]), key);
        foreach (var text in plain.Prompts.Values)
            Assert.That(text, Does.Not.Contain("Korean").And.Not.Contain("first language").And.Not.Contain("non-native"));
    }

    // ------------------------------------------------------------------ heuristics

    [Test]
    public void SentenceSplitting_HandlesKoreanPunctuation_AndLeavesEnglishAsItWas()
    {
        var ko = new SentenceSplitter();
        var parts = ko.Add("광합성은 무엇인가요?그리고 왜 중요할까요。 ").Concat([ko.Flush()]).Where(s => s.Length > 0).ToList();
        Assert.That(parts, Is.EqualTo(new[] { "광합성은 무엇인가요?", "그리고 왜 중요할까요。" }));

        var en = new SentenceSplitter();
        var english = en.Add("Dr. Smith measured 3.5 grams. Why?Then stop. ").ToList();
        Assert.That(english, Is.EqualTo(new[] { "Dr. Smith measured 3.5 grams.", "Why?Then stop." }));
    }

    [Test]
    public void WordCounts_WeighKoreanWords_AndCountEnglishAsBefore()
    {
        Assert.That(LanguageText.WordCount("Why do you think so?"), Is.EqualTo(5));
        Assert.That(LanguageText.WordCount("왜 그렇게 생각하세요?"), Is.GreaterThanOrEqualTo(QuestionGenerator.FirstPartMinWords));
        Assert.That(QuestionGenerator.Usable("왜 그렇게 생각하세요?"), Is.Not.Empty);
        Assert.That(QuestionGenerator.Usable("Why so?"), Is.Empty);
    }

    [TestCase("광합성은 빛 에너지를 화학 에너지로 바꿉니다", false)]
    [TestCase("빛을 받아서 포도당을 만들어요", false)]
    [TestCase("왜 그런지 설명할 수 있을까요", false)]
    [TestCase("엽록체가 빛을 흡수하고", true)]
    [TestCase("빛이 있으면", true)]
    [TestCase("포도당을 만드는데", true)]
    [TestCase("산소가 나오지만", true)]
    [TestCase("그래서", true)]
    [TestCase("그건 어떻게 합니까", false)]
    [TestCase("그건 잘 모르니까", true)]
    [TestCase("광합성은...", true)]
    public void KoreanEndings_DecideWhetherAnAnswerStoppedMidSentence(string answer, bool incomplete)
    {
        Assert.That(ResponseVerifier.IsIncomplete(answer), Is.EqualTo(incomplete));
        Assert.That(LanguageText.IsIncompleteKorean(answer), Is.EqualTo(incomplete));
    }

    [Test]
    public void TheEnglishIncompletenessRule_IsUnchanged()
    {
        Assert.That(LanguageText.IsIncompleteKorean("light becomes energy because"), Is.Null);
        Assert.That(ResponseVerifier.IsIncomplete("light becomes energy because"), Is.True);
        Assert.That(ResponseVerifier.IsIncomplete("light becomes chemical energy."), Is.False);
    }

    [TestCase("모르겠어요", true)]
    [TestCase("잘 모르겠습니다", true)]
    [TestCase("잘 몰라요", true)]
    [TestCase("기억이 안 나요", true)]
    [TestCase("광합성은 빛을 에너지로 바꾸는 과정이에요", false)]
    public void KoreanIDontKnow_IsRecognized(string answer, bool dontKnow)
    {
        Assert.That(LanguageText.SaysDontKnowKorean(answer), Is.EqualTo(dontKnow));
        Assert.That(MockVotingClient.SaysDontKnow(answer), Is.EqualTo(dontKnow));
    }

    [Test]
    public void PressureCues_ReadKoreanHedgesVagueWordsAndReasons_WithoutChangingEnglish()
    {
        const string hedged = "아마 빛 때문인 것 같아요. 그런 거요.";
        Assert.That(AnswerDelivery.HedgesIn(hedged), Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(AnswerDelivery.VagueIn(hedged), Is.Not.Empty);
        Assert.That(AnswerDelivery.HasReasonOrExample("빛 에너지 때문에 포도당이 만들어져요"), Is.True);
        Assert.That(AnswerDelivery.HasReasonOrExample("포도당이 만들어져요"), Is.False);
        Assert.That(AnswerDelivery.HedgesIn("I think maybe it is light"), Is.EqualTo(new[] { "i think", "maybe" }));
    }

    [Test]
    public void TheJudgesContentView_DropsFillersRepetitionsAndFalseStarts_ByAFixedRule()
    {
        Assert.That(LanguageText.ContentView("Um, the the plant uh uses pho- photosynthesis to make, er, glucose."),
            Is.EqualTo("the plant uses photosynthesis to make, glucose."));
        Assert.That(LanguageText.ContentView("음 광합성은 어 빛을 빛을 이용해요"), Is.EqualTo("광합성은 빛을 이용해요"));
        Assert.That(LanguageText.ContentView("Plants make glucose."), Is.EqualTo("Plants make glucose."));
    }

    [Test]
    public void KoreanClarificationTemplates_OnlyQuoteTheStudent()
    {
        var t = ResponseVerifier.Template(ClarificationTrigger.AmbiguousReference, "그런 거", ExamLanguage.Korean);
        Assert.That(t, Does.Contain("그런 거").And.Contain("무슨 뜻"));
        Assert.That(ResponseVerifier.Template(ClarificationTrigger.AmbiguousReference, "that thing"), Is.EqualTo("When you say \"that thing\", what do you mean?"));
    }

    [Test]
    public void FallbackQuestions_AreKoreanInAKoreanExam_AndUnchangedInEnglish()
    {
        foreach (var move in Enum.GetValues<MoveKind>())
            Assert.That(LanguageText.HasHangul(QuestionGenerator.Fallback(move, "Photosynthesis", null, ExamLanguage.Korean)), Is.True, move.ToString());
        Assert.That(QuestionGenerator.Fallback(MoveKind.Elicit, "A", null), Is.EqualTo("In your own words, what is A?"));
    }

    // ------------------------------------------------------------------ a Korean session on the mock panel

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
    public async Task AKoreanSession_AsksInKorean_JudgesKoreanAnswers_AndNeedsAHumanForEveryScore()
    {
        var engine = await StartAsync(Korean);
        Assert.That(engine.Session.PromptSetSha256, Is.EqualTo(ExamPromptLanguages.For(ExamPromptsV3.Set, Korean).Sha256));

        var first = await engine.NextQuestionAsync();
        Assert.That(first, Is.Not.Null);
        Assert.That(LanguageText.HasHangul(first!.Question), Is.True, first.Question);

        var judged = await engine.SubmitAnswerAsync(Said("광합성은 식물이 빛 에너지를 이용해서 이산화탄소와 물로 포도당을 만드는 과정이에요. 왜냐하면 엽록체가 빛을 흡수하기 때문이에요."));
        Assert.That(judged.Judgment, Is.Not.Null);
        Assert.That(judged.Judgment!.OverallScore, Is.GreaterThan(0));
        Assert.That(judged.Judgment.NeedsHumanReview, Is.True, "cross-lingual judging is not yet calibrated");
        Assert.That(judged.Judgment.Summary, Does.StartWith(PanelAnswerJudge.CrossLingualNote));

        var second = await engine.NextQuestionAsync();
        Assert.That(second, Is.Not.Null);
        Assert.That(LanguageText.HasHangul(second!.Question), Is.True, second.Question);
        Assert.That(second.Question, Is.Not.EqualTo(first.Question));

        var shrug = await engine.SubmitAnswerAsync(Said("잘 모르겠어요", 90));
        Assert.That(ObjectiveAssessor.IsAporia(shrug.Judgment!), Is.True, "a Korean \"I don't know\" is an honest aporia");
    }

    [Test]
    public async Task SayItAnotherWay_RewordsTheOpenQuestion_ThroughTheGuard_AndRecordsIt()
    {
        var engine = await StartAsync(Korean);
        var open = await engine.NextQuestionAsync();
        var reworded = await engine.RephraseAsync();
        Assert.That(reworded, Is.Not.Null.And.Not.EqualTo(open!.Question));
        Assert.That(LanguageText.HasHangul(reworded), Is.True);
        Assert.That(engine.Session.OpenTurn!.Question, Is.EqualTo(open.Question), "the question on record is the one first asked");
        Assert.That(engine.Session.OpenTurn.Rephrasings, Is.EqualTo(new[] { reworded }));
    }

    [Test]
    public async Task AnEnglishSession_IsUnchanged_NoReviewFlagAndTheEnglishPromptSet()
    {
        var engine = await StartAsync(AttemptLanguage.Default);
        Assert.That(engine.Session.PromptSetSha256, Is.EqualTo(ExamPromptsV3.Set.Sha256));
        var first = await engine.NextQuestionAsync();
        Assert.That(LanguageText.HasHangul(first!.Question), Is.False);
        var judged = await engine.SubmitAnswerAsync(Said("Photosynthesis turns light energy into chemical energy stored in glucose, because chlorophyll absorbs light."));
        Assert.That(judged.Judgment!.Summary, Does.Not.StartWith(PanelAnswerJudge.CrossLingualNote));
    }
}
