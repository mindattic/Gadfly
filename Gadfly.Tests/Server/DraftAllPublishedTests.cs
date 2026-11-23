using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Translation;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Drafting every published exam in a language (GAD-T640): mock mode only, so no live model is called. The one failure test uses
/// a stub "live" panel with a model that always throws, never a real provider.
/// </summary>
[TestFixture]
public class DraftAllPublishedTests : ServerTest
{
    private sealed class StubPanel(bool mock) : IPanelInfo
    {
        public bool IsMock => mock;
        public string Mode => mock ? PanelModes.Mock : "live";
        public string Reason => "test";
    }

    private sealed class FailingModel : ILiveTurnModel
    {
        public string ProviderId => "claude";
        public string Model => "failing-" + Guid.NewGuid().ToString("N");

        public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("The test model is down.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private static ExamLanguageService Service(Guid org, string role, bool mock, out GadflyDbContext db)
    {
        var tenant = Member(org, Guid.NewGuid(), role);
        db = Db(tenant);
        return new ExamLanguageService(db, new FixedTenant(tenant), Clock, new StubPanel(mock), new FailingModel(),
            new UsageMeter(new ConfigurationBuilder().Build(), Clock));
    }

    private static async Task<Guid> AddExamAsync(Guid org, string title, ExamStatus status)
    {
        var w = await Seed.ExamAsync(org, status: status);
        await using var db = OrgDb(org);
        var exam = await db.Exams.SingleAsync(e => e.Id == w.ExamId);
        exam.Title = title;
        await db.SaveChangesAsync();
        return w.ExamId;
    }

    private static async Task<ExamLocalization?> RowAsync(Guid org, Guid exam, string lang)
    {
        await using var db = OrgDb(org);
        return await db.ExamLocalizations.AsNoTracking().FirstOrDefaultAsync(l => l.ExamId == exam && l.Language == lang);
    }

    [Test]
    public async Task Drafts_published_exams_skips_approved_and_ignores_drafts_and_other_organizations()
    {
        var org = await Seed.OrganizationAsync();
        var a = await AddExamAsync(org, "A exam", ExamStatus.Published);
        var b = await AddExamAsync(org, "B exam", ExamStatus.Published);
        var approved = await AddExamAsync(org, "C exam", ExamStatus.Published);
        var draft = await AddExamAsync(org, "D exam", ExamStatus.Draft);
        var other = (await Seed.ExamAsync()).ExamId;
        await using (var seed = OrgDb(org))
        {
            seed.ExamLocalizations.Add(new ExamLocalization
            {
                OrganizationId = org, ExamId = approved, Language = "es", Title = "Aprobado", Status = ExamLocalizationStatus.Approved,
                ApprovedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var service = Service(org, OrgRoles.Instructor, mock: true, out var db);
        DraftAllResult r;
        await using (db) r = await service.DraftAllPublishedAsync("es");

        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That((r.Drafted, r.Skipped, r.Failed, r.NotReached), Is.EqualTo((2, 1, 0, 0)));
        Assert.That(r.Exams.Select(e => e.Outcome), Is.EqualTo(new[] { ExamDraftOutcome.Ok, ExamDraftOutcome.Ok, ExamDraftOutcome.Skip }));
        Assert.That((await RowAsync(org, a, "es"))!.Status, Is.EqualTo(ExamLocalizationStatus.MachineDraft));
        Assert.That((await RowAsync(org, b, "es"))!.Title, Does.Contain("mock translation"));
        var kept = await RowAsync(org, approved, "es");
        Assert.That((kept!.Status, kept.Title), Is.EqualTo((ExamLocalizationStatus.Approved, "Aprobado")), "approved texts are never replaced");
        Assert.That(await RowAsync(org, draft, "es"), Is.Null, "a draft exam is not published");
        Assert.That(await RowAsync(org, other, "es"), Is.Null, "another organization's exam is untouched");
    }

    [Test]
    public async Task A_second_run_redrafts_machine_drafts_and_only_that_language_is_touched()
    {
        var org = await Seed.OrganizationAsync();
        var a = await AddExamAsync(org, "A exam", ExamStatus.Published);
        var service = Service(org, OrgRoles.Instructor, mock: true, out var db);
        await using (db) await service.DraftAllPublishedAsync("ko");
        var again = Service(org, OrgRoles.Instructor, mock: true, out var db2);
        DraftAllResult r;
        await using (db2) r = await again.DraftAllPublishedAsync("ko");

        Assert.That((r.Drafted, r.Skipped), Is.EqualTo((1, 0)));
        Assert.That(await RowAsync(org, a, "ko"), Is.Not.Null);
        Assert.That(await RowAsync(org, a, "es"), Is.Null);
    }

    [Test]
    public async Task Unsupported_english_and_unauthorised_requests_do_nothing()
    {
        var org = await Seed.OrganizationAsync();
        var a = await AddExamAsync(org, "A exam", ExamStatus.Published);

        var service = Service(org, OrgRoles.Instructor, mock: true, out var db);
        DraftAllResult en, xx;
        await using (db) { en = await service.DraftAllPublishedAsync("en"); xx = await service.DraftAllPublishedAsync("xx"); }
        var student = Service(org, OrgRoles.Student, mock: true, out var db2);
        DraftAllResult denied;
        await using (db2) denied = await student.DraftAllPublishedAsync("es");

        Assert.That(new[] { en.Ok, xx.Ok, denied.Ok }, Is.All.False);
        Assert.That(new[] { en, xx, denied }.Select(x => x.Exams.Count), Is.All.Zero);
        Assert.That(await RowAsync(org, a, "es"), Is.Null);
    }

    [Test]
    public async Task A_model_error_stops_the_run_at_the_first_failing_exam()
    {
        var org = await Seed.OrganizationAsync();
        var a = await AddExamAsync(org, "A exam", ExamStatus.Published);
        var b = await AddExamAsync(org, "B exam", ExamStatus.Published);
        var c = await AddExamAsync(org, "C exam", ExamStatus.Published);

        var service = Service(org, OrgRoles.Instructor, mock: false, out var db);   // "live" panel, but the model is a stub that throws
        DraftAllResult r;
        await using (db) r = await service.DraftAllPublishedAsync("es");

        Assert.That(r.Ok, Is.False);
        Assert.That((r.Drafted, r.Skipped, r.Failed, r.NotReached), Is.EqualTo((0, 0, 1, 2)));
        Assert.That(r.Exams, Has.Count.EqualTo(1));
        Assert.That(r.Exams[0].ExamId, Is.EqualTo(a));
        Assert.That(r.Exams[0].Outcome, Is.EqualTo(ExamDraftOutcome.Fail));
        Assert.That(r.Message, Does.Contain("stopped"));
        foreach (var id in new[] { a, b, c }) Assert.That(await RowAsync(org, id, "es"), Is.Null);
    }
}
