using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The exam room's answer cut-off marker on its way in (GAD-T462; under GAD-T399): a delay that isn't a finite number in the window,
/// or a negative turn, is refused before anything is read; voice measurements off for the session (an EU organization or student by
/// default, or switched off) drops it without touching the session; otherwise it goes through the session's own step. The rules
/// for marking an answer are in Engine/AnswerCutOffTests.
/// </summary>
[TestFixture]
public class AnswerCutOffRecorderTests : ServerTest
{
    private static IServiceScopeFactory Scopes => Services.GetRequiredService<IServiceScopeFactory>();
    private static SessionOrchestrator Orchestrator => Services.GetRequiredService<SessionOrchestrator>();

    /// <summary>A null orchestrator: any test that reaches the session step with it fails, proving the marker stopped earlier.</summary>
    private static readonly SessionOrchestrator Untouched = null!;

    [TestCase(double.NaN, 0)]
    [TestCase(double.PositiveInfinity, 0)]
    [TestCase(-100, 0)]
    [TestCase((AnswerCutOff.WindowSeconds + 1) * 1000, 0)]
    [TestCase(1500, -1)]
    public async Task A_marker_out_of_range_is_refused_before_anything_is_read(double delayMs, int turn)
    {
        Assert.That(await AnswerCutOffRecorder.RecordAsync(null!, Untouched, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), turn, delayMs, CancellationToken.None),
            Is.False);
    }

    [Test]
    public async Task An_unknown_session_is_refused()
    {
        var org = await Seed.OrganizationAsync();
        Assert.That(await AnswerCutOffRecorder.RecordAsync(Scopes, Untouched, org, Guid.NewGuid(), Guid.NewGuid(), 0, 1500, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task An_EU_organization_with_the_default_setting_drops_the_marker()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        await SettingsAsync(w.Org, s => s.InEuropeanUnion = true);
        Assert.That(await AnswerCutOffRecorder.RecordAsync(Scopes, Untouched, w.Org, w.StudentId, sessionId, 0, 1500, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task A_student_marked_as_living_in_the_EU_drops_the_marker()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        await using (var db = OrgDb(w.Org))
        {
            (await db.ExamAssignments.SingleAsync(a => a.Id == w.AssignmentId)).StudentInEuropeanUnion = true;
            await db.SaveChangesAsync();
        }
        Assert.That(await AnswerCutOffRecorder.RecordAsync(Scopes, Untouched, w.Org, w.StudentId, sessionId, 0, 1500, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task Voice_measurements_switched_off_drop_the_marker()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        await SettingsAsync(w.Org, s => s.VoiceMeasures = VoiceMeasuresSetting.Off);
        Assert.That(await AnswerCutOffRecorder.RecordAsync(Scopes, Untouched, w.Org, w.StudentId, sessionId, 0, 1500, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task With_voice_measurements_on_the_marker_goes_through_the_session_step()
    {
        var w = await Seed.ExamAsync();
        var sessionId = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "test-browser");
        await Orchestrator.RecordConsentAsync(w.Org, sessionId, w.StudentId, new string('c', 64));
        // Accepted for recording; the session step then marks it only on a watched spoken answer, which this session has none of.
        Assert.That(await AnswerCutOffRecorder.RecordAsync(Scopes, Orchestrator, w.Org, w.StudentId, sessionId, 0, 1500, CancellationToken.None), Is.True);
    }

    private static async Task SettingsAsync(Guid org, Action<OrganizationSettings> change)
    {
        await using var db = SystemDb();
        change(await db.OrganizationSettings.SingleAsync(s => s.OrganizationId == org));
        await db.SaveChangesAsync();
    }
}
