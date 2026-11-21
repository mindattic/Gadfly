using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Stands in for the student's BROWSER so the sample can run unattended: it signs in through the sign-in form, opens the
/// launch link, and answers the exam over the exam room's SessionHub (/hubs/session). In real life the student does this
/// in a browser and your app never touches it; the hub is the exam room's own protocol, NOT part of the platform API.
/// </summary>
sealed class StudentSimulator : IDisposable
{
    private static readonly string[] Answers =
    [
        "Multiplication means adding equal groups. Three bags with four apples in each bag is four plus four plus four, which is twelve, because every group has the same size.",
        "It only works when the groups are equal, because repeated addition needs the same number each time. With unequal groups you have to add them one by one.",
        "The three is the number of groups and the four is how many are in each group, so they stand for different things even though three times four and four times three are both twelve.",
        "If I double the number of bags the total doubles too, because there are twice as many equal groups.",
    ];

    private readonly string _baseUrl;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;

    public StudentSimulator(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient(new SocketsHttpHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = false,
            SslOptions = { RemoteCertificateValidationCallback = (sender, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || sender is HttpRequestMessage { RequestUri.IsLoopback: true } },
        }) { BaseAddress = new Uri(_baseUrl + "/") };
    }

    /// <summary>Signs in, follows the launch link, takes the exam to its end and returns the number of questions answered.</summary>
    public async Task<int> TakeExamAsync(string launchUrl, Guid assignmentId, string userName, string password)
    {
        await SignInAsync(userName, password);

        // The launch link, opened signed in as the student it was made for, goes straight to the exam's get-ready page.
        using var launch = await _http.GetAsync(launchUrl);
        var location = launch.Headers.Location?.OriginalString ?? "";
        Console.WriteLine($"  launch link -> HTTP {(int)launch.StatusCode} {location}");
        if (!location.Contains($"/my-exams/{assignmentId}/start", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The launch link did not lead to the exam (it went to '{location}').");

        var hub = new HubConnectionBuilder().WithUrl(_baseUrl + "/hubs/session", o =>
        {
            o.Cookies = _cookies;
            o.HttpMessageHandlerFactory = inner =>
            {
                if (inner is HttpClientHandler h) h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;   // local dev certificate
                return inner;
            };
            o.WebSocketConfiguration = ws => ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }).Build();
        await using var _ = hub;
        await hub.StartAsync();

        // The student accepts the notice first; the server records the hash of the text they saw.
        var consent = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("The student accepts the notice for the recorded exam.")));
        var sessionId = await hub.InvokeAsync<Guid>("Start", assignmentId, consent);
        var step = await hub.InvokeAsync<JsonElement>("Current", sessionId);

        var answered = 0;
        while (!Ended(step) && answered < Answers.Length)
        {
            Console.WriteLine($"  Examiner: {Text(step, "question")}");
            var answer = Answers[answered++];
            Console.WriteLine($"  Student:  {answer}");
            step = await hub.InvokeAsync<JsonElement>("Answer", sessionId, new { text = answer, mode = "text" });
            // A neutral clarification request about the answer just given: restate it plainly and carry on.
            for (var i = 0; i < 3 && step.TryGetProperty("verification", out var v) && v.ValueKind == JsonValueKind.Object; i++)
            {
                var id = v.GetProperty("responseId").GetGuid();
                step = Text(v, "kind") == "transcript"
                    ? await hub.InvokeAsync<JsonElement>("ConfirmTranscript", sessionId, id)
                    : await hub.InvokeAsync<JsonElement>("Clarify", sessionId, id, "I mean the number of groups times the size of each group gives the total.");
            }
        }
        if (!Ended(step)) await hub.InvokeAsync<JsonElement>("Finish", sessionId);
        return answered;
    }

    private static bool Ended(JsonElement step) =>
        step.TryGetProperty("state", out var s) && (s.ValueKind == JsonValueKind.Number ? s.GetInt32() is 4 or 5 : s.GetString() is "Ended" or "Failed");

    private static string Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>The sign-in form: GET /login, copy its hidden antiforgery field, POST the credentials.</summary>
    private async Task SignInAsync(string userName, string password)
    {
        var html = await _http.GetStringAsync("login?returnUrl=%2F");
        var timeout = TimeSpan.FromSeconds(5);
        var match = Regex.Matches(html, @"<form\b([^>]*)>(.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout)
            .FirstOrDefault(m => Regex.IsMatch(m.Groups[2].Value, @"type\s*=\s*""password""", RegexOptions.IgnoreCase, timeout))
            ?? throw new InvalidOperationException("The sign-in page has no form with a password field.");
        var form = match.Groups[2].Value;
        var action = Regex.Match(match.Groups[1].Value, @"action\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase, timeout) is { Success: true } a
            ? WebUtility.HtmlDecode(a.Groups[1].Value) : "/login";   // the form says where it posts (the auth library's own path)
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(form, @"<input\b[^>]*>", RegexOptions.IgnoreCase, timeout))
        {
            string? Attr(string name) => Regex.Match(input.Value, $@"\b{name}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase, timeout) is { Success: true } m
                ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
            var name = Attr("name");
            var type = (Attr("type") ?? "text").ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || type is "submit" or "button" or "checkbox") continue;
            fields.Add(new(name, type == "password" ? password : type == "hidden" ? Attr("value") ?? "" : userName));
        }
        using var post = await _http.PostAsync(action.TrimStart('/'), new FormUrlEncodedContent(fields));
        var next = post.Headers.Location?.OriginalString ?? "";
        if ((int)post.StatusCode is < 300 or >= 400 || next.Contains("error", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Sign-in as '{userName}' was refused (HTTP {(int)post.StatusCode} {next}).");
        Console.WriteLine($"  signed in as {userName}");
    }

    public void Dispose() => _http.Dispose();
}
