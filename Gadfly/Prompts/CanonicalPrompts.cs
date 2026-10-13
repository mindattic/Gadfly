namespace Gadfly.Prompts;

/// <summary>
/// The three fixed Socratic prompts Gadfly exists to run. These are irrevocable by design:
/// no settings file, environment variable, or CLI flag may override them. Every runner in
/// <see cref="Gadfly.Socrates"/> reads these constants directly. If the wording ever needs to
/// change, it changes here, in source, under review — not at runtime.
/// </summary>
public static class CanonicalPrompts
{
    /// <summary>Prompt 1 — The Socratic Interrogation. Drives <see cref="Gadfly.Socrates.InterrogationSession"/>.</summary>
    public const string Interrogation =
        "Interrogate me as Socrates would interrogate Elenchus-style. " +
        "Base your questions on all the conversations we've had so far. " +
        "Identify one fundamental belief that I keep repeating and accepting without sufficient evidence, " +
        "and challenge it with a single question that could make that belief collapse. " +
        "Never give me the answer. " +
        "Keep questioning me until I acknowledge the flaw in my own reasoning and declare \"Aporia\" " +
        "— a state of philosophical puzzlement.";

    /// <summary>Prompt 2 — The Final Reflection. Drives <see cref="Gadfly.Socrates.ReflectionRunner"/>.</summary>
    public const string FinalReflection =
        "Now that the interrogation is over, give me three lines of what Socrates would say to me. " +
        "Tell me: " +
        "What I mistakenly believed. " +
        "The question I truly needed to ask myself. " +
        "One action I can take today to change. " +
        "Then break that action down into three small, practical steps I can take this week.";

    /// <summary>Prompt 3 — The Master Prompt (Expert Version). Drives <see cref="Gadfly.Socrates.MasterExaminationRunner"/>.</summary>
    public const string MasterPrompt =
        "Examine me through three different philosophical perspectives: Socrates, Confucius, and Buddha. " +
        "Use our entire conversation history as your source material. " +
        "When speaking as Socrates, use the Socratic method of questioning to dismantle one fundamental " +
        "assumption that I repeatedly accept without questioning. " +
        "Do not give me the answer. Lead me to discover the contradictions in my own thinking through a " +
        "series of challenging questions, until I reach a state of aporia.";
}
