// Gadfly hosted-API sample: a third-party LMS integrating Gadfly with an organization API key (docs/API.md).
//
//   1. creates a source collection, uploads a markdown source and processes it into a concept map
//   2. creates and publishes an exam, assigns a student, asks for the student's signed launch link
//   3. registers a webhook endpoint THIS program hosts, and verifies the HMAC signature of what it receives
//   4. reads the finished attempt's result
//
// Run Gadfly.Server first (mock AI mode needs no provider keys), then see samples/HostedApi/README.md.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var opts = Options.Parse(args);
if (opts.ApiKey is null)
{
    Console.Error.WriteLine("Set GADFLY_API_KEY (or pass --api-key). See samples/HostedApi/README.md for how to provision one.");
    return 2;
}

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var gadfly = new HttpClient(new SocketsHttpHandler
{
    // A local Gadfly.Server uses the ASP.NET development certificate; accept it for loopback only.
    SslOptions = { RemoteCertificateValidationCallback = (sender, _, _, errors) =>
        errors == System.Net.Security.SslPolicyErrors.None || sender is HttpRequestMessage { RequestUri.IsLoopback: true } },
}) { BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
gadfly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

// ---------------------------------------------------------------- the LMS's own webhook endpoint

Guid? webhookId = null;                             // so the sample can deactivate its own webhook when it finishes
string? webhookSecret = null;                       // the whsec_... string returned once at registration
var received = new TaskCompletionSource<WebhookDelivery>(TaskCreationOptions.RunContinuationsAsynchronously);
var seenDeliveries = new HashSet<string>();         // X-Gadfly-Delivery: retries repeat the id, so duplicates are ignored

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"https://localhost:{opts.HookPort}");
var hooks = builder.Build();
hooks.MapPost("/hooks/gadfly", async (HttpRequest request) =>
{
    // The RAW body, exactly as received: the signature is over these bytes, not over re-serialized JSON.
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();
    var signature = request.Headers["X-Gadfly-Signature"].ToString();
    var delivery = request.Headers["X-Gadfly-Delivery"].ToString();
    var eventType = request.Headers["X-Gadfly-Event"].ToString();

    if (webhookSecret is null || !WebhookVerifier.Verify(webhookSecret, signature, body, DateTimeOffset.UtcNow))
    {
        Console.WriteLine($"  webhook {delivery}: signature REJECTED");
        return Results.Unauthorized();
    }
    lock (seenDeliveries)
        if (!seenDeliveries.Add(delivery)) return Results.NoContent();      // a retry of one we already handled
    Console.WriteLine($"  webhook {delivery}: {eventType}, signature verified ({signature[..Math.Min(24, signature.Length)]}...)");
    received.TrySetResult(new WebhookDelivery(eventType, delivery, signature, body));
    return Results.NoContent();                                             // any 2xx within 15 seconds acknowledges it
});
await hooks.StartAsync();

