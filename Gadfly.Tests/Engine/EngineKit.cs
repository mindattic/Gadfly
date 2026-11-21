using Gadfly.Engine;
using Gadfly.Sources;

namespace Gadfly.Tests.Engine;

/// <summary>Shared builders for engine tests: sessions, judged turns, a small source-material subject.</summary>
internal static class EngineKit
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    public static SocraticSession Session(SessionPolicy? policy = null, string subject = "subject") =>
        new(subject, policy ?? new SessionPolicy(), ExamPrompts.Set, T0);

    public static Turn AskAnswer(this SocraticSession s, string concept, MoveKind move, string answer = "an answer", int minute = 0,
        IReadOnlyList<string>? targets = null)
    {
        s.Ask(concept, move, $"Question about {concept}?", T0.AddMinutes(minute), targets);
        return s.RecordAnswer(new Answer { Text = answer, CompletedAt = T0.AddMinutes(minute).AddSeconds(30) });
    }

    /// <summary>Asks, answers and judges one turn with <paramref name="score"/>, leaving the concept's estimate at <paramref name="estimate"/>.</summary>
    public static Turn Judged(this SocraticSession s, string concept, MoveKind move, double score, double estimate, int minute = 0,
        bool needsReview = false, string answer = "an answer", IReadOnlyList<string>? targets = null)
    {
        s.AskAnswer(concept, move, answer, minute, targets);
        return s.RecordJudgment(Judgment(score, needsReview), estimate);
    }

    public static AnswerJudgment Judgment(double score, bool needsReview = false, string summary = "ok") => new()
    {
        Criteria = [new CriterionScore { Criterion = "accuracy", Score = score, Citations = [new Citation("d1:0", "quote")] }],
        OverallScore = score,
        NeedsHumanReview = needsReview,
        Summary = summary,
    };

    public static SourcePassage Passage(string id, string text, string? location = null) =>
        new() { Id = id, DocumentId = id.Split(':')[0], Ordinal = int.TryParse(id.Split(':').Last(), out var n) ? n : 0, Text = text, Location = location };

    /// <summary>Photosynthesis/respiration material: two related concepts with rubrics and passages.</summary>
    public static SourceMaterialSubject Biology(bool related = true)
    {
        var passages = new List<SourcePassage>
        {
            Passage("bio:0", "Photosynthesis converts light energy into chemical energy stored in glucose, using carbon dioxide and water and releasing oxygen."),
            Passage("bio:1", "Chlorophyll in the chloroplasts absorbs light; the light reactions split water and make ATP and NADPH."),
            Passage("bio:2", "Cellular respiration breaks glucose down with oxygen to release energy as ATP, producing carbon dioxide and water."),
            Passage("bio:3", "Respiration happens in the mitochondria of plant and animal cells, day and night."),
        };
        var map = new ConceptMap
        {
            CollectionId = "biology",
            Concepts =
            [
                new ConceptEntry { Key = "photosynthesis", Title = "Photosynthesis", Definition = "How plants turn light into chemical energy.", PassageIds = ["bio:0", "bio:1"] },
                new ConceptEntry { Key = "respiration", Title = "Cellular respiration", Definition = "How cells release energy from glucose.", PassageIds = ["bio:2", "bio:3"] },
            ],
            Relations = related ? [new ConceptRelationEntry("photosynthesis", "respiration", "contrasts-with")] : [],
        };
        var rubrics = new List<ConceptRubric>
        {
            new() { ConceptKey = "photosynthesis", Version = 2, Understanding = [new RubricItem("Light energy becomes chemical energy in glucose", 3, ["bio:0"])] },
            new() { ConceptKey = "respiration", Understanding = [new RubricItem("Glucose is broken down to release ATP", 2, ["bio:2"])] },
        };
        return new SourceMaterialSubject("Biology", map, rubrics, new GroundingRetriever(passages));
    }
}
