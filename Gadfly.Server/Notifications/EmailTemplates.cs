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

    /// <param name="language">
    /// The recipient's language (GAD-T544): "ko" renders the student emails in Korean (the staff-only test email stays English);
    /// anything else, English as before.
    /// </param>
    public static RenderedEmail Render(string kind, EmailData d, string? language = null)
    {
        if (Gadfly.Engine.ExamLanguage.IsKorean(language) && Korean(kind, d) is { } ko)
        {
            var koHtml = Wrap(d.OrganizationName, ko.Lead, ko.Body, ko.Action.Label ?? d.ActionLabel, ko.Action.Url ?? d.ActionUrl, korean: true);
            var koText = PlainText(d.OrganizationName, ko.Lead, ko.Body, ko.Action.Label ?? d.ActionLabel, ko.Action.Url ?? d.ActionUrl);
            return new RenderedEmail(ko.Subject, koHtml, koText);
        }
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

    /// <summary>
    /// The student emails in Korean (GAD-T544): the same facts and the same single action, in polite 합쇼체/해요체; terminology per
    /// docs/LOCALIZATION.md. Null for a kind with no Korean version (it is then sent in English).
    /// </summary>
    private static (string Subject, string Lead, string Body, (string? Label, string? Url) Action)? Korean(string kind, EmailData d)
    {
        string Title() => string.IsNullOrWhiteSpace(d.ExamTitle) ? "시험" : d.ExamTitle!;
        string KoWhen(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy년 M월 d일 HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ko-KR")) + " UTC";
        return kind switch
        {
            EmailKinds.ExamAssigned => (
                $"새 시험이 배정되었습니다: {Title()}",
                $"{d.OrganizationName}에서 시험을 배정했습니다.",
                Paragraphs(
                    $"<strong>{Esc(Title())}</strong> 시험이 내 시험 목록에 추가되었습니다.",
                    d.DueAtUtc is { } due ? $"마감: {KoWhen(due)}" : null,
                    d.AttemptsAllowed is { } n ? $"응시 기회는 {n}회입니다." : null),
                ("내 시험 열기", d.ActionUrl)),
            EmailKinds.Reminder => (
                $"알림: {Title()} 마감이 다가오고 있습니다",
                $"{d.OrganizationName}에서 보내는 알림입니다.",
                Paragraphs(
                    $"<strong>{Esc(Title())}</strong> 시험의 마감이 {(d.DueAtUtc is { } due ? KoWhen(due) + "입니다" : "곧입니다")}. 아직 시험을 마치지 않으셨어요.",
                    "이미 응시하셨다면 따로 하실 일은 없습니다."),
                ("내 시험 열기", d.ActionUrl)),
            EmailKinds.ResultReleased => (
                $"{Title()} 결과가 나왔습니다",
                $"{d.OrganizationName}에서 결과를 공개했습니다.",
                Paragraphs(
                    $"<strong>{Esc(Title())}</strong> 시험의 결과를 확인하실 수 있습니다.",
                    d.Score is { } s ? $"결과: <strong>{Esc(s)}</strong>" : null,
                    "잘못된 부분이 있다면 결과 페이지에서 이의 신청을 하실 수 있습니다."),
                ("내 결과 보기", d.ActionUrl)),
            EmailKinds.AppealReceived => (
                $"{Title()} 이의 신청이 접수되었습니다",
                "이의 신청이 제출되었습니다.",
                Paragraphs(
                    $"<strong>{Esc(Title())}</strong>에 대한 이의 신청이 접수되어 담당 교수자의 검토를 기다리고 있습니다.",
                    "검토하는 동안에도 이전에 공개된 결과는 계속 볼 수 있습니다. 결정이 나면 다시 이메일로 알려 드리겠습니다."),
                (null, null)),
            EmailKinds.AppealDecided => (
                $"{Title()} 이의 신청 결과가 나왔습니다",
                "이의 신청에 대한 결정이 내려졌습니다.",
                Paragraphs(
                    $"<strong>{Esc(Title())}</strong>에 대한 이의 신청 결과: <strong>{Esc(d.AppealOutcome ?? "처리 완료")}</strong>",
                    d.AppealResolution is { Length: > 0 } r ? Esc(r) : null),
                ("내 결과 보기", d.ActionUrl)),
            EmailKinds.AttemptReset => (
                $"{Title()} 응시 기록이 초기화되었습니다",
                $"{d.OrganizationName}에서 시험을 초기화했습니다.",
                Paragraphs(
                    $"기관에서 <strong>{Esc(Title())}</strong>의 응시 기록을 초기화했습니다. 이전의 답변, 점수, 성적은 더 이상 반영되지 않습니다.",
                    "처음부터 다시 응시하실 수 있습니다."),
                ("내 시험 열기", d.ActionUrl)),
            EmailKinds.LmsLink => (
                "LMS 로그인을 Gadfly 계정과 연결해 주세요",
                $"{d.OrganizationName}: 본인 확인이 필요합니다.",
                Paragraphs(
                    "누군가 이 이메일 주소로 학교 학습 관리 시스템(LMS)에서 Gadfly 시험을 열고, 새 계정 대신 이미 가지고 계신 Gadfly 계정을 사용하겠다고 요청했습니다.",
                    "본인이 요청하셨다면 24시간 안에 <strong>같은 브라우저에서</strong> 아래 링크를 열어 주세요. 그 뒤로는 수업에서 Gadfly를 열면 이 계정으로 연결됩니다.",
                    "본인이 아니라면 이 이메일은 무시하셔도 됩니다. 아무것도 바뀌지 않으며, 링크는 저절로 만료됩니다."),
                ("LMS 로그인 연결하기", d.ActionUrl)),
            _ => ((string Subject, string Lead, string Body, (string? Label, string? Url) Action)?)null,
        };
    }

    private static string Req(string? s) => string.IsNullOrWhiteSpace(s) ? "your exam" : s;
    private static string Esc(string s) => WebUtility.HtmlEncode(s);
    private static string When(DateTimeOffset at) => at.UtcDateTime.ToString("d MMM yyyy HH:mm") + " UTC";

    private static string Paragraphs(params string?[] lines) =>
        string.Join("", lines.Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => $"<p style=\"margin:0 0 .9em;color:{Ink};font-size:15px;line-height:1.55\">{l}</p>"));

    private static string Wrap(string organizationName, string lead, string bodyHtml, string? actionLabel, string? actionUrl, bool korean = false)
    {
        var footer = korean
            ? $"이 메일은 {Esc(organizationName)}을(를) 대신해 Gadfly가 자동으로 보낸 메일입니다. 받을 예정이 없었다면 무시하셔도 됩니다."
            : $"This is an automated message from Gadfly on behalf of {Esc(organizationName)}. If you weren't expecting it, you can ignore it.";
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
                <html lang="{(korean ? "ko" : "en")}">
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
                          {footer}
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
