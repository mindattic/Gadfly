using Gadfly.Server.Data;

namespace Gadfly.Server.Grading;

/// <summary>
/// What the reconciler-approver rule decides for one approval (GAD-T444). <see cref="Applies"/> is false when the approver
/// reconciled nothing on this grade. When it applies, the approval is either <see cref="Refused"/>, or it goes ahead with a
/// <see cref="Waiver"/> reason that is audited with it.
/// </summary>
public sealed record ApproverSeparationDecision(bool Applies, bool Refused, string? Waiver)
{
    public static readonly ApproverSeparationDecision NotApplicable = new(false, false, null);
}

/// <summary>
/// Separate reconciler and approver (GAD-T444; ElenchusAtScale M13, Section 3.8, Section 5.9, correction 47): a large
/// disagreement is to be resolved by a reviewer independent of both judgments, and the grade then approved by someone
/// who did not resolve it, so that no one person's judgment is ratified by that same person. GAD-T285 already keeps the
/// human scorer from reconciling or approving; this keeps the reconciler (or the second reviewer named on the
/// reconciliation) from approving. The organization chooses how strict it is (<see cref="ApproverSeparationSetting"/>).
/// </summary>
public static class ApproverSeparation
{
    public const string RefusedAction = "grade.reconciler-approver.refused";
    public const string WaivedAction = "grade.reconciler-approver.waived";

    /// <param name="setting">The organization's setting.</param>
    /// <param name="approverReconciled">The approver recorded, or was the second reviewer on, a reconciliation of this grade.</param>
    /// <param name="singleGraderAllowed">The organization allows a single grader (GAD-T285's audited waiver).</param>
    /// <param name="anotherApproverAvailable">
    /// Some other active member could approve this grade: holds the approve permission, and neither scored nor reconciled it
    /// (a scorer counts when a single grader is allowed).
    /// </param>
    public static ApproverSeparationDecision Decide(ApproverSeparationSetting setting, bool approverReconciled, bool singleGraderAllowed,
        bool anotherApproverAvailable)
    {
        if (!approverReconciled) return ApproverSeparationDecision.NotApplicable;
        return setting switch
        {
            ApproverSeparationSetting.Always => new(true, true, null),
            ApproverSeparationSetting.Off => new(true, false, "The organization does not require the approver to differ from the reconciler."),
            _ when singleGraderAllowed => new(true, false, "Allowed by the organization's single-grader setting."),
            _ when !anotherApproverAvailable => new(true, false, "No one else in the organization can approve this grade."),
            _ => new(true, true, null),
        };
    }

    /// <summary>The setting in plain words, for the settings page and the grading page.</summary>
    public static string Describe(ApproverSeparationSetting s) => s switch
    {
        ApproverSeparationSetting.Always => "Always: whoever reconciled a grade can never approve it",
        ApproverSeparationSetting.Off => "Off: whoever reconciled a grade may approve it (each time recorded in the audit log)",
        _ => "Automatic: whoever reconciled a grade cannot approve it while someone else in the organization can",
    };
}
