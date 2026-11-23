using Gadfly.Server.Data;
using Gadfly.Server.Student;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Translation;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Students read the approved localized exam texts and never an unapproved machine draft (GAD-T630).</summary>
[TestFixture]
public class StudentExamTextsTests
{
    private static async Task<Seed.ExamWorld> WorldAsync(ExamLocalizationStatus? status)
    {
        var w = await Seed.ExamAsync();
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(w.Org));
        db.LearningObjectives.Add(new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "o1", Title = "Photosynthesis" });
        if (status is { } st)
            db.ExamLocalizations.Add(new ExamLocalization
            {
                OrganizationId = w.Org, ExamId = w.ExamId, Language = "ko", Title = "생물 시험", Instructions = "차근차근 답하세요.",
                ObjectiveTitlesJson = "{\"o1\":\"광합성\"}", Status = st, UpdatedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();
        return w;
    }

    private static Task<IReadOnlyList<StudentAssignmentView>> ListAsync(Seed.ExamWorld w, string language) =>
        StudentExams.ListAsync(ServerFixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), w.Org, w.StudentId, DateTimeOffset.UtcNow, language);

    [Test]
    public async Task Approved_text_is_shown_in_the_language()
    {
        var w = await WorldAsync(ExamLocalizationStatus.Approved);
        var row = (await ListAsync(w, "ko")).Single();
        Assert.That(row.ExamTitle, Is.EqualTo("생물 시험"));
        Assert.That(row.Instructions, Is.EqualTo("차근차근 답하세요."));
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(w.Org));
        Assert.That((await StudentExamTexts.ForAsync(db, w.Org, w.ExamId, "ko"))!.ObjectiveTitle("Photosynthesis"), Is.EqualTo("광합성"));
    }

    [Test]
    public async Task An_unapproved_machine_draft_is_never_shown_to_a_student()
    {
        var w = await WorldAsync(ExamLocalizationStatus.MachineDraft);
        var row = (await ListAsync(w, "ko")).Single();
        Assert.That(row.ExamTitle, Is.EqualTo("Biology check"));
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(w.Org));
        Assert.That((await StudentExamTexts.ForAsync(db, w.Org, w.ExamId, "ko"))!.ObjectiveTitle("Photosynthesis"), Is.EqualTo("Photosynthesis"));
        Assert.That((await LocalizedExamTexts.ForAsync(db, w.Org, w.ExamId, "ko"))!.Title, Is.EqualTo("생물 시험"), "staff still see the draft");
    }

    [Test]
    public async Task English_is_unchanged()
    {
        var w = await WorldAsync(ExamLocalizationStatus.Approved);
        Assert.That((await ListAsync(w, "en")).Single().ExamTitle, Is.EqualTo("Biology check"));
    }
}
