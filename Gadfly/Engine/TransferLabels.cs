namespace Gadfly.Engine;

/// <summary>
/// How a transfer item's situation differs from the source material, using the context dimensions of Barnett and Ceci's
/// (2002) transfer taxonomy (research note 03). Every Apply question carries one, so a reviewer can see how far the
/// transfer reached instead of reading a bare "transfer shown" (GAD-T268).
/// </summary>
public enum TransferContext
{
    /// <summary>The same kind of problem put to a different purpose or use.</summary>
    Functional,
    /// <summary>The same idea in a different physical setting.</summary>
    Physical,
    /// <summary>The same idea where other people are involved, or a different role or group.</summary>
    Social,
    /// <summary>The same idea at a different time scale, or later or earlier than the source's case.</summary>
    Temporal,
    /// <summary>The same idea met in a different form (a table, a diagram, a description of an object, a procedure).</summary>
    Modality,
    /// <summary>The idea in a different subject area (far transfer).</summary>
    KnowledgeDomain,
}

/// <summary>
/// Deterministic transfer labels for Apply questions (GAD-T268). Only Apply, a situation new to the student, probes transfer;
/// a what-if inside a situation already discussed (ScenarioVariation) is extrapolation (research note 03: Bloom1956,
/// BarnettCeci2002). Each Apply question on an objective takes the next context in <see cref="Order"/> (nearest first), so the
/// label is recomputed from the transcript alone and replays the same for audit. Gadfly measures sequestered transfer (no
/// resources, no hints: BransfordSchwartz1999), which understates what a student could do with help; reports say so.
/// </summary>
public static class TransferLabels
{
    /// <summary>The order Apply questions on one objective move through the contexts: nearer differences first, a new domain last.</summary>
    public static readonly IReadOnlyList<TransferContext> Order =
    [
        TransferContext.Functional,
        TransferContext.Physical,
        TransferContext.Social,
        TransferContext.Temporal,
        TransferContext.Modality,
        TransferContext.KnowledgeDomain,
    ];

    /// <summary>What the transfer label says about the item, as shown to reviewers.</summary>
    public const string Caveat = "Sequestered transfer: answered without resources or hints, so it may understate what the student could do with them.";

    /// <summary>The context the next Apply question on <paramref name="objectiveKey"/> should differ from the source in.</summary>
    public static TransferContext Next(SocraticSession session, string objectiveKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        var asked = ObjectiveAssessor.TurnsOn(session, objectiveKey).Count(t => t.Move == MoveKind.Apply);
        return Order[asked % Order.Count];
    }

    /// <summary>The transfer label of <paramref name="turn"/>; null when it is not an Apply question (only Apply probes transfer).</summary>
    public static TransferContext? Of(SocraticSession session, Turn turn)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        if (turn.Move != MoveKind.Apply) return null;
        var key = ExamObjectives.KeyOf(turn);
        var before = session.Turns.Count(t => t.Index < turn.Index && t.Move == MoveKind.Apply && ExamObjectives.KeyOf(t) == key);
        return Order[before % Order.Count];
    }

    /// <summary>Short label for reviewers, e.g. "near transfer: different physical setting".</summary>
    public static string Label(TransferContext context) => $"{Distance(context)} transfer: {Difference(context)}";

    /// <summary>"near" for a difference of setting, use, people or time; "far" for a different form or subject.</summary>
    public static string Distance(TransferContext context) =>
        context is TransferContext.Modality or TransferContext.KnowledgeDomain ? "far" : "near";

    /// <summary>How the item's situation differs from the source, in plain words.</summary>
    public static string Difference(TransferContext context) => context switch
    {
        TransferContext.Functional => "different purpose or use",
        TransferContext.Physical => "different physical setting",
        TransferContext.Social => "different people or roles",
        TransferContext.Temporal => "different time scale",
        TransferContext.Modality => "different form of presentation",
        TransferContext.KnowledgeDomain => "different subject area",
        _ => context.ToString(),
    };

    /// <summary>The instruction the question prompt gets for an Apply move: how the new situation must differ from the source.</summary>
    public static string Instruction(TransferContext context) => context switch
    {
        TransferContext.Functional => "keep the subject, but put the concept to a purpose or use the source does not discuss",
        TransferContext.Physical => "keep the purpose, but set the situation in a physical place or setting the source does not discuss",
        TransferContext.Social => "keep the purpose, but involve people, roles or a group the source does not discuss",
        TransferContext.Temporal => "keep the purpose, but move the situation to a time scale (much shorter, much longer, or later) the source does not discuss",
        TransferContext.Modality => "present the situation in a different form from the source's (a few numbers, a described object or a short procedure)",
        TransferContext.KnowledgeDomain => "set the situation in a different subject area where the same concept still applies",
        _ => "describe a situation the source does not discuss",
    } + $" ({Label(context)})";
}
