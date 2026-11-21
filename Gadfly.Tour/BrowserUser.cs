using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gadfly.Tour;

/// <summary>
/// One person using the site the way a browser does: a cookie jar, the sign-in form with its antiforgery token,
/// redirects followed by hand (so the tour can see where it was sent), and a SessionHub connection that carries
/// the same cookies.
/// </summary>
public sealed partial class BrowserUser : IDisposable
{
    private readonly string _baseUrl;
    private readonly bool _verbose;

    public string UserName { get; }
    public CookieContainer Cookies { get; } = new();
    public HttpClient Http { get; }

    public BrowserUser(string baseUrl, string userName, bool verbose = false)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _verbose = verbose;
        UserName = userName;
        Http = new HttpClient(CreateHandler(Cookies), disposeHandler: true) { BaseAddress = new Uri(_baseUrl + "/"), Timeout = TimeSpan.FromSeconds(120) };
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GadflyTour", "1.0"));
    }

    /// <summary>The local development certificate is accepted for localhost only.</summary>
    public static SocketsHttpHandler CreateHandler(CookieContainer? cookies = null) => new()
    {
        CookieContainer = cookies ?? new CookieContainer(),
        UseCookies = cookies is not null,
        AllowAutoRedirect = false,
        SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || sender is HttpRequestMessage { RequestUri.IsLoopback: true } },
    };

    /// <summary>A page or endpoint, following redirects like a browser. Returns the final status, body and path.</summary>
    public async Task<(HttpStatusCode Status, string Body, string FinalPath)> GetAsync(string path, bool follow = true)
    {
        var url = path;
        for (var hop = 0; hop < 8; hop++)
        {
            using var response = await Http.GetAsync(url.TrimStart('/'));
            var body = await response.Content.ReadAsStringAsync();
            if (_verbose) Console.WriteLine($"      [{UserName}] GET {url} -> {(int)response.StatusCode}");
            if (follow && (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                url = next.IsAbsoluteUri ? next.PathAndQuery : next.OriginalString;
                continue;
            }
            return (response.StatusCode, body, new Uri(new Uri(_baseUrl), url).AbsolutePath);
        }
        throw new TourAssertException($"Too many redirects from {path}.");
    }

    private string? _antiforgeryToken;

    /// <summary>A JSON endpoint read with this user's cookies (no redirects followed). Body is default when not JSON.</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> GetJsonAsync(string path)
    {
        using var response = await Http.GetAsync(path.TrimStart('/'));
        if (_verbose) Console.WriteLine($"      [{UserName}] GET {path} -> {(int)response.StatusCode}");
        return (response.StatusCode, await ReadJsonAsync(response));
    }

    /// <summary>
    /// A JSON POST the way a signed-in browser script sends one: the cookies plus the antiforgery request token
    /// (fetched once from <paramref name="tokenPath"/>) in the RequestVerificationToken header.
    /// </summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> PostJsonAsync(string path, object body, string tokenPath)
    {
        if (_antiforgeryToken is null)
        {
            var (status, token) = await GetJsonAsync(tokenPath);
            Expect.True(status == HttpStatusCode.OK && token.ValueKind == JsonValueKind.Object, $"GET {tokenPath} answered {(int)status}");
            _antiforgeryToken = token.GetProperty("token").GetString();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/')) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", _antiforgeryToken);
        using var response = await Http.SendAsync(request);
        if (_verbose) Console.WriteLine($"      [{UserName}] POST {path} -> {(int)response.StatusCode}");
        return (response.StatusCode, await ReadJsonAsync(response));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return default;
        try { return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone(); }
        catch (JsonException) { return default; }
    }

    /// <summary>Signs in through the real form: GET /login, copy the antiforgery token and hidden fields, POST it.</summary>
    public async Task SignInAsync(string password)
    {
        var (status, html, _) = await GetAsync("/login?returnUrl=%2F");
        Expect.True(status == HttpStatusCode.OK, $"GET /login answered {(int)status}");
        var form = Forms(html).FirstOrDefault(f => f.Inputs.Any(i => i.Type == "password"))
            ?? throw new TourAssertException("The sign-in page has no form with a password field.");
        Expect.True(form.Inputs.Any(i => i.Name.Contains("RequestVerificationToken", StringComparison.OrdinalIgnoreCase)),
            "The sign-in form carries no antiforgery token.");

        var fields = new List<KeyValuePair<string, string>>();
        var userField = form.Inputs.FirstOrDefault(i => i.Type is "text" or "email" && i.Name.Length > 0)
            ?? form.Inputs.FirstOrDefault(i => Regex.IsMatch(i.Name, "user|email|login", RegexOptions.IgnoreCase) && i.Type != "hidden")
            ?? throw new TourAssertException("The sign-in form has no user-name field.");
        foreach (var input in form.Inputs.Where(i => i.Name.Length > 0))
        {
            if (input.Type is "submit" or "button" or "checkbox") continue;
            var value = input == userField ? UserName : input.Type == "password" ? password : input.Value;
            fields.Add(new(input.Name, value));
        }
        var action = string.IsNullOrEmpty(form.Action) ? "/login" : form.Action;
        using var post = await Http.PostAsync(action.TrimStart('/'), new FormUrlEncodedContent(fields));
        if (_verbose) Console.WriteLine($"      [{UserName}] POST {action} -> {(int)post.StatusCode} {post.Headers.Location}");
        var location = post.Headers.Location?.OriginalString ?? "";
        Expect.True((int)post.StatusCode is >= 300 and < 400, $"Sign-in POST answered {(int)post.StatusCode}, not a redirect.");
        Expect.True(!location.Contains("error", StringComparison.OrdinalIgnoreCase) && !location.Contains("/login", StringComparison.OrdinalIgnoreCase),
            $"Sign-in was refused (redirected to {location}).");

        var (homeStatus, _, finalPath) = await GetAsync("/");
        Expect.True(homeStatus == HttpStatusCode.OK && !finalPath.StartsWith("/login", StringComparison.OrdinalIgnoreCase),
            $"After sign-in, / ended at {finalPath} ({(int)homeStatus}).");
        if (finalPath.StartsWith("/account/", StringComparison.OrdinalIgnoreCase))
            throw new TourAssertException($"After sign-in the account was sent to {finalPath} (a forced step the demo accounts should not have).");
    }

    /// <summary>A SessionHub connection signed in as this user (same cookies, local certificate accepted).</summary>
    public HubConnection Hub(string path = "/hubs/session") =>
        new HubConnectionBuilder()
            .WithUrl(_baseUrl + path, o =>
            {
                o.Cookies = Cookies;
                if (!new Uri(_baseUrl).IsLoopback) return;
                o.HttpMessageHandlerFactory = inner =>
                {
                    if (inner is HttpClientHandler h) h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                    return inner;
                };
                o.WebSocketConfiguration = ws => ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            })
            .Build();

    public void Dispose() => Http.Dispose();

    // ------------------------------------------------------------------ minimal form parsing

    public sealed record Input(string Name, string Type, string Value);
    public sealed record Form(string Action, IReadOnlyList<Input> Inputs);

    public static List<Form> Forms(string html) =>
        FormTag().Matches(html).Select(m => new Form(
            WebUtility.HtmlDecode(Attr(m.Groups[1].Value, "action") ?? ""),
            InputTag().Matches(m.Groups[2].Value).Select(i => new Input(
                WebUtility.HtmlDecode(Attr(i.Value, "name") ?? ""),
                (Attr(i.Value, "type") ?? "text").ToLowerInvariant(),
                WebUtility.HtmlDecode(Attr(i.Value, "value") ?? ""))).ToList())).ToList();

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value) : null;
    }

    [GeneratedRegex(@"<form\b([^>]*)>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FormTag();

    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTag();
}
