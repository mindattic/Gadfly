namespace Gadfly.Server.Lti;

/// <summary>The LMSs the setup page has a step-by-step guide for (GAD-T384).</summary>
public enum LtiPlatformKind { Canvas, Moodle, Blackboard, Brightspace }

/// <summary>Gadfly's own LTI addresses, the values an administrator types into the LMS.</summary>
public sealed record LtiToolUrls(string Launch, string Login, string Jwks, string Domain)
{
    public static LtiToolUrls For(string baseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        return new(b + LtiEndpoints.LaunchPath, b + LtiEndpoints.LoginPath, b + LtiEndpoints.JwksPath,
            Uri.TryCreate(b, UriKind.Absolute, out var u) ? u.Host : b);
    }
}

/// <summary>
/// The platform-side values each LMS publishes for itself (GAD-T384), so the setup page can fill in all but the client
/// id and deployment id. These are the vendors' documented, public endpoints; a self-hosted or unusual installation may
/// differ, which is why every field stays editable and the guide says where the LMS shows its own values.
/// </summary>
public static class LtiPlatformGuide
{
    public static string Label(LtiPlatformKind kind) => kind switch
    {
        LtiPlatformKind.Canvas => "Canvas",
        LtiPlatformKind.Moodle => "Moodle",
        LtiPlatformKind.Blackboard => "Blackboard Learn",
        LtiPlatformKind.Brightspace => "D2L Brightspace",
        _ => kind.ToString(),
    };

    /// <summary>True when filling in needs the LMS's own web address (Moodle and Brightspace put their endpoints on it).</summary>
    public static bool NeedsSiteUrl(LtiPlatformKind kind) => kind is LtiPlatformKind.Moodle or LtiPlatformKind.Brightspace;

    /// <summary>The issuer, login, token, key-set and (Brightspace) token-audience values for <paramref name="kind"/>,
    /// or null with a reason when a needed input is missing. <paramref name="siteUrl"/> is the LMS's address, e.g.
    /// <c>https://moodle.example.edu</c>; <paramref name="clientId"/> is needed for Blackboard's key set.</summary>
    public static (LtiRegistrationInput? Values, string? Problem) Preset(LtiPlatformKind kind, string? siteUrl, string? clientId)
    {
        string? site = null;
        if (NeedsSiteUrl(kind))
        {
            if (!Uri.TryCreate((siteUrl ?? "").Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                return (null, $"Type your {Label(kind)} address first (the https:// address people open it at).");
            site = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
        var input = kind switch
        {
            LtiPlatformKind.Canvas => new LtiRegistrationInput
            {
                Name = "Canvas",
                Issuer = "https://canvas.instructure.com",
                AuthLoginUrl = "https://sso.canvaslms.com/api/lti/authorize_redirect",
                AuthTokenUrl = "https://sso.canvaslms.com/login/oauth2/token",
                KeySetUrl = "https://sso.canvaslms.com/api/lti/security/jwks",
            },
            LtiPlatformKind.Moodle => new LtiRegistrationInput
            {
                Name = "Moodle",
                Issuer = site!,
                AuthLoginUrl = site + "/mod/lti/auth.php",
                AuthTokenUrl = site + "/mod/lti/token.php",
                KeySetUrl = site + "/mod/lti/certs.php",
            },
            LtiPlatformKind.Blackboard => new LtiRegistrationInput
            {
                Name = "Blackboard Learn",
                Issuer = "https://blackboard.com",
                AuthLoginUrl = "https://developer.blackboard.com/api/v1/gateway/oidcauth",
                AuthTokenUrl = "https://developer.blackboard.com/api/v1/gateway/oauth2/jwttoken",
                KeySetUrl = string.IsNullOrWhiteSpace(clientId) ? ""
                    : $"https://developer.blackboard.com/api/v1/management/applications/{Uri.EscapeDataString(clientId.Trim())}/jwks.json",
            },
            LtiPlatformKind.Brightspace => new LtiRegistrationInput
            {
                Name = "Brightspace",
                Issuer = site!,
                AuthLoginUrl = site + "/d2l/lti/authenticate",
                AuthTokenUrl = "https://auth.brightspace.com/core/connect/token",
                KeySetUrl = site + "/d2l/.well-known/jwks",
                TokenAudience = "https://api.brightspace.com/auth/token",
            },
            _ => null,
        };
        return input is null ? (null, "Unknown LMS.") : (input, null);
    }
}
