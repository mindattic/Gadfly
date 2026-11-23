using Gadfly.Server.Data;
using Gadfly.Server.Student;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The exam room speaks the attempt's language (GAD-T606): every heading, button, status line, pause and notice comes from
/// examRoom.&lt;lang&gt;.json in <c>ExamSession.Language</c>, chosen before Begin and fixed for the attempt, whatever the site's language
/// switch says. Read before the first render, so the room never flips language in front of the student. The examiner's pronouns
/// follow the examiner on screen (<see cref="ExamRoomText"/>). An English attempt reads exactly as before.
/// </summary>
public partial class ExamRoom
{
    [Inject] private IWebHostEnvironment Env { get; set; } = default!;

    /// <summary>The attempt's language ("en" until read, and for every attempt before localization).</summary>
    private string language = Gadfly.Engine.ExamLanguage.English;
    private string? examAvatarId, organizationAvatarId;
    private bool maleExaminer;
    private ExamRoomText? roomText;

    /// <summary>The exam room's text in the attempt's language; cascaded to everything the room hosts.</summary>
    private ExamRoomText T => roomText ??= new ExamRoomText(Ui.For(language), maleExaminer);

    protected override async Task OnInitializedAsync()
    {
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        try
        {
            await using var scope = Scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(org));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var row = await (from s in db.ExamSessions.AsNoTracking()
                             join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                             where s.Id == SessionId && s.OrganizationId == org && s.StudentUserId == user && e.OrganizationId == org
                             select new { s.Language, e.AvatarId }).FirstOrDefaultAsync(stop.Token);
            if (row is null) return;   // not this student's: LoadAsync says so
            language = Gadfly.Server.Localization.UiCatalog.Normalize(row.Language);
            examAvatarId = row.AvatarId;
            organizationAvatarId = await db.OrganizationSettings.AsNoTracking().Where(o => o.OrganizationId == org)
                .Select(o => o.DefaultAvatarId).FirstOrDefaultAsync(stop.Token);
            roomText = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // English, as before localization: the exam itself is unaffected.
            Log.LogWarning(ex, "The attempt's language could not be read for session {SessionId}.", SessionId);
        }
    }

    /// <summary>Which examiner is on screen, for the pronouns (once interactive: the student's choice is kept in the browser).</summary>
    private async Task LoadExaminerAsync()
    {
        var male = await ExamRoomText.MaleExaminerAsync(JS, ExaminerCatalog.For(VoiceConfig, Env.WebRootPath), examAvatarId, organizationAvatarId, stop.Token);
        if (male == maleExaminer) return;
        maleExaminer = male;
        roomText = null;
    }
}
