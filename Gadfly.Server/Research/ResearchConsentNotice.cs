using System.Security.Cryptography;
using System.Text;
using Gadfly.Server.Data;

namespace Gadfly.Server.Research;

/// <summary>
/// The optional research question a student is asked (GAD-T452, ElenchusAtScale 7.5): separate from the exam notice, never a
/// condition of taking the exam. Like <see cref="Gadfly.Server.Student.ConsentNotice"/>, the exact text shown is hashed (SHA-256)
/// and stored with the decision, so the study can tell which wording each student answered.
/// </summary>
public sealed record ResearchConsentNotice(string Title, IReadOnlyList<string> Paragraphs, ResearchConsentDecision Kind)
{
    /// <summary>Bumped whenever the wording changes; part of the hashed text.</summary>
    public const string Version = "2026-11-22.1";

    /// <summary>The invitation: what joining means, shown on the get-ready page and on the student's own research page.</summary>
    public static ResearchConsentNotice Invitation(string organizationName, string studyTitle, string? contact, string? ethicsReference, int retentionDays)
    {
        var title = string.IsNullOrWhiteSpace(studyTitle) ? "a study of oral exams" : studyTitle.Trim();
        var paragraphs = new List<string>
        {
            $"{organizationName} is running research ({title}) on how well this kind of spoken exam works: whether the AI examiner agrees with human graders, whether it is fair to different speakers, and whether feedback helps on a later attempt. You can choose whether your exam records are included. This is separate from the exam notice and entirely optional.",
            "Saying no, or not answering, changes nothing about your exam: you take the same exam, it is graded the same way, and no one grading you is told what you chose.",
            "What the study would get: a de-identified copy of your exam records: the questions' types and timing, how the AI and the human graders scored each topic, how disagreements between them were settled, counts and types of integrity notes, and any accommodation you had (held back where so few students share it that it could point to you). Your name, email, the words you said or typed, audio and video are never included. You appear under a study code instead of your name.",
            "Voice measurements are included only where your organization measures voices at all, and never for students in the European Union or EEA.",
            $"You can change your mind at any time on the \"My research choice\" page (from My exams). Withdrawing stops your records going into any later copy for the study; a copy already given to the researchers cannot be called back, but it holds no name or contact details. Your exam records themselves are still kept for {retentionDays} days as the exam notice says.",
        };
        if (!string.IsNullOrWhiteSpace(contact))
            paragraphs.Add($"Questions about the study: {contact.Trim()}.");
        if (!string.IsNullOrWhiteSpace(ethicsReference))
            paragraphs.Add($"Ethics approval: {ethicsReference.Trim()}.");
        return new ResearchConsentNotice("Research (optional)", paragraphs, ResearchConsentDecision.Agreed);
    }

    /// <summary>The confirmation of a withdrawal, hashed like the invitation.</summary>
    public static ResearchConsentNotice Withdrawal(string organizationName) => new("Withdraw from the research",
    [
        $"You are withdrawing from {organizationName}'s research. From now on none of your exam records go into a copy for the study.",
        "A copy already given to the researchers cannot be called back; it holds no name or contact details. Your exams and grades are not affected.",
    ], ResearchConsentDecision.Withdrawn);

    /// <summary>The exact text shown, as hashed.</summary>
    public string Text => $"Gadfly research notice {Version}\n{Title}\n" + string.Join("\n", Paragraphs);

    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text))).ToLowerInvariant();
}
