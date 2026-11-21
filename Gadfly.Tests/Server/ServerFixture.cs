using System.Runtime.CompilerServices;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// One in-process Gadfly.Server for the whole server test namespace (GAD-T104): a uniquely named LocalDB
/// database created by the app's own startup migration, dropped when the run ends. The LLM panel is a
/// <see cref="ScriptedPanel"/> (swapped per test), the clock is a manual one, and Gadfly's background
/// services (job worker, retention, grading sweeps...) are not started, so nothing runs that a test did not ask for.
/// </summary>
[SetUpFixture]
public sealed class ServerFixture
{
    public static GadflyFactory? Factory { get; private set; }

    /// <summary>Why the server could not start (no LocalDB, a broken migration...); server tests are then ignored.</summary>
    public static string? Unavailable { get; private set; }

    public static string ConnectionString { get; private set; } = "";

    private string? _previousConnection;

    [OneTimeSetUp]
    public void Start()
    {
        var name = $"GadflyTests_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..48];
        ConnectionString = $@"Server=(localdb)\MSSQLLocalDB;Database={name};Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        _previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__" + GadflyDatabase.ConnectionStringName);
        Environment.SetEnvironmentVariable("ConnectionStrings__" + GadflyDatabase.ConnectionStringName, ConnectionString);
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_GADFLY_SERVER", ServerProjectDirectory());
        try
        {
            Factory = new GadflyFactory(ConnectionString);
            _ = Factory.Services;   // starts the host: migrates the new database
        }
        catch (Exception ex)
        {
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            TestContext.Progress.WriteLine("Server tests are ignored: " + Unavailable);
        }
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        try
        {
            if (Factory is not null && Unavailable is null)
            {
                await using var db = NewContext(TenantSnapshot.System);
                SqlConnection.ClearAllPools();
                await db.Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            Factory?.Dispose();
            Environment.SetEnvironmentVariable("ConnectionStrings__" + GadflyDatabase.ConnectionStringName, _previousConnection);
        }
    }

    /// <summary>A context on the test database acting as <paramref name="tenant"/>.</summary>
    public static GadflyDbContext NewContext(TenantSnapshot tenant)
    {
        var options = new DbContextOptionsBuilder<GadflyDbContext>();
        GadflyDatabase.Configure(options, ConnectionString);
        return new GadflyDbContext(options.Options, new FixedTenant(tenant));
    }

    private static string ServerProjectDirectory([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "Gadfly.Server"));
}

/// <summary>An <see cref="ITenantContext"/> fixed to one snapshot.</summary>
public sealed class FixedTenant(TenantSnapshot t) : ITenantContext
{
    public bool IsSystem => t.IsSystem;
    public Guid? UserId => t.UserId;
    public Guid? OrganizationId => t.OrganizationId;
    public string? Role => t.Role;
    public IReadOnlyList<Membership> Memberships => t.Memberships;
    public bool IsSuperAdmin => t.IsSuperAdmin;
}

/// <summary>Forwards to whichever <see cref="ScriptedPanel"/> the current test installed.</summary>
public sealed class PanelSwitch : ISocraticVotingClient
{
    public ScriptedPanel Current { get; set; } = new();

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
        Current.AskAsync(question, context, quorum, ct);

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) =>
        Current.AskWithProfilesAsync(question, context, voters, ct);

    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) =>
        Current.DecideAsync(question, options, context, ct);

    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096, double temperature = 0.2,
        CancellationToken ct = default) =>
        Current.FanOutAsync(systemPrompt, userMessage, maxTokens, temperature, ct);
}

public sealed class GadflyFactory(string connectionString) : WebApplicationFactory<GadflyDbContext>
{
    public PanelSwitch Panel { get; } = new();
    public ManualClock Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:" + GadflyDatabase.ConnectionStringName, connectionString);
        builder.UseSetting(Gadfly.Server.Platform.WebhookTargets.AllowPrivateSetting, "false");
        // Never probe or call the live LLM providers from tests (GAD-T100's "auto" probes them at startup).
        builder.UseSetting("Gadfly:AiMode", "mock");
        builder.ConfigureTestServices(services =>
        {
            // Gadfly's own background services stay off: tests drive jobs explicitly.
            foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Assembly == typeof(GadflyDbContext).Assembly).ToList())
                services.Remove(d);
            // No test ever reaches a real LLM provider: the scripted panel answers everything, and the live client
            // and its selection (which can probe providers) are never constructed.
            services.RemoveAll<ISocraticVotingClient>();
            services.AddSingleton<ISocraticVotingClient>(Panel);
            services.RemoveAll<IPanelInfo>();
            services.AddSingleton<IPanelInfo>(new TestPanelInfo());
            services.RemoveAll<LegionVotingClient>();
            services.AddSingleton<LegionVotingClient>(_ => throw new InvalidOperationException("Tests never construct the live LLM client."));
            services.RemoveAll<Gadfly.Socrates.Mock.SelectedPanel>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>Base for tests that need the server: ignores them when it couldn't start, resets the panel and clock.</summary>
[Category("Server")]
public abstract class ServerTest
{
    protected static GadflyFactory Factory => ServerFixture.Factory!;
    protected static IServiceProvider Services => Factory.Services;
    protected static ManualClock Clock => Factory.Clock;
    protected ScriptedPanel Panel { get; private set; } = new();

    [SetUp]
    public void RequireServer()
    {
        if (ServerFixture.Unavailable is not null || ServerFixture.Factory is null)
            Assert.Ignore("The test server (LocalDB) is unavailable: " + ServerFixture.Unavailable);
        Panel = new ScriptedPanel();
        Factory.Panel.Current = Panel;
        Clock.Now = DateTimeOffset.UtcNow;
    }

    protected static GadflyDbContext Db(TenantSnapshot tenant) => ServerFixture.NewContext(tenant);
    protected static GadflyDbContext SystemDb() => Db(TenantSnapshot.System);
    protected static GadflyDbContext OrgDb(Guid org) => Db(TenantSnapshot.ForOrganization(org));

    /// <summary>A DI scope acting as <paramref name="tenant"/>, for services resolved from the app.</summary>
    protected static AsyncServiceScope Scope(TenantSnapshot tenant) =>
        Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeFor(tenant);

    protected static TenantSnapshot Member(Guid org, Guid user, string role) =>
        new(false, user, org, role, [new Membership(org, role)], false);
}

/// <summary>The panel the test host reports: the scripted fake, labelled mock.</summary>
public sealed class TestPanelInfo : IPanelInfo
{
    public bool IsMock => true;
    public string Mode => PanelModes.Mock;
    public string Reason => "Tests use a scripted fake panel; no LLM provider is ever called.";
}
