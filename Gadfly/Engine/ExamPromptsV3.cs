namespace Gadfly.Engine;

/// <summary>
/// The objective-driven examination prompts (GAD-T89..T93). They now live in <see cref="ExamPrompts"/>
/// (version 4, GAD-T120), so <see cref="Merge"/> returns <see cref="ExamPrompts.Set"/> itself and
/// <see cref="Set"/> is the one exam prompt set every session runs on and records.
/// </summary>
public static class ExamPromptsV3
{
    /// <summary>Replaces the "question" prompt: all eight strategies (Part 2.3), the objective, and the never-teach rule.</summary>
    public const string QuestionKey = ExamPrompts.QuestionKey;
    /// <summary>Learning Mode only: a hint or guided explanation followed by a question (Part 3.2).</summary>
    public const string LearningQuestionKey = ExamPrompts.LearningQuestionKey;
    /// <summary>Per-answer response analysis (Component B): claims, misconceptions, uncertainties, evidence per objective.</summary>
    public const string AnalysisKey = ExamPrompts.AnalysisKey;

    /// <summary>Merged into <see cref="ExamPrompts"/> version 4 word for word (GAD-T120); these aliases keep callers compiling.</summary>
    public const string Question = ExamPrompts.Question;
    public const string LearningQuestion = ExamPrompts.LearningQuestion;
    public const string Analysis = ExamPrompts.Analysis;

    /// <summary>The prompts this change adds or replaces, by key.</summary>
    public static readonly IReadOnlyDictionary<string, string> Prompts = new Dictionary<string, string>
    {
        [QuestionKey] = Question,
        [LearningQuestionKey] = LearningQuestion,
        [AnalysisKey] = Analysis,
    };

    /// <summary>
    /// <paramref name="baseSet"/> with these prompts overlaid, as the next version. Returns the base set unchanged
    /// once it already carries every one of them word for word (after the coordinator merges them).
    /// </summary>
    public static PromptSet Merge(PromptSet baseSet)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        if (Prompts.All(kv => baseSet.Prompts.TryGetValue(kv.Key, out var text) && text == kv.Value)) return baseSet;
        var merged = baseSet.Prompts.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var (key, text) in Prompts) merged[key] = text;
        return new PromptSet(baseSet.Id, baseSet.Version + 1, merged);
    }

    /// <summary>The exam prompt set sessions run on: <see cref="ExamPrompts.Set"/> with these prompts merged.</summary>
    public static readonly PromptSet Set = Merge(ExamPrompts.Set);
}