try
{
    // ------------------------------------------------------------ 1. source material
    Step("Create a collection");
    var collection = await Post<IdResponse>("api/v1/collections", new { title = $"Sample LMS: multiplication {DateTime.UtcNow:HHmmss}" });
    Console.WriteLine($"  collection {collection.Id}");

    Step("Upload a source document");
    var material = Path.Combine(AppContext.BaseDirectory, "multiplication.md");
    using (var form = new MultipartFormDataContent())
    {
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(material)), "file", "multiplication.md");
        using var upload = await gadfly.PostAsync($"api/v1/collections/{collection.Id}/documents", form);
        await EnsureAsync(upload);
        Console.WriteLine($"  {await upload.Content.ReadAsStringAsync()}");
    }

    Step("Process the collection (the panel builds the concept map and rubrics)");
    var job = await Post<JobResponse>($"api/v1/collections/{collection.Id}/process", new { rebuildConceptMap = false });
    for (var i = 0; ; i++)
    {
        var status = await gadfly.GetFromJsonAsync<JobStatus>($"api/v1/jobs/{job.JobId}", json) ?? throw new InvalidOperationException("no job");
        Console.WriteLine($"  job {status.State} {status.Progress:P0} {status.ProgressMessage}");
        if (status.State == "Succeeded") break;
        if (status.State is "Failed" or "Cancelled") throw new InvalidOperationException($"Processing {status.State}: {status.Error}");
        if (i > 120) throw new TimeoutException("Processing did not finish in time.");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    var concepts = await gadfly.GetFromJsonAsync<JsonElement>($"api/v1/collections/{collection.Id}/concepts", json);
    var keys = concepts.GetProperty("concepts").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToList();
    Console.WriteLine($"  {keys.Count} concepts: {string.Join(", ", keys)}");

    // ------------------------------------------------------------ 2. exam, student, launch link
    Step("Create and publish an exam");
    var exam = await Post<IdResponse>("api/v1/exams", new
    {
        collectionId = collection.Id,
        title = "Sample LMS: multiplication oral check",
        conceptKeys = keys.Take(2),
        minutes = 20,
        difficulty = 3,
        maxTurns = 4,
        mode = "Text",
        instructions = "Explain in your own words. 'I don't know' is fine.",
    });
    Console.WriteLine($"  exam {exam.Id}");

    Step("Register a webhook endpoint (hosted by this program)");
    var hookUrl = opts.HookPublicUrl ?? $"https://localhost:{opts.HookPort}/hooks/gadfly";
    var hook = await Post<WebhookRegistration>("api/v1/webhooks", new { url = hookUrl, events = new[] { "session.completed" } });
    webhookId = hook.Id;
    webhookSecret = hook.Secret;                       // shown once; a real app stores it in a secret manager
    Console.WriteLine($"  webhook {hook.Id} -> {hook.Url}, secret {hook.Secret[..10]}... (stored)");

    Step("Assign the student");
    var assignment = await Post<AssignmentResult>($"api/v1/exams/{exam.Id}/assignments", new
    {
        userName = opts.Student,
        email = $"{opts.Student}@example.edu",
        attemptsAllowed = 1,
    });
    Console.WriteLine($"  assignment {assignment.AssignmentId} for '{opts.Student}' (assignment {(assignment.Created ? "created" : "updated")}; the student {(assignment.TemporaryPassword is null ? "was already a member" : "is a new account")})");
    if (assignment.TemporaryPassword is not null)
        Console.WriteLine($"  temporary password (shown once, hand it to the student): {assignment.TemporaryPassword}");

    Step("Get a signed launch link");
    var launch = await Post<LaunchLink>($"api/v1/assignments/{assignment.AssignmentId}/launch", null);
    Console.WriteLine($"  {launch.Url[..Math.Min(70, launch.Url.Length)]}... (expires in {launch.ExpiresInSeconds}s)");
    Console.WriteLine("  Your app redirects the student's browser to this URL when they click \"Start exam\".");

    // ------------------------------------------------------------ 3. the student takes the exam
    if (opts.StudentPassword is not null)
    {
        Step($"Simulate the student's browser taking the exam as '{opts.Student}'");
        using var student = new StudentSimulator(opts.BaseUrl);
        var answered = await student.TakeExamAsync(launch.Url, assignment.AssignmentId, opts.Student, opts.StudentPassword);
        Console.WriteLine($"  exam finished after {answered} answer(s)");
    }
    else
    {
        Step("Waiting for the student to take the exam");
        Console.WriteLine($"  Open the launch link in a browser, sign in as '{opts.Student}', take the exam. Waiting up to {opts.WaitMinutes} minutes for the webhook.");
    }

    // ------------------------------------------------------------ 4. the webhook and the result
    Step("Wait for the session.completed webhook");
    var winner = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromMinutes(opts.WaitMinutes)));
    if (winner != received.Task) throw new TimeoutException("No webhook arrived. Is the webhook URL reachable from the server? (docs/API.md, Webhooks)");
    var hookDelivery = await received.Task;
    using var payload = JsonDocument.Parse(hookDelivery.Body);
    var data = payload.RootElement.GetProperty("data");
    Console.WriteLine($"  type {payload.RootElement.GetProperty("type").GetString()}, session {data.GetProperty("sessionId")}, " +
                      $"endReason {data.GetProperty("endReason")}, recommendation {data.GetProperty("recommendation")}, overall {data.GetProperty("overallScore")}");

    Step("Prove the check works: a tampered body and a stale timestamp are refused");
    var tampered = WebhookVerifier.Verify(webhookSecret!, hookDelivery.Signature, hookDelivery.Body.Replace("session.completed", "session.deleted"), DateTimeOffset.UtcNow);
    var stale = WebhookVerifier.Verify(webhookSecret!, hookDelivery.Signature, hookDelivery.Body, DateTimeOffset.UtcNow.AddHours(1));
    var genuine = WebhookVerifier.Verify(webhookSecret!, hookDelivery.Signature, hookDelivery.Body, DateTimeOffset.UtcNow);
    Console.WriteLine($"  genuine delivery verifies: {genuine}; tampered body verifies: {tampered}; one hour later verifies: {stale}");
    if (!genuine || tampered || stale) throw new InvalidOperationException("Webhook signature verification misbehaved.");

    Step("Read the attempt's result");
    var sessionId = data.GetProperty("sessionId").GetGuid();
    var result = await gadfly.GetFromJsonAsync<JsonElement>($"api/v1/sessions/{sessionId}/result", json);
    Console.WriteLine($"  state {result.GetProperty("state")}, recommendation {result.GetProperty("recommendation")}, overall {result.GetProperty("overallScore")}");
    foreach (var c in result.GetProperty("report").GetProperty("concepts").EnumerateArray())
        Console.WriteLine($"    {c.GetProperty("title").GetString(),-40} mastery {c.GetProperty("mastery").GetDouble():P0}  {c.GetProperty("status").GetString()}");
    Console.WriteLine("  (The AI only recommends. The instructor decides the grade: result.grade is null until they do.)");

    Console.WriteLine();
    Console.WriteLine("DONE: collection, exam, assignment, launch link, webhook with verified signature, result.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    return 1;
}
finally
{
    // Housekeeping: deactivate the webhook so the next run (or a dead endpoint) does not keep receiving deliveries.
    if (webhookId is { } id)
    {
        try { using var _ = await gadfly.DeleteAsync($"api/v1/webhooks/{id}"); }
        catch (HttpRequestException) { }
    }
    await hooks.StopAsync();
}

