using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Assessments;

/// <summary>Where an exam stands on concept map and rubric approval (GAD-T437), for the review step.</summary>
public sealed record RubricApprovalStatus(Guid CollectionId, string CollectionTitle, int CurrentVersion, int Concepts, int RubricPoints,
    bool Required, RubricApproval? Approval, string? ApprovedByName, bool Published)
{
    /// <summary>The version in use has been approved.</summary>
    public bool Current => Approval is { } a && a.ConceptMapVersion == CurrentVersion;
    /// <summary>An approval exists, but the map or a rubric changed after it.</summary>
    public bool Stale => Approval is { } a && a.ConceptMapVersion != CurrentVersion;
}

/// <summary>
/// Concept map and rubric approval before summative use (GAD-T437; ElenchusAtScale 4.2, Appendix A correction 35, 6.8). The
/// panel builds each source collection's concept map and per-concept rubric, and the exam-time judge scores answers against
/// them, so without this they could be the standard of truth without any person reading them. An instructor now reads the
/// version in use (on the source collection's page) and approves it for the exam: a summative (Examination mode) assessment
/// cannot be published until the current version is approved, and once an approved map or rubric changes, no new attempt
/// starts until it is approved again (attempts in progress finish). The approved version, approver and time are recorded on
/// the exam (<see cref="SessionPolicy.RubricApproval"/>, in its policy JSON, so no schema change) and every approval is
/// audited. Learning Mode is formative: approval is offered but not required.
/// </summary>
public sealed class RubricApprovalService(GadflyDbContext db, ITenantContext tenant, IOrganizationDirectory directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly AssessmentGuard guard = new(db, tenant, clock);

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");

    /// <summary>True when an assessment in this mode needs an approved concept map and rubric (summative use).</summary>
    public static bool IsRequired(AssessmentMode mode) => mode == AssessmentMode.Examination;

    /// <summary>The publishing problem for this exam, or null when its concept map and rubric approval is in order.</summary>
    public static string? PublishProblem(AssessmentMode mode, SessionPolicy policy, int currentVersion) =>
        !IsRequired(mode) ? null
        : policy.RubricApproval is not { } a
            ? $"Concept map and rubric: read version {currentVersion} of the source material's concept map and rubrics and approve it for this examination (below). The judge scores answers against them."
        : a.ConceptMapVersion != currentVersion
            ? $"Concept map and rubric: you approved version {a.ConceptMapVersion}, but the source material is now at version {currentVersion}. Read the changes and approve again (below)."
        : null;

    /// <summary>
    /// Why a new attempt may not start, or null when it may: a summative exam whose approved concept map or rubric has changed
    /// since. An exam published before approval was required (no approval recorded) keeps running, flagged on its page.
    /// </summary>
    public static string? AttemptRefusal(AssessmentMode mode, SessionPolicy policy, int currentVersion) =>
        IsRequired(mode) && policy.RubricApproval is { } a && a.ConceptMapVersion != currentVersion
            ? "This exam's marking guide was changed and is waiting for your instructor to approve it again. Please try again later."
            : null;

    /// <summary>
    /// The published examinations on <paramref name="collectionId"/> with an approval (GAD-T437), split into those approved on
    /// <paramref name="version"/> (an edit will stop their new attempts until re-approved) and those waiting for re-approval.
    /// For the source collection's page, so whoever edits a concept or rubric knows what it holds up.
    /// </summary>
    public static async Task<(IReadOnlyList<string> ApprovedOnThis, IReadOnlyList<string> AwaitingReapproval)> ExamsOnCollectionAsync(
        GadflyDbContext db, Guid organizationId, Guid collectionId, int version, CancellationToken ct = default)
    {
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.CollectionId == collectionId && e.Status == ExamStatus.Published
                        && e.AssessmentMode == AssessmentMode.Examination)
            .Select(e => new { e.Title, e.PolicyJson }).ToListAsync(ct);
        var approvals = exams.Select(e => (e.Title, Approval: ExamService.ReadPolicy(e.PolicyJson).RubricApproval))
            .Where(x => x.Approval is not null).ToList();
        return (approvals.Where(x => x.Approval!.ConceptMapVersion == version).Select(x => x.Title).ToList(),
            approvals.Where(x => x.Approval!.ConceptMapVersion != version).Select(x => x.Title).ToList());
    }

    public async Task<RubricApprovalStatus?> StatusAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments)) return null;
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return null;
        var collection = await db.SourceCollections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == exam.CollectionId && c.OrganizationId == org, ct);
        if (collection is null) return null;
        var conceptIds = await db.Concepts.AsNoTracking().Where(c => c.CollectionId == collection.Id && c.OrganizationId == org && !c.IsArchived)
            .Select(c => c.Id).ToListAsync(ct);
        // Each concept's current rubric is its latest version's points.
        var points = (await db.RubricPoints.AsNoTracking().Where(p => p.OrganizationId == org && conceptIds.Contains(p.ConceptId))
                .Select(p => new { p.ConceptId, p.RubricVersion }).ToListAsync(ct))
            .GroupBy(p => p.ConceptId).Sum(g => { var latest = g.Max(x => x.RubricVersion); return g.Count(x => x.RubricVersion == latest); });
        var approval = ExamService.ReadPolicy(exam.PolicyJson).RubricApproval;
        string? by = null;
        if (approval is { } a)
            by = (await directory.MembersAsync(org, ct)).Where(m => m.Member.UserId == a.ApprovedByUserId).Select(m => m.UserName)
                .FirstOrDefault() ?? "(a former member)";
        return new RubricApprovalStatus(collection.Id, collection.Title, collection.ConceptMapVersion, conceptIds.Count, points,
            IsRequired(exam.AssessmentMode), approval, by, exam.Status != ExamStatus.Draft);
    }

    /// <summary>
    /// Records that the caller has read and approves concept map and rubric version <paramref name="readVersion"/> for this exam.
    /// Refused when the source material changed after the page was opened (the version read is no longer the one in use), so
    /// an approval always names exactly what was read. Allowed on a published exam too, to re-approve after a change. Audited.
    /// </summary>
    public async Task<AssessmentResult> ApproveAsync(Guid examId, int readVersion, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Approving the concept map and rubric", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        if (exam.Status == ExamStatus.Archived) return new(false, "This assessment is archived.");
        var collection = await db.SourceCollections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == exam.CollectionId && c.OrganizationId == org, ct);
        if (collection is null || collection.Status != ProcessingStatus.Ready || collection.ConceptMapVersion <= 0)
            return new(false, "The source material's concept map is not ready yet, so there is nothing to approve.");
        if (collection.ConceptMapVersion != readVersion)
            return new(false, $"The concept map or a rubric changed while you were reading (it is now version {collection.ConceptMapVersion}). Read it again, then approve.");
        var policy = ExamService.ReadPolicy(exam.PolicyJson);
        if (policy.RubricApproval?.ConceptMapVersion == readVersion)
            return new(true, $"Version {readVersion} is already approved for this exam.", exam.Id);
        var user = tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var approval = new RubricApproval(readVersion, user, clock.GetUtcNow());
        exam.PolicyJson = JsonSerializer.Serialize(policy with { RubricApproval = approval }, Json);
        guard.Audit("assessment.rubric.approved", "Exam", exam.Id.ToString(),
            policy.RubricApproval is { } old ? new { ApprovedConceptMapVersion = old.ConceptMapVersion, old.ApprovedAt } : null,
            new { CollectionId = collection.Id, ApprovedConceptMapVersion = readVersion, Mode = exam.AssessmentMode.ToString(), exam.Status },
            "The instructor read and approved the concept map and rubric the judge scores against (GAD-T437).");
        await db.SaveChangesAsync(ct);
        return new(true, exam.Status == ExamStatus.Draft
            ? $"Concept map and rubric version {readVersion} approved for this exam."
            : $"Version {readVersion} approved. New attempts can start again.", exam.Id);
    }
}
