using Gadfly.Engine;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Data;

// Exams in more than one language (GAD-T526, schema GAD-T534). An exam offers languages (Exam.Languages); each attempt is
// conducted in one of them (ExamSession.Language) and keeps a copy of the student's primary language at the time
// (ExamSession.PrimaryLanguage). The source material, concept map and rubric are never translated: only the texts a student
// reads (title, instructions, objective titles) have per-language versions here. A student's language profile is per user,
// per organization, and editable by the student.

public enum ExamLocalizationStatus
{
    /// <summary>Machine-translated and not yet checked by a person: shown to students only with a notice, never silently.</summary>
    MachineDraft,
    /// <summary>Checked and approved by an instructor or admin.</summary>
    Approved,
}

/// <summary>One exam's student-facing texts in one language (GAD-T540). English is the Exam row itself; this holds the others.</summary>
public sealed class ExamLocalization : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ExamId { get; set; }
    /// <summary>BCP-47 primary language tag, lower case (<see cref="ExamLanguage"/>), never "en".</summary>
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    /// <summary>JSON object: learning objective key to its title in this language.</summary>
    public string ObjectiveTitlesJson { get; set; } = "{}";
    public ExamLocalizationStatus Status { get; set; } = ExamLocalizationStatus.MachineDraft;
    public Guid? UpdatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A student's languages (GAD-T552): the language they speak best and the others they speak. Per user and per organization,
/// editable by the student, asked once before their first exam. Used so the examiner and judges can treat a non-native speaker
/// fairly (GAD-T555) and for fairness views by language (GAD-T549); never used to judge the student.
/// </summary>
public sealed class StudentLanguageProfile : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>BCP-47 tag of the language the student speaks best ("en", "ko", or any other tag they choose).</summary>
    public string PrimaryLanguage { get; set; } = ExamLanguage.English;
    /// <summary>Comma-separated BCP-47 tags of every language the student speaks, primary included.</summary>
    public string LanguagesSpoken { get; set; } = ExamLanguage.English;
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class LanguageModel
{
    public const int MaxTagLength = 16;
    public const int MaxListLength = 200;
    public const int MaxReviewReasonLength = 500;

    public static void Configure(ModelBuilder b)
    {
        b.Entity<ExamLocalization>(e =>
        {
            e.Property(x => x.Language).HasMaxLength(MaxTagLength);
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId);
            e.HasIndex(x => new { x.ExamId, x.Language }).IsUnique();
        });
        b.Entity<StudentLanguageProfile>(e =>
        {
            e.Property(x => x.PrimaryLanguage).HasMaxLength(MaxTagLength);
            e.Property(x => x.LanguagesSpoken).HasMaxLength(MaxListLength);
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        });
        b.Entity<Exam>(e => e.Property(x => x.Languages).HasMaxLength(MaxListLength).HasDefaultValue(ExamLanguage.DefaultOffered));
        b.Entity<ExamSession>(e =>
        {
            e.Property(x => x.Language).HasMaxLength(MaxTagLength).HasDefaultValue(ExamLanguage.English);
            e.Property(x => x.PrimaryLanguage).HasMaxLength(MaxTagLength);
            e.Property(x => x.ReviewReason).HasMaxLength(MaxReviewReasonLength);
        });
    }
}
