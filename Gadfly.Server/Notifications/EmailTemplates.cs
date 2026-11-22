using System.Net;

namespace Gadfly.Server.Notifications;

/// <summary>
/// What a template needs, gathered by whoever stages the email (GAD-T310). Every field is optional except the
/// two every template uses; a template leaves out what it doesn't have rather than printing "(none)".
/// </summary>
public sealed record EmailData(
    string OrganizationName,
    string RecipientName,
    string? ExamTitle = null,
    DateTimeOffset? DueAtUtc = null,
    int? AttemptsAllowed = null,
    string? Score = null,
    string? AppealOutcome = null,
    string? AppealResolution = null,
    string? ActionUrl = null,
    string? ActionLabel = null);

/// <summary>One rendered email: a plain-English subject, an HTML body in Gadfly's own colors, and a plain-text fallback.</summary>
public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// Branded HTML templates for Gadfly's own notification emails (GAD-T310): the same palette as the public site
/// (wwwroot/app.css, GAD-T308) inlined, since email clients ignore stylesheets. Plain English, short, one clear
/// action link where there is one. Invitation email stays MindAttic.Authentication's own (plain text); these are
/// never sent for that.
/// </summary>
public static class EmailTemplates
{
    private const string Accent = "#7a3e2e";
    private const string Ink = "#1d1d1f";
    private const string Muted = "#5f6368";
    private const string Bg = "#f6f5f1";
    private const string Panel = "#ffffff";
    private const string Line = "#dedbd2";

    public static RenderedEmail Render(string kind, EmailData d)
    {
        var (subject, lead, body, action) = kind switch
        {
            EmailKinds.ExamAssigned => (
                $"{Req(d.ExamTitle)} has been assigned to you",
                $"{d.OrganizationName} assigned you an exam.",
                Paragraphs(
                    $"<strong>{Esc(Req(d.ExamTitle))}</strong> is now on your list of exams.",
                    d.DueAtUtc is { } due ? $"Due by {When(due)}." : null,
                    d.AttemptsAllowed is { } n ? $"You have {n} attempt{(n == 1 ? "" : "s")}." : null),
                ("Open my exams", d.ActionUrl)),

            EmailKinds.Reminder => (
                $"Reminder: {Req(d.ExamTitle)} is due soon",
                $"A friendly reminder from {d.OrganizationName}.",
                Paragraphs(
                    $"<strong>{Esc(Req(d.ExamTitle))}</strong> is due {(d.DueAtUtc is { } due ? $"by {When(due)}" : "soon")} and you haven't finished it yet.",
                    "If you've already taken it, there's nothing to do."),
                ("Open my exams", d.ActionUrl)),

            EmailKinds.ResultReleased => (
                $"Your result for {Req(d.ExamTitle)} is ready",
                $"{d.OrganizationName} has released your result.",
                Paragraphs(
                    $"Your result for <strong>{Esc(Req(d.ExamTitle))}</strong> is ready to view.",
                    d.Score is { } s ? $"Result: <strong>{Esc(s)}</strong>." : null,
                    "If anything looks wrong, you can appeal from the result page."),
                ("View my result", d.ActionUrl)),

            EmailKinds.AppealReceived => (
                $"We received your appeal for {Req(d.ExamTitle)}",
                "Your appeal was submitted.",
                Paragraphs(
                    $"Your appeal about <strong>{Esc(Req(d.ExamTitle))}</strong> has been received and is waiting for your instructor to review.",
                    "Your previously published result stays visible in the meantime. We'll email you again once it's decided."),
                ((string?)null, (string?)null)),

            EmailKinds.AppealDecided => (
                $"Your appeal for {Req(d.ExamTitle)} has been decided",
                "Your appeal has a decision.",
                Paragraphs(
                    $"Your appeal about <strong>{Esc(Req(d.ExamTitle))}</strong> has been decided: <strong>{Esc(d.AppealOutcome ?? "Resolved")}</strong>.",
                    d.AppealResolution is { Length: > 0 } r ? Esc(r) : null),
                ("View my result", d.ActionUrl)),

            EmailKinds.AttemptReset => (
                $"Your attempts for {Req(d.ExamTitle)} were reset",
                $"{d.OrganizationName} reset your exam.",
                Paragraphs(
                    $"Your attempts for <strong>{Esc(Req(d.ExamTitle))}</strong> were reset by your organization. Your previous answers, scores and grades for it no longer count.",
                    "You can take it again from the start."),
                ("Open my exams", d.ActionUrl)),

            EmailKinds.LmsLink => (
                "Connect your LMS sign-in to your Gadfly account",
                $"{d.OrganizationName}: confirm it's you.",
                Paragraphs(
                    "Someone opened a Gadfly exam from your school's learning system (LMS) with this email address, and asked to use the Gadfly account you already have instead of a new one.",
                    "If that was you, open the link below <strong>in the same browser</strong> within 24 hours. After that, opening Gadfly from your course takes you to this account.",
                    "If it wasn't you, ignore this email: nothing changes, and the link stops working on its own."),
                ("Connect my LMS sign-in", d.ActionUrl)),

            EmailKinds.Test => (
                "Gadfly test email",
                "This is a test send.",
                Paragraphs("If you can read this, Gadfly's outgoing email is working: the template, the branding and the delivery path all came through."),
                ((string?)null, (string?)null)),

            _ => (kind, "", Paragraphs(Esc(kind)), ((string?)null, (string?)null)),
        };

        // The lead is plain text: Wrap encodes it once (GAD-T479: it used to be encoded above as well, so "A & B" showed "A &amp; B").
        var html = Wrap(d.OrganizationName, lead, body, action.Item1 ?? d.ActionLabel, action.Item2 ?? d.ActionUrl);
        var text = PlainText(d.OrganizationName, lead, body, action.Item1 ?? d.ActionLabel, action.Item2 ?? d.ActionUrl);
        return new RenderedEmail(subject, html, text);
    }

