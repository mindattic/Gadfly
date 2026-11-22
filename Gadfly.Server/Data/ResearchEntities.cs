using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Data;

// Research use of exam records (GAD-T451, GAD-T452; ElenchusAtScale Chapter 7). A study is opt-in twice over: the organization
// switches research on, and each student chooses separately, never as a condition of taking an exam. Study arms are labels on
// an exam's assignments; an arm that would switch off an owner decision (for example the gaze fade) is not offered here and
// needs the owner's approval first.

/// <summary>An organization's research settings (GAD-T451/T452). One row per organization; no row means research is off.</summary>
public sealed class ResearchSettings : ITenantOwned
{
    public Guid OrganizationId { get; set; }
    /// <summary>Students are asked the optional research question before an exam. Off (the default): nobody is asked and nothing can be exported.</summary>
    public bool Enabled { get; set; }
    /// <summary>The study's name as students see it.</summary>
    public string StudyTitle { get; set; } = "";
    /// <summary>Who runs the study and how to reach them, shown to students.</summary>
    public string? StudyContact { get; set; }
    /// <summary>The ethics board's approval reference, shown to students when set.</summary>
    public string? EthicsReference { get; set; }
    /// <summary>
    /// The secret the export's pseudonyms are made with (base64, 32 random bytes). The same key gives the same pseudonyms in
    /// every export; replacing it makes new, unlinkable ones. Never exported or shown.
    /// </summary>
    public string PseudonymKey { get; set; } = "";
    /// <summary>Small-group suppression: an exam, or an accommodation within an exam, with fewer consenting students than this is held back.</summary>
    public int MinimumGroupSize { get; set; } = 5;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One study condition (arm) of an exam, for example "relay" or "feedback withheld" (GAD-T452, ElenchusAtScale 7.2).</summary>
public sealed class StudyArm : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ExamId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>What the arm's participants are told or do differently, for the study team's records.</summary>
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>No new students go into an archived arm; those already in it keep it.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>Which arm one assignment (one student on one exam) is in. Fixed once the student has started an attempt.</summary>
public sealed class StudyArmMember : ITenantOwned
{
    public Guid AssignmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ArmId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

public enum ResearchConsentDecision { Agreed, Declined, Withdrawn }

/// <summary>
/// One research-consent decision (GAD-T452), append-only: the latest row is the student's current choice. Recorded like the exam
/// consent, with the SHA-256 of the exact text the student was shown.
/// </summary>
public sealed class ResearchConsent : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid StudentUserId { get; set; }
    public ResearchConsentDecision Decision { get; set; }
    /// <summary>The research notice's version (<c>ResearchConsentNotice.Version</c>).</summary>
    public string NoticeVersion { get; set; } = "";
    /// <summary>SHA-256 of the exact text shown when the choice was made.</summary>
    public string ConsentTextSha256 { get; set; } = "";
    /// <summary>"start-exam" (before an attempt) or "my-research" (the student's own page).</summary>
    public string Source { get; set; } = "";
    /// <summary>The attempt the choice was made before, when it was made on the get-ready page.</summary>
    public Guid? SessionId { get; set; }
    public Guid? AssignmentId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>The research tables' model configuration, called from <see cref="GadflyDbContext"/>.OnModelCreating.</summary>
internal static class ResearchModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<ResearchSettings>(e =>
        {
            e.HasKey(x => x.OrganizationId);
            e.Property(x => x.StudyTitle).HasMaxLength(200);
            e.Property(x => x.StudyContact).HasMaxLength(400);
            e.Property(x => x.EthicsReference).HasMaxLength(200);
            e.Property(x => x.PseudonymKey).HasMaxLength(100);
            e.ToTable("ResearchSettings", "gadfly");
        });
        b.Entity<StudyArm>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => new { x.OrganizationId, x.ExamId });
            e.ToTable("StudyArms", "gadfly");
        });
        b.Entity<StudyArmMember>(e =>
        {
            e.HasKey(x => x.AssignmentId);
            e.HasIndex(x => x.ArmId);
            e.ToTable("StudyArmMembers", "gadfly");
        });
        b.Entity<ResearchConsent>(e =>
        {
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.NoticeVersion).HasMaxLength(40);
            e.Property(x => x.ConsentTextSha256).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasIndex(x => new { x.OrganizationId, x.StudentUserId, x.RecordedAt });
            e.ToTable("ResearchConsents", "gadfly");
        });
    }
}
