namespace Gadfly.Server.Exams;

/// <summary>
/// Converts the wall-clock times an instructor types for an assignment window to the instants stored (GAD-T214). The browser's
/// time zone is used for every date, so a window across a daylight-saving change is right; a single UTC offset (today's) is only the
/// fallback when the browser's zone is unknown to the server.
/// </summary>
public static class AssignmentTime
{
    /// <summary>The time zone the browser named (an IANA id such as "Europe/London"), or null when it is missing or unknown here.</summary>
    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    /// <summary>The instant a typed local time means. A time that does not exist (spring forward) or happens twice (fall back) takes the zone's standard offset.</summary>
    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo? zone, TimeSpan fallbackOffset)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone is null ? fallbackOffset : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset).ToUniversalTime();
    }

    /// <summary>An instant as the wall-clock time the instructor's zone shows for it.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo? zone, TimeSpan fallbackOffset) =>
        zone is null ? instant.ToOffset(fallbackOffset) : TimeZoneInfo.ConvertTime(instant, zone);
}