// ---------------------------------------------------------------- helpers

static void Step(string title) => Console.WriteLine($"\n== {title}");

async Task EnsureAsync(HttpResponseMessage response)
{
    if (response.IsSuccessStatusCode) return;
    var body = await response.Content.ReadAsStringAsync();
    throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");
}

async Task<T> Post<T>(string path, object? body)
{
    using var response = await gadfly.PostAsJsonAsync(path, body ?? new { }, json);
    await EnsureAsync(response);
    return await response.Content.ReadFromJsonAsync<T>(json) ?? throw new InvalidOperationException($"Empty response from {path}");
}

record IdResponse(Guid Id);
record JobResponse(Guid JobId);
record JobStatus(Guid Id, string State, double Progress, string? ProgressMessage, string? Error);
record WebhookRegistration(Guid Id, string Url, string[] Events, string Secret);
record AssignmentResult(Guid AssignmentId, Guid StudentUserId, bool Created, string? TemporaryPassword);
record LaunchLink(string Url, int ExpiresInSeconds);
record WebhookDelivery(string EventType, string DeliveryId, string Signature, string Body);

sealed record Options(string BaseUrl, string? ApiKey, int HookPort, string? HookPublicUrl, string Student, string? StudentPassword, int WaitMinutes)
{
    public static Options Parse(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        return new Options(
            BaseUrl: Arg("--url") ?? Environment.GetEnvironmentVariable("GADFLY_URL") ?? "https://localhost:7311",
            ApiKey: Arg("--api-key") ?? Environment.GetEnvironmentVariable("GADFLY_API_KEY"),
            HookPort: int.TryParse(Arg("--hook-port"), out var p) ? p : 7401,
            HookPublicUrl: Arg("--hook-public-url"),
            Student: Arg("--student") ?? $"lms-student-{Guid.NewGuid():N}"[..20],
            StudentPassword: Arg("--student-password") ?? Environment.GetEnvironmentVariable("GADFLY_STUDENT_PASSWORD"),
            WaitMinutes: int.TryParse(Arg("--wait-minutes"), out var w) ? w : 10);
    }
}
