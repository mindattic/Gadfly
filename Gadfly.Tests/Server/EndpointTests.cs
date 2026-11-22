using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gadfly.Server;
using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Every mapped route is default-deny unless it is on the anonymous allow-list (GAD-T41).</summary>
[TestFixture]
public class RouteAuthorizationTests : ServerTest
{
    private static IReadOnlyList<RouteEndpoint> Routes() =>
        Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static string Raw(RouteEndpoint e) => (e.RoutePattern.RawText ?? "").Trim('/');

    private static bool IsAnonymous(Endpoint e) => e.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    /// <summary>
    /// Pages that render signed-out because they share AccountPages.razor with /mfa (static SSR for antiforgery);
    /// their actions are the library's change-password and MFA-setup posts, which require sign-in (checked below).
    /// </summary>
    private static readonly string[] SharedAccountPages = ["account/change-password", "account/mfa/setup"];

    private static bool Allowed(string raw) =>
        GadflyEndpoints.AnonymousRoutes.Any(a => string.Equals(a, raw, StringComparison.OrdinalIgnoreCase))
        || SharedAccountPages.Contains(raw, StringComparer.OrdinalIgnoreCase)
        || raw.StartsWith("_framework/", StringComparison.OrdinalIgnoreCase);

    [Test]
    public void TheAccountPagesActions_RequireSignIn()
    {
        var actions = Routes().Where(r => Raw(r).StartsWith("_ma-auth/", StringComparison.OrdinalIgnoreCase)
            && (Raw(r).Contains("change-password", StringComparison.OrdinalIgnoreCase) || Raw(r).Contains("mfa/setup", StringComparison.OrdinalIgnoreCase)
                || Raw(r).Contains("mfa/enroll", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(actions.Where(IsAnonymous).Select(Raw), Is.Empty);
    }

    [Test]
    public void TheAppMapsRoutes() => Assert.That(Routes(), Has.Count.GreaterThan(10));

    [Test]
    public void EveryAnonymousRoute_IsOnTheAllowList()
    {
        var anonymous = Routes().Where(IsAnonymous).Select(Raw).Distinct().ToList();
        var unexpected = anonymous.Where(r => !Allowed(r)).ToList();
        Assert.That(unexpected, Is.Empty, "routes reachable signed-out that are not on GadflyEndpoints.AnonymousRoutes");
    }

    [Test]
    public void EveryAllowListedRoute_IsMapped_AndAnonymous()
    {
        var routes = Routes();
        foreach (var allowed in GadflyEndpoints.AnonymousRoutes)
        {
            var matches = routes.Where(r => string.Equals(Raw(r), allowed, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.That(matches, Is.Not.Empty, $"'{allowed}' is on the allow-list but not mapped");
            Assert.That(matches.Any(IsAnonymous), Is.True, $"'{allowed}' is on the allow-list but still requires sign-in");
        }
    }

    [Test]
    public void TheBlazorHubAndExamHub_AreNotAnonymous()
    {
        var hubs = Routes().Where(r => Raw(r).StartsWith("_blazor", StringComparison.OrdinalIgnoreCase)
                                       || Raw(r).StartsWith(Gadfly.Server.Sessions.SessionHub.Path.Trim('/'), StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(hubs, Is.Not.Empty);
        Assert.That(hubs.Where(IsAnonymous).Select(Raw), Is.Empty);
    }

    [Test]
    public void ApiRoutes_RequireTheApiPolicy()
    {
        var api = Routes().Where(r => Raw(r).StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(api, Is.Not.Empty);
        foreach (var r in api)
            Assert.That(r.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == GadflyPolicies.Api), Is.True, Raw(r));
    }

    private static string Concrete(string raw) =>
        "/" + Regex.Replace(raw, @"\{\*?(?<name>[^}:=?]+)(?<rest>[^}]*)\}", m =>
            m.Groups["rest"].Value.Contains("guid", StringComparison.OrdinalIgnoreCase) ? Guid.NewGuid().ToString()
            : m.Groups["rest"].Value.Contains("int", StringComparison.OrdinalIgnoreCase) ? "1"
            : "x");

    public static IEnumerable<string> ProtectedGetPaths()
    {
        if (ServerFixture.Factory is null || ServerFixture.Unavailable is not null) yield break;
        foreach (var r in Routes().Where(r => !IsAnonymous(r)))
        {
            var methods = r.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is not null && !methods.Contains("GET")) continue;
            yield return Concrete(Raw(r));
        }
    }

    [Test]
    public async Task EveryProtectedGetRoute_RefusesASignedOutVisitor()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var leaks = new List<string>();
        foreach (var path in ProtectedGetPaths().Distinct())
        {
            using var response = await client.GetAsync(path);
            var status = (int)response.StatusCode;
            var refused = status is 401 or 403 or 404 or 405
                || status is 301 or 302 or 303 or 307 && (response.Headers.Location?.ToString() ?? "").Contains("login", StringComparison.OrdinalIgnoreCase);
            if (!refused) leaks.Add($"{path} -> {status}");
        }
        Assert.That(leaks, Is.Empty);
    }

    [TestCase("/healthz")]
    [TestCase("/healthz/ready")]   // GAD-T356: 200 while no check fails (the test database is up and migrated)
    [TestCase("/login")]
    [TestCase("/forgot-password")]
    [TestCase("/openapi/v1.json")]
    public async Task AllowListedPages_AreReachableSignedOut(string path)
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync(path);
        Assert.That((int)response.StatusCode, Is.EqualTo(200), path);
    }

    [Test]
    public async Task Healthz_RevealsNoData()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var body = await client.GetStringAsync("/healthz");
        Assert.That(body, Is.EqualTo("Healthy"));
    }

    /// <summary>GAD-T190: the production exception handler re-executes /error, so it must answer anonymously with a page, not a sign-in redirect or nothing.</summary>
    [TestCase("GET")]
    [TestCase("POST")]
    public async Task ErrorPath_AnswersSignedOut_WithAPage_ForAnyVerb(string method)
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), GadflyEndpoints.ErrorPath));
        Assert.That((int)response.StatusCode, Is.EqualTo(404), "reached directly (no failed request) it is a plain not-found page");
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Something went wrong"));
    }
}

