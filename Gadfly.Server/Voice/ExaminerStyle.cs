using Gadfly.Engine;

namespace Gadfly.Server.Voice;

/// <summary>
/// How the talking examiner carries herself while she asks a question (GAD-T303): a fixed, server-side map from the kind of
/// question (the planner's <see cref="MoveKind"/>) to one of three speaking styles the browser plays on the avatar.
/// <para>
/// The styles differ only in head tilt and how far the brows lift, never in valence: every style keeps the same mild, friendly
/// baseline, and none smiles more or frowns. Moves that tend to follow a weak answer (a counterexample, a contradiction) share
/// the style of moves that follow any answer (a probe, a clarification), so her face can't tell the student how the last answer
/// went. Requests about an answer (a clarification of meaning on a typed answer), fillers and remarks are always <see cref="Neutral"/>.
/// A spoken answer is never read back (GAD-T405).
/// </para>
/// </summary>
public static class ExaminerStyle
{
    /// <summary>Level gaze, no tilt: requests, closings, hints and anything not listed.</summary>
    public const string Neutral = "neutral";
    /// <summary>Opening a topic or a new situation: brows lifted a little, a slight tilt.</summary>
    public const string Open = "open";
    /// <summary>Following up on what was said: a small lean in and a slight tilt the other way.</summary>
    public const string Probe = "probe";

    public static readonly IReadOnlyList<string> All = [Neutral, Open, Probe];

    /// <summary>The style for a question of this move (the step's <c>Move</c>, a <see cref="MoveKind"/> name); unknown or null is neutral.</summary>
    public static string For(string? move) => Enum.TryParse<MoveKind>(move, ignoreCase: false, out var kind) ? For(kind) : Neutral;

    public static string For(MoveKind move) => move switch
    {
        MoveKind.Elicit or MoveKind.Apply or MoveKind.Synthesize or MoveKind.ScenarioVariation or MoveKind.IndependentExplanation => Open,
        MoveKind.Clarify or MoveKind.Probe or MoveKind.Counterexample or MoveKind.Contradiction
            or MoveKind.CausalReasoning or MoveKind.AssumptionTesting => Probe,
        _ => Neutral,
    };
}
