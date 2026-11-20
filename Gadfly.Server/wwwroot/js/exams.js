// Exam builder (GAD-T50): the browser's UTC offset, so assignment windows are entered in local time.
// getTimezoneOffset is minutes behind UTC (positive west of Greenwich).
export function timezoneOffsetMinutes() {
    return new Date().getTimezoneOffset();
}