    /// <summary>Where the server's public address is configured, first match wins (the same keys invitations and resets use).</summary>
    public static readonly string[] PublicBaseUrlKeys =
    [
        "MindAttic:Auth:Organizations:Invitations:PublicBaseUrl", "MindAttic:Auth:Reset:PublicBaseUrl", "MindAttic:Vault:Payments:stripe:publicBaseUrl",
    ];

    /// <summary>
    /// The link an email can carry (GAD-T479): callers stage app paths such as "/my-exams", which mean nothing in a mail client.
    /// A path is joined to the configured public address; an absolute http(s) URL is kept; anything else, or a path when no public
    /// address is configured, gives null, so the email goes out without a button rather than with a dead one.
    /// </summary>
    public static string? AbsoluteActionUrl(string? url, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && abs.Scheme is "https" or "http") return url;
        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal)) return null;
        if (string.IsNullOrWhiteSpace(publicBaseUrl) || !Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var b) || b.Scheme is not ("https" or "http")) return null;
        return publicBaseUrl.Trim().TrimEnd('/') + url;
    }

    /// <inheritdoc cref="AbsoluteActionUrl(string?, string?)"/>
    public static string? AbsoluteActionUrl(string? url, IConfiguration configuration) =>
        AbsoluteActionUrl(url, PublicBaseUrlKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)));

    private static string Req(string? s) => string.IsNullOrWhiteSpace(s) ? "your exam" : s;
    private static string Esc(string s) => WebUtility.HtmlEncode(s);
    private static string When(DateTimeOffset at) => at.UtcDateTime.ToString("d MMM yyyy HH:mm") + " UTC";

    private static string Paragraphs(params string?[] lines) =>
        string.Join("", lines.Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => $"<p style=\"margin:0 0 .9em;color:{Ink};font-size:15px;line-height:1.55\">{l}</p>"));

    private static string Wrap(string organizationName, string lead, string bodyHtml, string? actionLabel, string? actionUrl)
    {
        var button = actionLabel is { Length: > 0 } && actionUrl is { Length: > 0 }
            ? $"""
               <table role="presentation" cellpadding="0" cellspacing="0" style="margin:1.1em 0 .2em">
                 <tr><td style="border-radius:8px;background:{Accent}">
                   <a href="{Esc(actionUrl)}" style="display:inline-block;padding:.65em 1.3em;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px">{Esc(actionLabel)}</a>
                 </td></tr>
               </table>
               """
            : "";
        return $"""
                <!doctype html>
                <html>
                <body style="margin:0;padding:0;background:{Bg};font-family:-apple-system,'Segoe UI',system-ui,sans-serif">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Bg};padding:24px 0">
                    <tr><td align="center">
                      <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:92vw;background:{Panel};border:1px solid {Line};border-radius:10px;overflow:hidden">
                        <tr><td style="padding:22px 28px;border-bottom:1px solid {Line}">
                          <span style="font-weight:700;font-size:18px;color:{Ink}">Gadfly</span>
                          <div style="color:{Muted};font-size:13px;margin-top:2px">{Esc(organizationName)}</div>
                        </td></tr>
                        <tr><td style="padding:24px 28px">
                          <p style="margin:0 0 1em;color:{Muted};font-size:14px">{Esc(lead)}</p>
                          {bodyHtml}
                          {button}
                        </td></tr>
                        <tr><td style="padding:16px 28px;border-top:1px solid {Line};color:{Muted};font-size:12px">
                          This is an automated message from Gadfly on behalf of {Esc(organizationName)}. If you weren't expecting it, you can ignore it.
                        </td></tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;
    }

    private static string PlainText(string organizationName, string lead, string bodyHtml, string? actionLabel, string? actionUrl)
    {
        // Strip the inline HTML tags added by Paragraphs/Wrap for a reasonable plain-text fallback.
        var text = System.Text.RegularExpressions.Regex.Replace(bodyHtml, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text).Trim();
        var lines = new List<string> { "Gadfly", organizationName, "", lead, "", text };
        if (actionLabel is { Length: > 0 } && actionUrl is { Length: > 0 })
        {
            lines.Add("");
            lines.Add($"{actionLabel}: {actionUrl}");
        }
        return string.Join("\n", lines);
    }
}
