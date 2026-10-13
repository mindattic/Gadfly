using Gadfly.Prompts;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

/// <summary>
/// Guards against an accidental edit to the three irrevocable prompts. Deliberately
/// written as plain literals here (not re-derived via the same concatenation the
/// source uses) so a bug in how the source assembles the string would still be caught.
/// </summary>
[TestFixture]
public class CanonicalPromptsTests
{
    [Test]
    public void Interrogation_MatchesSpecVerbatim()
    {
        Assert.That(CanonicalPrompts.Interrogation, Is.EqualTo(
            "Interrogate me as Socrates would interrogate Elenchus-style. Base your questions on all the " +
            "conversations we've had so far. Identify one fundamental belief that I keep repeating and " +
            "accepting without sufficient evidence, and challenge it with a single question that could make " +
            "that belief collapse. Never give me the answer. Keep questioning me until I acknowledge the flaw " +
            "in my own reasoning and declare \"Aporia\" — a state of philosophical puzzlement."));
    }

    [Test]
    public void FinalReflection_MatchesSpecVerbatim()
    {
        Assert.That(CanonicalPrompts.FinalReflection, Is.EqualTo(
            "Now that the interrogation is over, give me three lines of what Socrates would say to me. Tell " +
            "me: What I mistakenly believed. The question I truly needed to ask myself. One action I can take " +
            "today to change. Then break that action down into three small, practical steps I can take this week."));
    }

    [Test]
    public void MasterPrompt_MatchesSpecVerbatim()
    {
        Assert.That(CanonicalPrompts.MasterPrompt, Is.EqualTo(
            "Examine me through three different philosophical perspectives: Socrates, Confucius, and Buddha. " +
            "Use our entire conversation history as your source material. When speaking as Socrates, use the " +
            "Socratic method of questioning to dismantle one fundamental assumption that I repeatedly accept " +
            "without questioning. Do not give me the answer. Lead me to discover the contradictions in my own " +
            "thinking through a series of challenging questions, until I reach a state of aporia."));
    }
}
