using Gadfly.Server.Admin;
using Gadfly.Server.Diagnostics;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The system status checks behind the page and /healthz/ready (GAD-T302, GAD-T353, GAD-T356) and the in-memory error list (GAD-T355).</summary>
[TestFixture]
public class SystemReadinessTests
{
    private static SystemReadinessService.ReadinessInputs Healthy(bool development = false) => new(
        DatabaseHealthy: true, MigrationsRead: true, PendingMigrations: 0, MockPanel: false, Providers: 2, FailingProviders: 0,
        Voice: VoiceMode.Live, VoiceCalls: [], JobsFailed24h: 0, OldestQueuedWait: null, DeliveriesGaveUp24h: 0, DeliveriesRetrying: 0,
        ErrorsLastHour: 0, Development: development);

    private static CheckState State(IReadOnlyList<ReadinessCheck> checks, string name) => checks.Single(c => c.Name == name).State;

    [Test]
    public void AllWell_EveryCheckIsOk()
    {
        var checks = SystemReadinessService.Evaluate(Healthy());
        Assert.That(checks.Select(c => c.Name), Is.EquivalentTo(new[] { "database", "migrations", "ai-panel", "voice", "jobs", "webhooks", "errors" }));
        Assert.That(checks.All(c => c.State == CheckState.Ok), Is.True);
    }

    [Test]
    public void DatabaseDown_OrMigrationsPending_Fail()
    {
        var down = SystemReadinessService.Evaluate(Healthy() with { DatabaseHealthy = false });
        Assert.That(State(down, "database"), Is.EqualTo(CheckState.Failing));
        Assert.That(State(down, "migrations"), Is.EqualTo(CheckState.Failing));
        var behind = SystemReadinessService.Evaluate(Healthy() with { PendingMigrations = 2 });
        Assert.That(State(behind, "migrations"), Is.EqualTo(CheckState.Failing));
        Assert.That(behind.Single(c => c.Name == "migrations").Summary, Does.Contain("2 migration"));
    }

    [Test]
    public void EverythingElse_OnlyWarns()
    {
        var checks = SystemReadinessService.Evaluate(Healthy() with
        {
            MockPanel = true, Voice = VoiceMode.Off, JobsFailed24h = 3, DeliveriesGaveUp24h = 1, ErrorsLastHour = 4,
        });
        Assert.That(checks.Any(c => c.State == CheckState.Failing), Is.False, "only the database and migrations make the server unready");
        foreach (var name in new[] { "ai-panel", "voice", "jobs", "webhooks", "errors" })
            Assert.That(State(checks, name), Is.EqualTo(CheckState.Warning), name);
    }

    [Test]
    public void MockPanelAndSimulatedVoice_AreFineInDevelopment()
    {
        var checks = SystemReadinessService.Evaluate(Healthy(development: true) with { MockPanel = true, Voice = VoiceMode.Mock });
        Assert.That(State(checks, "ai-panel"), Is.EqualTo(CheckState.Ok));
        Assert.That(State(checks, "voice"), Is.EqualTo(CheckState.Ok));
    }

    [Test]
    public void FailingProviders_AndAFailedSpeechCall_Warn()
    {
        var failed = new VoiceCallLine("tts.elevenlabs", DateTimeOffset.UtcNow, false, 900, "401");
        var checks = SystemReadinessService.Evaluate(Healthy() with { FailingProviders = 2, VoiceCalls = [failed] });
        Assert.That(State(checks, "ai-panel"), Is.EqualTo(CheckState.Warning));
        Assert.That(checks.Single(c => c.Name == "ai-panel").Summary, Does.Contain("all 2"));
        Assert.That(State(checks, "voice"), Is.EqualTo(CheckState.Warning));
        Assert.That(checks.Single(c => c.Name == "voice").Summary, Does.Contain("tts.elevenlabs").And.Not.Contain("401"), "summaries never carry error text");
    }

    [Test]
    public void AJobWaitingTooLong_ReadsAsAStalledWorker()
    {
        var checks = SystemReadinessService.Evaluate(Healthy() with { OldestQueuedWait = SystemReadinessService.StalledAfter + TimeSpan.FromMinutes(5) });
        Assert.That(State(checks, "jobs"), Is.EqualTo(CheckState.Warning));
        Assert.That(checks.Single(c => c.Name == "jobs").Summary, Does.Contain("stalled"));
    }

    [Test]
    public void RecentErrorLog_KeepsOnlyErrors_NewestFirst_UpToItsCapacity()
    {
        var log = new RecentErrorLog();
        var logger = log.CreateLogger("Test.Category");
        logger.LogWarning("not kept");
        for (var i = 0; i < RecentErrorLog.Capacity + 5; i++) logger.LogError("error {N}", i);
        logger.LogCritical(new InvalidOperationException("boom"), "last");

        var recent = log.Recent();
        Assert.That(recent, Has.Count.EqualTo(RecentErrorLog.Capacity));
        Assert.That(recent[0].Message, Is.EqualTo("last"));
        Assert.That(recent[0].Exception, Is.EqualTo("InvalidOperationException: boom"));
        Assert.That(recent[0].Category, Is.EqualTo("Test.Category"));
        Assert.That(recent.Any(e => e.Message == "not kept"), Is.False);
        Assert.That(log.CountSince(DateTimeOffset.UtcNow.AddMinutes(-1)), Is.EqualTo(RecentErrorLog.Capacity));
    }

    [Test]
    public void RecentErrorLog_CutsLongMessages()
    {
        var log = new RecentErrorLog();
        log.Add(LogLevel.Error, "c", new string('x', RecentErrorLog.MaxMessageLength + 100), null);
        Assert.That(log.Recent()[0].Message, Has.Length.EqualTo(RecentErrorLog.MaxMessageLength + 1));
    }
}
