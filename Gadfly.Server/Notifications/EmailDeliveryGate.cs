using Gadfly.Socrates;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Notifications;

/// <summary>
/// Whether this server may hand a notification email to the real SMTP transport (GAD-T310/GAD-T374). Two things
/// stop it, and both are checked at send time, not at queue time, so the log always says why nothing went out:
/// <list type="bullet">
/// <item>No SMTP configured (MindAttic.Authentication's <see cref="AuthEmailStatus"/> has no settings).</item>
/// <item>The server runs the mock AI panel (<c>Gadfly:AiMode=mock</c>): a mock server is a demo or a test bed, and
/// must never email real people. <c>Gadfly:Notifications:SendInMockMode=true</c> lifts this for a developer who has
/// pointed SMTP at a local catcher on purpose.</item>
/// </list>
/// </summary>
public static class EmailDeliveryGate
{
    public const string SendInMockModeSetting = "Gadfly:Notifications:SendInMockMode";

    /// <summary>Null when a real send may go ahead; otherwise the plain-English reason it may not.</summary>
    public static string? BlockedReason(AuthEmailStatus emailStatus, IPanelInfo? panel, IConfiguration configuration)
    {
        if (emailStatus.Settings is null)
            return "Outgoing email is not configured for this server (MindAttic:Vault:Notifications:email), so no email is sent.";
        if (panel is { IsMock: true } && !configuration.GetValue(SendInMockModeSetting, false))
            return "This server runs in mock mode, so no real email is sent.";
        return null;
    }
}
