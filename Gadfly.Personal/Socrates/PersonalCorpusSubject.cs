using Gadfly.Engine;
using Gadfly.Prompts;

namespace Gadfly.Socrates;

/// <summary>
/// The personal introspection subject: one concept ("self"), grounded in the digest of your own
/// mined conversations, worded by the three irrevocable <see cref="CanonicalPrompts"/>. The prompt
/// set wraps those constants byte for byte; it adds a hash for the session record, nothing else.
/// </summary>
public sealed class PersonalCorpusSubject : ISubjectMatter
{
    public const string SubjectId = "personal";
    public const string SelfConceptId = "self";
    public const string InterrogationKey = "interrogation";
    public const string ReflectionKey = "reflection";
    public const string MasterKey = "master";

    /// <summary>The personal prompt set, version 1: exactly the three canonical prompts.</summary>
    public static readonly PromptSet Prompts = new(SubjectId, 1, new Dictionary<string, string>
    {
        [InterrogationKey] = CanonicalPrompts.Interrogation,
        [ReflectionKey] = CanonicalPrompts.FinalReflection,
        [MasterKey] = CanonicalPrompts.MasterPrompt,
    });

    private static readonly IReadOnlyList<PlannerConcept> Concepts = [new PlannerConcept(SelfConceptId)];

    private readonly string _corpusDigest;

    public PersonalCorpusSubject(string corpusDigest)
    {
        _corpusDigest = corpusDigest ?? "";
    }

    public string Id => SubjectId;
    public string Title => "Your own conversations";
    public PromptSet PromptSet => Prompts;

    public Task<IReadOnlyList<PlannerConcept>> GetConceptsAsync(CancellationToken ct = default) => Task.FromResult(Concepts);

    public Task<GroundingContext> GetGroundingAsync(string conceptId, string? answer = null, CancellationToken ct = default) =>
        Task.FromResult(new GroundingContext(_corpusDigest, []));

    /// <summary>
    /// A policy for an open-ended personal interrogation: one concept, up to <paramref name="maxTurns"/>
    /// turns, no practical time limit (it ends at Aporia, end of input or the turn cap).
    /// </summary>
    public static SessionPolicy InterrogationPolicy(int maxTurns) => new()
    {
        ConceptIds = [SelfConceptId],
        MaxTurns = Math.Max(1, maxTurns),
        MaxTurnsPerConcept = Math.Max(1, maxTurns),
        TimeBudget = TimeSpan.FromDays(1),
    };
}
