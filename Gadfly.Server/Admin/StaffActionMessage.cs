using Gadfly.Server.Localization;

namespace Gadfly.Server.Admin;

/// <summary>
/// What a staff action from the live monitor says in the student's exam room (GAD-T299), sent through <see cref="LiveSessionFeed"/>
/// as a language-neutral message (GAD-T606): a string key and its times, worded by the exam room in the attempt's language. Text that
/// isn't such a message (an older server's sentence) is shown as it is.
/// </summary>
public static class StaffActionMessage
{
    public const string Extended = "examRoom.staffAction.extended";
    public const string Paused = "examRoom.staffAction.paused";
    public const string Resumed = "examRoom.staffAction.resumed";
    public const string Ended = "examRoom.staffAction.ended";

    private const char Marker = '\u0001';
    private const char Separator = '|';

    /// <summary>A message for <paramref name="key"/> whose slots are the given times (worded as minutes in the student's language).</summary>
    public static string Encode(string key, params TimeSpan[] times) =>
        Marker + key + string.Concat(times.Select(t => Separator + ((long)Math.Round(Math.Max(0, t.TotalSeconds))).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The message in <paramref name="text"/>'s language; anything that isn't an encoded message comes back unchanged.</summary>
    public static string Render(string message, UiText text)
    {
        if (message.Length < 2 || message[0] != Marker) return message;
        var parts = message[1..].Split(Separator);
        var times = parts.Skip(1).Select(p => long.TryParse(p, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? Minutes(TimeSpan.FromSeconds(s), text) : p).Cast<object>().ToArray();
        return text[parts[0], times].Value;
    }

    /// <summary>A span as the student reads it: "less than a minute", "1 minute", "12 minutes" in English (as LiveSessionService.Minutes).</summary>
    public static string Minutes(TimeSpan span, UiText text)
    {
        if (span < TimeSpan.FromMinutes(1)) return text["examRoom.minutes.lessThanOne"];
        var m = (int)Math.Round(span.TotalMinutes);
        return m == 1 ? text["examRoom.minutes.one"] : text["examRoom.minutes.many", m];
    }
}
