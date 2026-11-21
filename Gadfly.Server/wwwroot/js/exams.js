// Exam builder (GAD-T50): the browser's UTC offset, so assignment windows are entered in local time.
// getTimezoneOffset is minutes behind UTC (positive west of Greenwich).
export function timezoneOffsetMinutes() {
    return new Date().getTimezoneOffset();
}

// The browser's IANA time zone (e.g. "Europe/London"), so the server converts every date with that zone's own rules, daylight saving
// included, instead of today's offset (GAD-T214). Null when the browser does not say.
export function timeZoneId() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
        return null;
    }
}
