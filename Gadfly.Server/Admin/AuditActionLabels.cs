using Gadfly.Server.Privacy;

namespace Gadfly.Server.Admin;

/// <summary>
/// Plain-English names for audit actions whose code alone doesn't say what happened (GAD-T320), shown beside the code on
/// the audit log page and in its Action filter. The code stays the record (filters, exports and the API use it); an action
/// with no name here is shown by its code only.
/// </summary>
public static class AuditActionLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [PrivacyService.ErasedAction] = "Student data erased",
        ["audit.exported"] = "Audit log exported",
        ["audit.viewed.platform"] = "Audit log opened by MindAttic platform staff",
    };

    /// <summary>The plain name of <paramref name="action"/>, or null when it has none.</summary>
    public static string? For(string? action) => action is not null && Labels.TryGetValue(action, out var label) ? label : null;

    /// <summary>The text of the action's filter choice: "privacy.erased (Student data erased)", or the code alone.</summary>
    public static string Option(string action) => For(action) is { } label ? $"{action} ({label})" : action;
}
