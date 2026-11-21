namespace Gadfly.Engine;

/// <summary>
/// The examiner's move for one turn: what kind of question it is asking and why.
/// The planner picks one per turn; the question generator writes the question for it.
/// See docs/SOCRATIC_PLATFORM.md, "The Socratic engine".
/// </summary>
public enum MoveKind
{
    /// <summary>"What is X? In your own words." Opens a concept.</summary>
    Elicit,
    /// <summary>Pins down a vague or ambiguous term the student just used.</summary>
    Clarify,
    /// <summary>"Why does that follow?" / "What would happen if…" Tests the reasoning behind a claim.</summary>
    Probe,
    /// <summary>Offers a case that breaks the student's definition or rule.</summary>
    Counterexample,
    /// <summary>Confronts the student with two of their own claims that conflict (the elenchus).</summary>
    Contradiction,
    /// <summary>A new situation that needs the concept rather than its definition.</summary>
    Apply,
    /// <summary>Connects two concepts.</summary>
    Synthesize,
    /// <summary>Ends the current concept: mastery shown, aporia reached, or its budget spent.</summary>
    Close,
    // Strategies B, D, G and H of the master plan (Part 2.3), and Learning Mode's assistance moves (Part 3.2).
    // Appended so the numeric values of the older moves never change; transcripts store names anyway.
    /// <summary>"What causes this outcome?" Tests cause-and-effect understanding (strategy B).</summary>
    CausalReasoning,
    /// <summary>Changes one relevant condition and asks what changes and why (strategy D).</summary>
    ScenarioVariation,
    /// <summary>"What must be true for your explanation to hold?" (strategy G).</summary>
    AssumptionTesting,
    /// <summary>Reconstruct the explanation in their own reasoning, without the textbook wording (strategy H).</summary>
    IndependentExplanation,
    /// <summary>Learning Mode only: a hint, then the student tries again. Everything after it is assisted evidence.</summary>
    Hint,
    /// <summary>Learning Mode only: a short guided explanation, then a reassessment question. Assisted evidence.</summary>
    GuidedExplanation,
}

/// <summary>Why a session (or one concept within it) ended.</summary>
public enum EndReason
{
    /// <summary>Still running.</summary>
    None,
    /// <summary>Every target concept reached the policy's mastery threshold.</summary>
    Mastery,
    /// <summary>The student declared, or the examiner detected, aporia: an honest "I don't know".</summary>
    Aporia,
    /// <summary>The session's time budget ran out.</summary>
    TimeUp,
    /// <summary>The policy's turn limit was reached.</summary>
    TurnLimit,
    /// <summary>The student or the host ended the session early.</summary>
    Abandoned,
    /// <summary>The session stopped on an unrecoverable error.</summary>
    Failed,
    /// <summary>Every learning objective closed: sufficient evidence, or an explicit inconclusive result (GAD-T89).</summary>
    EvidenceComplete,
}
