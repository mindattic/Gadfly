namespace Gadfly.Server.Platform;

/// <summary>
/// The four named plans (GAD-T309), with the seat and exam numbers the public pricing page shows
/// (Site/SitePlans.cs, GAD-T308) and plan enforcement applies by default. A platform admin may still set
/// different numbers for one organization (PlatformPlans.razor): these are only the defaults a plan starts
/// with when first chosen, not a ceiling on what an admin may type in.
/// </summary>
public static class PlanCatalog
{
    public const string Trial = "Trial";
    public const string Standard = "Standard";
    public const string Institution = "Institution";
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<string> All = [Trial, Standard, Institution, Custom];

    public static bool IsValid(string? plan) => plan is not null && All.Contains(plan, StringComparer.Ordinal);

    /// <param name="StaffSeats">Owner/Admin/Instructor/Examiner/SecondReviewer accounts.</param>
    /// <param name="StudentSeats">Student accounts.</param>
    /// <param name="MonthlyExams">Exam sessions allowed per period; null = no cap.</param>
    /// <param name="TrialDays">Only set for Trial: the one-off period its exam allowance covers.</param>
    public sealed record Defaults(int StaffSeats, int StudentSeats, int? MonthlyExams, int? TrialDays);

    private static readonly IReadOnlyDictionary<string, Defaults> Numbers = new Dictionary<string, Defaults>(StringComparer.Ordinal)
    {
        // "Up to 3 staff / 25 students, 50 exams in total over 30 days" (Site/SitePlans.cs).
        [Trial] = new(3, 25, 50, 30),
        // "Up to 10 staff / 250 students, 500 exams a month."
        [Standard] = new(10, 250, 500, null),
        // "Up to 50 staff / 2,500 students, 5,000 exams a month."
        [Institution] = new(50, 2500, 5000, null),
        // "By agreement": a platform admin sets the real seat and exam numbers when they set this plan.
        [Custom] = new(0, 0, null, null),
    };

    /// <summary>The seat/exam numbers a plan starts with; unknown names fall back to Trial's.</summary>
    public static Defaults DefaultsFor(string? plan) => Numbers.TryGetValue(plan ?? "", out var d) ? d : Numbers[Trial];
}
