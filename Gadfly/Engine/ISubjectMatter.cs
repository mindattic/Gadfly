namespace Gadfly.Engine;

/// <summary>Source context for one concept: the text the examiner and judge are grounded in, and where it came from.</summary>
public sealed record GroundingContext(string Text, IReadOnlyList<Citation> Citations)
{
    public static readonly GroundingContext Empty = new("", []);
}

/// <summary>
/// What a Socratic session examines. The engine is subject-agnostic: a subject supplies the
/// concepts to cover, the grounding for each, and the prompt set that words the questions.
/// Implementations: the personal introspection corpus (Gadfly.Personal) and instructor source
/// material (Gadfly.Sources).
/// </summary>
public interface ISubjectMatter
{
    /// <summary>Stable id recorded on every session, e.g. "personal" or a source collection id.</summary>
    string Id { get; }

    /// <summary>Human-readable title.</summary>
    string Title { get; }

    /// <summary>The versioned prompt set sessions on this subject run with.</summary>
    PromptSet PromptSet { get; }

    /// <summary>The concepts a session can cover, with their relationships.</summary>
    Task<IReadOnlyList<PlannerConcept>> GetConceptsAsync(CancellationToken ct = default);

    /// <summary>
    /// Grounding for <paramref name="conceptId"/>, optionally focused by the student's latest
    /// <paramref name="answer"/> (retrieval can use it to pull the passages the answer touches).
    /// </summary>
    Task<GroundingContext> GetGroundingAsync(string conceptId, string? answer = null, CancellationToken ct = default);
}