/// <summary>The platform API (GAD-T46/T69/T78): authentication, validation, tenant scoping, launch links and rate limits.</summary>
[TestFixture]
public class ApiEndpointTests : ServerTest
{
    private static async Task<(HttpClient Client, string Key)> ApiClient(Guid org)
    {
        string key;
        await using (var scope = Scope(TenantSnapshot.ForOrganization(org)))
            key = (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(org, Guid.Empty, "tests")).Key;
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return (client, key);
    }

    private static async Task<string> Error(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    // ------------------------------------------------------------------ authentication

    [TestCase(null)]
    [TestCase("Bearer gfk_00000000_notarealkey")]
    [TestCase("Bearer something-else")]
    [TestCase("Basic dXNlcjpwYXNz")]
    public async Task MissingOrUnknownKey_Is401(string? header)
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (header is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
        using var r = await client.GetAsync("/api/v1/collections");
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ValidKey_Works_AndRecordsLastUse()
    {
        var org = await Seed.OrganizationAsync();
        var (client, key) = await ApiClient(org);
        using (client)
        {
            using var r = await client.GetAsync("/api/v1/collections");
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await r.Content.ReadAsStringAsync(), Is.EqualTo("[]"));
        }
        await using var db = SystemDb();
        Assert.That((await db.ApiKeys.AsNoTracking().SingleAsync(k => k.KeyHash == ApiKeyService.Hash(key))).LastUsedAt, Is.Not.Null);
    }

    [Test]
    public async Task RevokedKey_Is401()
    {
        var org = await Seed.OrganizationAsync();
        var (client, key) = await ApiClient(org);
        await using (var db = SystemDb())
        {
            (await db.ApiKeys.SingleAsync(k => k.KeyHash == ApiKeyService.Hash(key))).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        using (client)
        using (var r = await client.GetAsync("/api/v1/collections"))
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task KeyOfADeactivatedOrganization_Is401()
    {
        var org = await Seed.OrganizationAsync();
        var (client, _) = await ApiClient(org);
        await using (var db = SystemDb())
        {
            (await db.Organizations.SingleAsync(o => o.Id == org)).IsActive = false;
            await db.SaveChangesAsync();
        }
        using (client)
        using (var r = await client.GetAsync("/api/v1/collections"))
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AKeyOnlySeesItsOwnOrganization()
    {
        var mine = await Seed.ExamAsync();
        var theirs = await Seed.ExamAsync();
        var (client, _) = await ApiClient(mine.Org);
        using (client)
        {
            using var list = await client.GetAsync("/api/v1/collections");
            var body = await list.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain(mine.CollectionId.ToString()));
            Assert.That(body, Does.Not.Contain(theirs.CollectionId.ToString()));
            Assert.That((await client.GetAsync($"/api/v1/collections/{theirs.CollectionId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.GetAsync($"/api/v1/collections/{theirs.CollectionId}/concepts")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.PostAsync($"/api/v1/assignments/{theirs.AssignmentId}/launch", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.PostAsJsonAsync($"/api/v1/exams/{theirs.ExamId}/assignments", new { userName = Seed.UserName() })).StatusCode,
                Is.EqualTo(HttpStatusCode.NotFound));
            var sessions = await client.GetStringAsync($"/api/v1/assignments/{theirs.AssignmentId}/sessions");
            Assert.That(sessions, Is.EqualTo("[]"));
        }
    }

    // ------------------------------------------------------------------ collections

    [TestCase("{\"title\": \"\"}", "title is required")]
    [TestCase("{\"title\": \"   \"}", "title is required")]
    [TestCase("{\"title\": null}", "title is required")]
    [TestCase("{}", "title is required")]
    public async Task CreateCollection_Validation(string body, string error)
    {
        var (client, _) = await ApiClient(await Seed.OrganizationAsync());
        using (client)
        {
            using var r = await client.PostAsync("/api/v1/collections", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await Error(r), Is.EqualTo(error));
        }
    }

    [Test]
    public async Task CreateCollection_TitleLength()
    {
        var (client, _) = await ApiClient(await Seed.OrganizationAsync());
        using (client)
        {
            using var tooLong = await client.PostAsJsonAsync("/api/v1/collections", new { title = new string('t', 301) });
            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await Error(tooLong), Does.Contain("at most 300"));
            using var ok = await client.PostAsJsonAsync("/api/v1/collections", new { title = "  " + new string('t', 300) + "  " });
            Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(ok.Headers.Location!.ToString(), Does.StartWith("/api/v1/collections/"));
        }
    }

    [Test]
    public async Task Process_NeedsAnExistingCollectionWithFiles()
    {
        var org = await Seed.OrganizationAsync();
        var (client, _) = await ApiClient(org);
        using (client)
        {
            Assert.That((await client.PostAsJsonAsync($"/api/v1/collections/{Guid.NewGuid()}/process", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using var created = await client.PostAsJsonAsync("/api/v1/collections", new { title = "Empty" });
            var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
            using var r = await client.PostAsJsonAsync($"/api/v1/collections/{id}/process", new { });
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await Error(r), Does.Contain("upload at least one document"));
        }
    }

    [TestCase("virus.exe", "only")]
    [TestCase("CON.txt", "reserved device name")]
    [TestCase("lpt1.md", "reserved device name")]
    [TestCase(".md", "needs a name")]
    public async Task Upload_RefusesBadFileNames_BeforeSavingAnything(string fileName, string error)
    {
        var org = await Seed.OrganizationAsync();
        var (client, _) = await ApiClient(org);
        using (client)
        {
            using var created = await client.PostAsJsonAsync("/api/v1/collections", new { title = "Uploads" });
            var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
            using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", fileName } };
            using var r = await client.PostAsync($"/api/v1/collections/{id}/documents", form);
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await Error(r), Does.Contain(error));
            Assert.That(Directory.Exists(Gadfly.Server.Sources.SourceUploads.FolderFor(org, id))
                        && Directory.EnumerateFiles(Gadfly.Server.Sources.SourceUploads.FolderFor(org, id)).Any(), Is.False);
        }
    }

    [Test]
    public async Task Upload_NeedsMultipart_AndAnExistingCollection()
    {
        var (client, _) = await ApiClient(await Seed.OrganizationAsync());
        using (client)
        {
            using var missing = await client.PostAsync($"/api/v1/collections/{Guid.NewGuid()}/documents", new StringContent("x"));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using var created = await client.PostAsJsonAsync("/api/v1/collections", new { title = "Uploads" });
            var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
            using var notForm = await client.PostAsync($"/api/v1/collections/{id}/documents", new StringContent("x"));
            Assert.That(notForm.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using var emptyForm = await client.PostAsync($"/api/v1/collections/{id}/documents", new MultipartFormDataContent { { new StringContent("v"), "field" } });
            Assert.That(emptyForm.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
    }

    // ------------------------------------------------------------------ exams and assignments

    [Test]
    public async Task CreateExam_Validation()
    {
        var w = await Seed.ExamAsync();
        var (client, _) = await ApiClient(w.Org);
        Guid emptyCollection;
        using (var created = await client.PostAsJsonAsync("/api/v1/collections", new { title = "No map yet" }))
            emptyCollection = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        using (client)
        {
            async Task<(HttpStatusCode Status, string Error)> Post(object body)
            {
                using var r = await client.PostAsJsonAsync("/api/v1/exams", body);
                return (r.StatusCode, r.StatusCode == HttpStatusCode.Created ? "" : await Error(r));
            }
            Assert.That(await Post(new { collectionId = w.CollectionId, title = "" }), Is.EqualTo((HttpStatusCode.BadRequest, "title is required")));
            Assert.That((await Post(new { collectionId = w.CollectionId, title = new string('x', 301) })).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await Post(new { collectionId = Guid.Empty, title = "T" }), Is.EqualTo((HttpStatusCode.BadRequest, "collectionId is required")));
            Assert.That((await Post(new { collectionId = w.CollectionId, title = "T", instructions = new string('i', 4001) })).Error, Does.Contain("instructions must be at most 4000"));
            Assert.That((await Post(new { collectionId = w.CollectionId, title = "T", instructions = new string('i', 4000) })).Status, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(await Post(new { collectionId = w.CollectionId, title = "T", conceptKeys = new[] { "photosynthesis", " " } }),
                Is.EqualTo((HttpStatusCode.BadRequest, "conceptKeys must not contain blank keys")));
            Assert.That((await Post(new { collectionId = w.CollectionId, title = "T", mode = "Hologram" })).Error, Does.Contain("mode must be"));
            Assert.That((await Post(new { collectionId = emptyCollection, title = "T" })).Error, Does.Contain("no concept map"));
            Assert.That((await Post(new { collectionId = Guid.NewGuid(), title = "T" })).Error, Does.Contain("no concept map"));

            using var unknown = await client.PostAsJsonAsync("/api/v1/exams", new { collectionId = w.CollectionId, title = "T", conceptKeys = new[] { "photosynthesis", "alchemy" } });
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using var doc = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
            Assert.That(doc.RootElement.GetProperty("unknown").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "alchemy" }));
        }
    }

    [Test]
    public async Task CreateExam_ClampsThePolicy_AndDefaultsToEveryConcept()
    {
        var w = await Seed.ExamAsync();
        var (client, _) = await ApiClient(w.Org);
        using (client)
        {
            using var r = await client.PostAsJsonAsync("/api/v1/exams", new { collectionId = w.CollectionId, title = "Clamped", minutes = 9999, difficulty = 42, maxTurns = 0, mode = "voice" });
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var id = doc.RootElement.GetProperty("id").GetGuid();
            Assert.That(doc.RootElement.GetProperty("conceptKeys").EnumerateArray().Select(e => e.GetString()),
                Is.EquivalentTo(new[] { "photosynthesis", "respiration" }));
            await using var db = SystemDb();
            var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == id);
            var policy = JsonSerializer.Deserialize<Gadfly.Engine.SessionPolicy>(exam.PolicyJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            Assert.That(policy.TimeBudget, Is.EqualTo(TimeSpan.FromMinutes(240)));
            Assert.That(policy.Difficulty, Is.EqualTo(5));
            Assert.That(policy.MaxTurns, Is.EqualTo(1));
            Assert.That(exam.Mode, Is.EqualTo(ExamMode.Voice));
            Assert.That(exam.Status, Is.EqualTo(ExamStatus.Published));
            Assert.That(exam.OrganizationId, Is.EqualTo(w.Org));
        }
    }

    [Test]
    public async Task Assign_Validation()
    {
        var w = await Seed.ExamAsync();
        var (client, _) = await ApiClient(w.Org);
        using (client)
        {
            async Task<HttpResponseMessage> Assign(object body) => await client.PostAsJsonAsync($"/api/v1/exams/{w.ExamId}/assignments", body);
            Assert.That((await client.PostAsJsonAsync($"/api/v1/exams/{Guid.NewGuid()}/assignments", new { userName = "x" })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using (var r = await Assign(new { userName = " " })) Assert.That(await Error(r), Is.EqualTo("userName is required"));
            using (var r = await Assign(new { userName = Seed.UserName(), attemptsAllowed = 0 })) Assert.That(await Error(r), Is.EqualTo("attemptsAllowed must be at least 1"));
            using (var r = await Assign(new { userName = new string('u', 257) })) Assert.That(await Error(r), Does.Contain("userName must be at most 256"));
            using (var r = await Assign(new { userName = Seed.UserName(), email = new string('a', 300) + "@x.test" })) Assert.That(await Error(r), Does.Contain("email must be an email address"));
            using (var r = await Assign(new { userName = Seed.UserName(), email = "not-an-address" })) Assert.That(await Error(r), Does.Contain("email must be an email address"));
            var now = DateTimeOffset.UtcNow;
            using (var r = await Assign(new { userName = Seed.UserName(), opensAt = now, closesAt = now }))
                Assert.That(await Error(r), Is.EqualTo("closesAt must be after opensAt"));
        }
    }

    [Test]
    public async Task Assign_NewStudent_ThenAgain_UpdatesTheSameAssignment()
    {
        var w = await Seed.ExamAsync();
        var (client, _) = await ApiClient(w.Org);
        var name = Seed.UserName();
        using (client)
        {
            using var first = await client.PostAsJsonAsync($"/api/v1/exams/{w.ExamId}/assignments", new { userName = name, attemptsAllowed = 2 });
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var a = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.That(a.RootElement.GetProperty("created").GetBoolean(), Is.True);
            Assert.That(a.RootElement.GetProperty("temporaryPassword").GetString(), Does.StartWith("Gf-"));
            var assignment = a.RootElement.GetProperty("assignmentId").GetGuid();
            var student = a.RootElement.GetProperty("studentUserId").GetGuid();

            using var second = await client.PostAsJsonAsync($"/api/v1/exams/{w.ExamId}/assignments", new { userName = name, attemptsAllowed = 3 });
            using var b = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.That(b.RootElement.GetProperty("created").GetBoolean(), Is.False);
            Assert.That(b.RootElement.GetProperty("assignmentId").GetGuid(), Is.EqualTo(assignment));
            Assert.That(b.RootElement.GetProperty("temporaryPassword").ValueKind, Is.EqualTo(JsonValueKind.Null));

            await using var db = SystemDb();
            Assert.That((await db.ExamAssignments.AsNoTracking().SingleAsync(x => x.Id == assignment)).AttemptsAllowed, Is.EqualTo(3));
            Assert.That((await db.OrganizationMembers.AsNoTracking().SingleAsync(m => m.OrganizationId == w.Org && m.UserId == student)).Role,
                Is.EqualTo(OrgRoles.Student));
        }
    }

    [Test]
    public async Task Assign_NeverPullsInOutsideAccountsOrChangesRoles()
    {
        var w = await Seed.ExamAsync();
        var outsider = Seed.UserName();
        await Seed.MemberAsync(Services, await Seed.OrganizationAsync(), OrgRoles.Student, outsider);
        var instructor = Seed.UserName();
        await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor, instructor);
        var gone = Seed.UserName();
        var goneId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Student, gone);
        await using (var db = SystemDb())
        {
            (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == w.Org && m.UserId == goneId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var (client, _) = await ApiClient(w.Org);
        using (client)
        {
            foreach (var (name, why) in new[] { (outsider, "not a member"), (instructor, "Instructor role"), (gone, "deactivated") })
            {
                using var r = await client.PostAsJsonAsync($"/api/v1/exams/{w.ExamId}/assignments", new { userName = name });
                Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), name);
                Assert.That(await Error(r), Does.Contain(why));
            }
        }
        await using var check = SystemDb();
        Assert.That(await check.OrganizationMembers.Where(m => m.OrganizationId == w.Org).Select(m => m.Role).ToListAsync(),
            Does.Contain(OrgRoles.Instructor), "the instructor kept their role");
    }

    // ------------------------------------------------------------------ launch links

    [Test]
    public async Task LaunchLink_SignedOutVisitorGoesToSignIn_ThenTheStartPage()
    {
        var w = await Seed.ExamAsync();
        var (client, _) = await ApiClient(w.Org);
        string url;
        using (client)
        {
            using var r = await client.PostAsync($"/api/v1/assignments/{w.AssignmentId}/launch", null);
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            url = doc.RootElement.GetProperty("url").GetString()!;
            Assert.That(doc.RootElement.GetProperty("expiresInSeconds").GetInt32(), Is.EqualTo(900));
            Assert.That(url, Does.Contain("/launch?token="));
        }
        using var anonymous = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var launch = await anonymous.GetAsync(new Uri(url).PathAndQuery);
        Assert.That((int)launch.StatusCode, Is.InRange(300, 399));
        var location = Uri.UnescapeDataString(launch.Headers.Location!.ToString());
        Assert.That(location, Does.StartWith("/login?returnUrl="));
        Assert.That(location, Does.EndWith($"/my-exams/{w.AssignmentId}/start"));
    }

    [Test]
    public async Task LaunchLink_TamperedOrExpired_IsRefused()
    {
        var w = await Seed.ExamAsync();
        var dp = Services.GetRequiredService<IDataProtectionProvider>();
        var expired = LaunchTokens.Protector(dp).Protect($"{w.AssignmentId:N}|{w.StudentId:N}", DateTimeOffset.UtcNow.AddMinutes(-1));
        var valid = LaunchTokens.Protector(dp).Protect($"{w.AssignmentId:N}|{w.StudentId:N}", TimeSpan.FromMinutes(15));
        var tampered = valid[..^4] + (valid.EndsWith("AAAA") ? "BBBB" : "AAAA");
        using var anonymous = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        foreach (var token in new[] { expired, tampered, "not-a-token" })
        {
            using var r = await anonymous.GetAsync("/launch?token=" + Uri.EscapeDataString(token));
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await r.Content.ReadAsStringAsync(), Does.Contain("expired or is not valid"));
        }
    }

    [Test]
    public void LaunchTokens_FromAnotherPurpose_DoNotVerify()
    {
        var dp = Services.GetRequiredService<IDataProtectionProvider>();
        var foreign = dp.CreateProtector("Something.Else").ToTimeLimitedDataProtector().Protect("x|y", TimeSpan.FromMinutes(5));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => LaunchTokens.Protector(dp).Unprotect(foreign));
    }

    // ------------------------------------------------------------------ webhooks, jobs, results

    [Test]
    public async Task Webhooks_CreateListDelete()
    {
        var org = await Seed.OrganizationAsync();
        var (client, _) = await ApiClient(org);
        using (client)
        {
            using (var bad = await client.PostAsJsonAsync("/api/v1/webhooks", new { url = "http://hooks.example.com/x", events = new[] { "session.completed" } }))
                Assert.That(bad.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using (var ssrf = await client.PostAsJsonAsync("/api/v1/webhooks", new { url = "https://169.254.169.254/x", events = new[] { "session.completed" } }))
                Assert.That(ssrf.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using var created = await client.PostAsJsonAsync("/api/v1/webhooks", new { url = "https://hooks.example.com/x", events = new[] { "session.completed" } });
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = doc.RootElement.GetProperty("id").GetGuid();
            Assert.That(doc.RootElement.GetProperty("secret").GetString(), Does.StartWith("whsec_"));
            Assert.That(doc.RootElement.GetProperty("events").GetArrayLength(), Is.EqualTo(1));

            var list = await client.GetStringAsync("/api/v1/webhooks");
            Assert.That(list, Does.Contain(id.ToString()).And.Not.Contain("whsec_"), "the secret is shown once");
            Assert.That((await client.DeleteAsync($"/api/v1/webhooks/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That((await client.DeleteAsync($"/api/v1/webhooks/{Guid.NewGuid()}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
        var other = await Seed.OrganizationAsync();
        Guid foreignHook;
        await using (var db = SystemDb())
        {
            var h = new Webhook { OrganizationId = other, Url = "https://hooks.example.com/y", Events = "session.completed", ProtectedSecret = "x", CreatedAt = DateTimeOffset.UtcNow };
            db.Webhooks.Add(h);
            await db.SaveChangesAsync();
            foreignHook = h.Id;
        }
        var (client2, _) = await ApiClient(org);
        using (client2)
            Assert.That((await client2.DeleteAsync($"/api/v1/webhooks/{foreignHook}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await using var check = SystemDb();
        Assert.That((await check.Webhooks.AsNoTracking().SingleAsync(h => h.Id == foreignHook)).IsActive, Is.True);
    }

    [Test]
    public async Task UnknownJobAndSession_Are404()
    {
        var other = await Seed.EndedSessionAsync();
        var (client, _) = await ApiClient(await Seed.OrganizationAsync());
        using (client)
        {
            Assert.That((await client.GetAsync($"/api/v1/jobs/{Guid.NewGuid()}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.GetAsync($"/api/v1/sessions/{other.SessionId}/result")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task SessionResult_ForTheOwnOrganization_CarriesTheReport()
    {
        var s = await Seed.EndedSessionAsync();
        var (client, _) = await ApiClient(s.World.Org);
        using (client)
        {
            using var r = await client.GetAsync($"/api/v1/sessions/{s.SessionId}/result");
            Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            Assert.That(doc.RootElement.GetProperty("recommendation").GetString(), Is.Not.Empty);
            Assert.That(doc.RootElement.GetProperty("grade").GetProperty("grade").GetString(), Is.EqualTo("B"));
        }
    }

    // ------------------------------------------------------------------ rate limits

    [Test]
    public async Task ApiRateLimit_Answers429WithRetryAfter()
    {
        var (client, _) = await ApiClient(await Seed.OrganizationAsync());
        using (client)
        {
            HttpResponseMessage? limited = null;
            for (var i = 0; i < 90 && limited is null; i++)
            {
                var r = await client.GetAsync($"/api/v1/jobs/{Guid.Empty}");
                if (r.StatusCode == HttpStatusCode.TooManyRequests) limited = r;
                else r.Dispose();
            }
            Assert.That(limited, Is.Not.Null, "a burst beyond 60 calls must be limited");
            Assert.That(limited!.Headers.RetryAfter, Is.Not.Null);
            limited.Dispose();
        }
        // Another key has its own budget.
        var (fresh, _) = await ApiClient(await Seed.OrganizationAsync());
        using (fresh)
            Assert.That((await fresh.GetAsync($"/api/v1/jobs/{Guid.Empty}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
