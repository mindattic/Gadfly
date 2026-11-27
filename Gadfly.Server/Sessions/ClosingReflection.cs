using Gadfly.Engine;

namespace Gadfly.Server.Sessions;

/// <summary>
/// The closing reflection (GAD-T817): after the examiner ends the exam, one fixed question invites the student to examine their own
/// claim to know (Elenchus at Scale 2.2.1, Apology 21d; 2.2.2, Meno 84a-b). It is asked word for word to every student with no model
/// call, after the clock has stopped, and the student may skip it. It asks for reasons, never for the process of thinking (2.3.2), and
/// belongs to no objective: it is not a turn and never reaches a judge, a grade, a staff report or a model prompt (3.2). Only the
/// student's own published result shows it back to them (GAD-T821).
/// </summary>
public static class ClosingReflection
{
    /// <summary>Offered and waiting for the student.</summary>
    public const string Pending = "Pending";
    /// <summary>The student answered it.</summary>
    public const string Answered = "Answered";
    /// <summary>The student skipped it (an empty answer counts as skipped).</summary>
    public const string Skipped = "Skipped";

    /// <summary>The longest reflection kept, in characters.</summary>
    public const int MaxLength = 20_000;

    public const string English = "Before we finish: of everything you said today, which claim are you least sure you could defend, and why?";
    public const string Spanish = "Antes de terminar: de todo lo que dijo hoy, ¿qué afirmación es la que tiene menos certeza de poder defender, y por qué?";
    public const string Korean = "마치기 전에 여쭙겠습니다. 오늘 말씀하신 것 가운데 가장 자신 있게 옹호하기 어려운 주장은 무엇이며, 그 이유는 무엇인가요?";

    /// <summary>The reflection question in the attempt's exam language (English for any other).</summary>
    public static string Prompt(string? language) => language switch
    {
        ExamLanguage.Spanish => Spanish,
        ExamLanguage.Korean => Korean,
        _ => English,
    };

    /// <summary>
    /// Whether an exam that ended for <paramref name="reason"/> offers the reflection: only when the examiner ended it (every objective
    /// had its evidence, the turn limit, or time ran out), never when the student finished early or staff stopped it.
    /// </summary>
    public static bool IsOfferedAfter(EndReason reason) => reason is EndReason.EvidenceComplete or EndReason.TurnLimit or EndReason.TimeUp;
}
