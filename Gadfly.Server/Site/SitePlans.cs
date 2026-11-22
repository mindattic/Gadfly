namespace Gadfly.Server.Site;

/// <summary>
/// The plans the public pricing page describes (GAD-T308). Prices are by quote ("Contact us"). The names are the ones
/// plan enforcement uses (GAD-T309: Trial, Standard, Institution, Custom); when that ticket sets the real allowances,
/// change the numbers here so the pricing page and the enforced limits say the same thing.
/// </summary>
public static class SitePlans
{
    /// <summary>One plan as the pricing page shows it.</summary>
    /// <param name="Name">The plan's name, as enforcement will use it.</param>
    /// <param name="For">Who it suits, in one sentence.</param>
    /// <param name="StaffSeats">Instructor, examiner, reviewer and admin accounts.</param>
    /// <param name="StudentSeats">Student accounts.</param>
    /// <param name="Exams">Exam attempts allowed, with the period.</param>
    /// <param name="Extras">What the plan adds beyond the features every plan has.</param>
    public sealed record Plan(string Name, string For, string StaffSeats, string StudentSeats, string Exams, IReadOnlyList<string> Extras);

    public static readonly IReadOnlyList<Plan> All =
    [
        new("Trial", "One course team trying Gadfly on their own material.",
            "Up to 3", "Up to 25", "50 in total, over 30 days",
            ["A setup call with us", "Then move to any plan without losing your work"]),
        new("Standard", "A department running spoken exams across a few courses.",
            "Up to 10", "Up to 250", "500 a month",
            ["Email support"]),
        new("Institution", "A faculty or whole institution with several departments.",
            "Up to 50", "Up to 2,500", "5,000 a month",
            ["Priority support", "Help connecting your other systems through the API"]),
        new("Custom", "Certification bodies, consortia and anything larger.",
            "By agreement", "By agreement", "By agreement",
            ["Contract and data processing terms agreed with you", "Volume and multi-year pricing"]),
    ];

    /// <summary>The plan the page draws attention to.</summary>
    public const string Featured = "Institution";
}
