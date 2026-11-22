using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Research;

/// <summary>A student's research status as their own page shows it.</summary>
public sealed record ResearchStatus(bool Enabled, ResearchConsentDecision? Current, DateTimeOffset? Since, ResearchConsentNotice? Invitation, string? StudyCode);

/// <summary>What the admin research page shows about consent: counts only, never who chose what.</summary>
public sealed record ResearchConsentSummary(int Agreed, int Declined, int Withdrawn);

public sealed record StudyArmView(Guid Id, string Name, string? Description, bool Archived, int Members);

public sealed record ArmAssignmentRow(Guid AssignmentId, string Student, Guid? ArmId, bool Locked);

/// <summary>The outcome of an action on the research page.</summary>
public sealed record ResearchResult(bool Ok, string Message)
{
    public static ResearchResult Done(string m) => new(true, m);
    public static ResearchResult Fail(string m) => new(false, m);
}

/// <summary>
/// Research settings, research consent and study arms (GAD-T451, GAD-T452). Every query names the organization; every admin
/// change is audited. A student's own research choice is recorded in <see cref="ResearchConsent"/> (append-only) and not in the
/// audit log, so the people who grade them are never shown what they chose.
/// </summary>
public sealed class ResearchService(GadflyDbContext db, TimeProvider clock)
{
    public const int MinGroupSizeFloor = 3, MinGroupSizeCeiling = 50;

    // ------------------------------------------------------------------ settings

    public Task<ResearchSettings?> SettingsAsync(Guid org, CancellationToken ct = default) =>
        db.ResearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == org, ct);

    public async Task<ResearchResult> SaveSettingsAsync(TenantSnapshot actor, bool enabled, string? studyTitle, string? contact, string? ethicsReference,
        int minimumGroupSize, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org) return ResearchResult.Fail("No organization is selected.");
        var title = Clean(studyTitle, 200);
        if (enabled && title is null) return ResearchResult.Fail("Give the study a name students will recognize before turning research on.");
        if (minimumGroupSize is < MinGroupSizeFloor or > MinGroupSizeCeiling)
            return ResearchResult.Fail($"The smallest group must be between {MinGroupSizeFloor} and {MinGroupSizeCeiling} students.");
        var row = await db.ResearchSettings.FirstOrDefaultAsync(s => s.OrganizationId == org, ct);
        var before = row is null ? null : Snapshot(row);
        if (row is null) db.ResearchSettings.Add(row = new ResearchSettings { OrganizationId = org, PseudonymKey = ResearchExport.NewKey() });
        if (string.IsNullOrEmpty(row.PseudonymKey)) row.PseudonymKey = ResearchExport.NewKey();
        row.Enabled = enabled;
        row.StudyTitle = title ?? "";
        row.StudyContact = Clean(contact, 400);
        row.EthicsReference = Clean(ethicsReference, 200);
        row.MinimumGroupSize = minimumGroupSize;
        row.UpdatedAt = clock.GetUtcNow();
        Audit(actor, "research.settings.changed", "ResearchSettings", org.ToString(), before, Snapshot(row),
            "GAD-T452: whether students are asked the optional research question, and how the research export is made.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done(enabled ? "Saved. Students are now asked the optional research question before an exam." : "Saved. Students are not asked about research.");
    }

    /// <summary>Replaces the pseudonym key: every later export uses new codes that cannot be linked to earlier ones.</summary>
    public async Task<ResearchResult> ReplaceKeyAsync(TenantSnapshot actor, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org) return ResearchResult.Fail("No organization is selected.");
        var row = await db.ResearchSettings.FirstOrDefaultAsync(s => s.OrganizationId == org, ct);
        if (row is null) return ResearchResult.Fail("Save the research settings first.");
        row.PseudonymKey = ResearchExport.NewKey();
        row.UpdatedAt = clock.GetUtcNow();
        Audit(actor, "research.key.replaced", "ResearchSettings", org.ToString(), null, null,
            "GAD-T451: later research exports use new study codes that cannot be linked to earlier exports.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done("New study codes from the next export on. Earlier exports can no longer be linked to later ones.");
    }

    private static object Snapshot(ResearchSettings s) => new { s.Enabled, s.StudyTitle, s.StudyContact, s.EthicsReference, s.MinimumGroupSize };

    // ------------------------------------------------------------------ consent

    /// <summary>Every research decision of the organization (for the export and the counts).</summary>
    public Task<List<ResearchConsent>> ConsentsAsync(Guid org, CancellationToken ct = default) =>
        db.ResearchConsents.AsNoTracking().Where(c => c.OrganizationId == org).ToListAsync(ct);

    public async Task<ResearchConsentSummary> SummaryAsync(Guid org, CancellationToken ct = default)
    {
        var current = ResearchConsents.Current(await ConsentsAsync(org, ct));
        return new ResearchConsentSummary(
            current.Count(c => c.Value == ResearchConsentDecision.Agreed),
            current.Count(c => c.Value == ResearchConsentDecision.Declined),
            current.Count(c => c.Value == ResearchConsentDecision.Withdrawn));
    }

    /// <summary>The student's research status and the invitation text they would be shown.</summary>
    public async Task<ResearchStatus> StatusAsync(Guid org, Guid student, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(org, ct);
        var latest = await db.ResearchConsents.AsNoTracking().Where(c => c.OrganizationId == org && c.StudentUserId == student)
            .OrderByDescending(c => c.RecordedAt).ToListAsync(ct);
        var current = latest.Count == 0 ? (ResearchConsentDecision?)null : ResearchConsents.Current(latest)[student];
        var since = latest.FirstOrDefault()?.RecordedAt;
        if (settings is null) return new ResearchStatus(false, current, since, null, null);
        var invitation = await InvitationAsync(org, settings, ct);
        var code = current == ResearchConsentDecision.Agreed && settings.PseudonymKey.Length > 0
            ? ResearchExport.Pseudonym(settings.PseudonymKey, "S", student) : null;
        return new ResearchStatus(settings.Enabled, current, since, invitation, code);
    }

    private async Task<ResearchConsentNotice> InvitationAsync(Guid org, ResearchSettings settings, CancellationToken ct)
    {
        var name = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Your organization";
        var days = await db.OrganizationSettings.AsNoTracking().Where(s => s.OrganizationId == org).Select(s => (int?)s.RetentionDays).FirstOrDefaultAsync(ct) ?? 90;
        return ResearchConsentNotice.Invitation(name, settings.StudyTitle, settings.StudyContact, settings.EthicsReference, days);
    }

    /// <summary>
    /// Records a student's own choice. <paramref name="shownSha256"/> is the hash of the text they were shown; it must match the
    /// current wording, so a choice is never recorded against text the student did not see. Agreeing or declining needs research
    /// to be on; withdrawing is always possible once agreed.
    /// </summary>
    public async Task<ResearchResult> RecordAsync(Guid org, Guid student, ResearchConsentDecision decision, string shownSha256, string source,
        Guid? sessionId = null, Guid? assignmentId = null, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(org, ct);
        var status = await StatusAsync(org, student, ct);
        ResearchConsentNotice expected;
        if (decision == ResearchConsentDecision.Withdrawn)
        {
            if (status.Current != ResearchConsentDecision.Agreed) return ResearchResult.Fail("You are not taking part in the research, so there is nothing to withdraw.");
            var name = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Your organization";
            expected = ResearchConsentNotice.Withdrawal(name);
        }
        else
        {
            if (settings is not { Enabled: true } || status.Invitation is null) return ResearchResult.Fail("Your organization is not running research at the moment.");
            expected = status.Invitation;
        }
        if (!string.Equals(expected.Sha256, shownSha256, StringComparison.OrdinalIgnoreCase))
            return ResearchResult.Fail("The research notice has changed since this page was opened. Reload the page and read it again.");
        if (sessionId is { } sid && !await db.ExamSessions.AsNoTracking().AnyAsync(s => s.Id == sid && s.OrganizationId == org && s.StudentUserId == student, ct))
            return ResearchResult.Fail("That exam attempt is not yours.");
        db.ResearchConsents.Add(new ResearchConsent
        {
            OrganizationId = org, StudentUserId = student, Decision = decision, NoticeVersion = ResearchConsentNotice.Version,
            ConsentTextSha256 = expected.Sha256, Source = source, SessionId = sessionId, AssignmentId = assignmentId, RecordedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done(decision switch
        {
            ResearchConsentDecision.Agreed => "Thank you. Your de-identified exam records will be included in the research.",
            ResearchConsentDecision.Declined => "Saved. Your exam records will not be used for research.",
            _ => "You have withdrawn. None of your exam records will go into any later copy for the research.",
        });
    }

    // ------------------------------------------------------------------ study arms

    public async Task<List<StudyArmView>> ArmsAsync(Guid org, Guid examId, CancellationToken ct = default)
    {
        var arms = await db.StudyArms.AsNoTracking().Where(a => a.OrganizationId == org && a.ExamId == examId).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var ids = arms.Select(a => a.Id).ToList();
        var counts = await db.StudyArmMembers.AsNoTracking().Where(m => m.OrganizationId == org && ids.Contains(m.ArmId))
            .GroupBy(m => m.ArmId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return arms.Select(a => new StudyArmView(a.Id, a.Name, a.Description, a.ArchivedAt is not null, counts.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<ResearchResult> AddArmAsync(TenantSnapshot actor, Guid examId, string? name, string? description, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org || actor.UserId is not { } me) return ResearchResult.Fail("No organization is selected.");
        var clean = Clean(name, 80);
        if (clean is null) return ResearchResult.Fail("Give the arm a name, for example \"control\" or \"relay\".");
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return ResearchResult.Fail("That exam was not found.");
        if (await db.StudyArms.AnyAsync(a => a.OrganizationId == org && a.ExamId == examId && a.Name == clean && a.ArchivedAt == null, ct))
            return ResearchResult.Fail("This exam already has an arm with that name.");
        var arm = new StudyArm { OrganizationId = org, ExamId = examId, Name = clean, Description = Clean(description, 1000), CreatedByUserId = me, CreatedAt = clock.GetUtcNow() };
        db.StudyArms.Add(arm);
        Audit(actor, "research.arm.created", "StudyArm", arm.Id.ToString(), null, new { arm.ExamId, arm.Name, arm.Description }, "GAD-T452: a study condition for this exam.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done($"Added the arm \"{clean}\".");
    }

    public async Task<ResearchResult> ArchiveArmAsync(TenantSnapshot actor, Guid armId, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org) return ResearchResult.Fail("No organization is selected.");
        var arm = await db.StudyArms.FirstOrDefaultAsync(a => a.Id == armId && a.OrganizationId == org, ct);
        if (arm is null) return ResearchResult.Fail("That arm was not found.");
        if (arm.ArchivedAt is not null) return ResearchResult.Done("That arm is already closed.");
        arm.ArchivedAt = clock.GetUtcNow();
        Audit(actor, "research.arm.archived", "StudyArm", arm.Id.ToString(), new { arm.Name }, new { arm.ArchivedAt }, "GAD-T452: no new students go into this arm.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done($"Closed \"{arm.Name}\": no new students go into it; those already in it keep it.");
    }

    /// <summary>The exam's assignments with their arm; an assignment whose student has started an attempt is locked.</summary>
    public async Task<List<ArmAssignmentRow>> AssignmentsAsync(Guid org, Guid examId, CancellationToken ct = default)
    {
        var rows = await (from a in db.ExamAssignments.AsNoTracking()
                          join u in db.AuthUsers.AsNoTracking() on a.StudentUserId equals u.Id
                          where a.OrganizationId == org && a.ExamId == examId
                          select new
                          {
                              a.Id, Name = u.DisplayName ?? u.UserName,
                              Arm = db.StudyArmMembers.Where(m => m.AssignmentId == a.Id && m.OrganizationId == org).Select(m => (Guid?)m.ArmId).FirstOrDefault(),
                              Started = db.ExamSessions.Any(s => s.AssignmentId == a.Id && s.OrganizationId == org && s.StartedAt != null),
                          }).ToListAsync(ct);
        return rows.OrderBy(r => r.Name).Select(r => new ArmAssignmentRow(r.Id, r.Name, r.Arm, r.Started)).ToList();
    }

    /// <summary>Puts one assignment in an arm (or none). Refused once the student has started an attempt: the condition is fixed for the attempt.</summary>
    public async Task<ResearchResult> SetArmAsync(TenantSnapshot actor, Guid assignmentId, Guid? armId, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org || actor.UserId is not { } me) return ResearchResult.Fail("No organization is selected.");
        var assignment = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId && a.OrganizationId == org, ct);
        if (assignment is null) return ResearchResult.Fail("That assignment was not found.");
        if (await db.ExamSessions.AnyAsync(s => s.AssignmentId == assignmentId && s.OrganizationId == org && s.StartedAt != null, ct))
            return ResearchResult.Fail("This student has already started an attempt, so their arm can no longer change.");
        if (armId is { } aid && !await db.StudyArms.AnyAsync(a => a.Id == aid && a.OrganizationId == org && a.ExamId == assignment.ExamId && a.ArchivedAt == null, ct))
            return ResearchResult.Fail("That arm is closed or belongs to another exam.");
        var row = await db.StudyArmMembers.FirstOrDefaultAsync(m => m.AssignmentId == assignmentId && m.OrganizationId == org, ct);
        var before = row?.ArmId;
        if (before == armId) return ResearchResult.Done("No change.");
        if (row is not null) db.StudyArmMembers.Remove(row);
        if (armId is { } newArm)
            db.StudyArmMembers.Add(new StudyArmMember { AssignmentId = assignmentId, OrganizationId = org, ArmId = newArm, AssignedByUserId = me, AssignedAt = clock.GetUtcNow() });
        Audit(actor, "research.arm.member-set", "ExamAssignment", assignmentId.ToString(), new { arm = before }, new { arm = armId }, "GAD-T452: the study condition of one student on this exam.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done("Saved.");
    }

    /// <summary>Puts every assignment of the exam that has no arm and no started attempt into an open arm at random, keeping the arms' sizes even.</summary>
    public async Task<ResearchResult> RandomizeAsync(TenantSnapshot actor, Guid examId, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org || actor.UserId is not { } me) return ResearchResult.Fail("No organization is selected.");
        var arms = await db.StudyArms.AsNoTracking().Where(a => a.OrganizationId == org && a.ExamId == examId && a.ArchivedAt == null).Select(a => a.Id).ToListAsync(ct);
        if (arms.Count < 2) return ResearchResult.Fail("Add at least two open arms first.");
        var rows = await AssignmentsAsync(org, examId, ct);
        var open = rows.Where(r => r.ArmId is null && !r.Locked).Select(r => r.AssignmentId).ToList();
        if (open.Count == 0) return ResearchResult.Done("Every student who hasn't started already has an arm.");
        var sizes = arms.ToDictionary(a => a, a => rows.Count(r => r.ArmId == a));
        var now = clock.GetUtcNow();
        foreach (var id in open.OrderBy(_ => Random.Shared.Next()))
        {
            var smallest = sizes.Min(kv => kv.Value);
            var candidates = sizes.Where(kv => kv.Value == smallest).Select(kv => kv.Key).ToList();
            var pick = candidates[Random.Shared.Next(candidates.Count)];
            sizes[pick]++;
            db.StudyArmMembers.Add(new StudyArmMember { AssignmentId = id, OrganizationId = org, ArmId = pick, AssignedByUserId = me, AssignedAt = now });
        }
        Audit(actor, "research.arm.randomized", "Exam", examId.ToString(), null, new { assignments = open.Count, arms = arms.Count },
            "GAD-T452: students without an arm were put into the open arms at random, keeping the arms even.");
        await db.SaveChangesAsync(ct);
        return ResearchResult.Done($"Put {open.Count} student(s) into an arm at random.");
    }

    // ------------------------------------------------------------------ export

    /// <summary>
    /// Builds the de-identified research export for the organization (optionally one exam) and audits it. Returns null when
    /// research has never been set up.
    /// </summary>
    public async Task<(byte[] Zip, ResearchDataset Data)?> ExportAsync(TenantSnapshot actor, Guid? examId, CancellationToken ct = default)
    {
        if (actor.OrganizationId is not { } org) return null;
        var settings = await SettingsAsync(org, ct);
        if (settings is null) return null;
        if (string.IsNullOrEmpty(settings.PseudonymKey))
        {
            var row = await db.ResearchSettings.FirstAsync(s => s.OrganizationId == org, ct);
            row.PseudonymKey = ResearchExport.NewKey();
            await db.SaveChangesAsync(ct);
            settings = row;
        }
        var now = clock.GetUtcNow();
        var input = await LoadAsync(org, settings, examId, now, ct);
        var data = ResearchExport.Build(input);
        var zip = ResearchExport.ToZip(data, now, settings.MinimumGroupSize);
        Audit(actor, "research.exported", "ResearchExport", examId?.ToString() ?? org.ToString(), null,
            new { exam = examId, data.Students, data.Sessions, data.ExamsIncluded, examsHeldBack = data.ExamsSuppressed, studentsLeftOut = data.StudentsNotConsenting, settings.MinimumGroupSize },
            "GAD-T451: de-identified research export of consenting students only.");
        await db.SaveChangesAsync(ct);
        return (zip, data);
    }

    /// <summary>Reads the rows the export needs. Only students who currently agree are read beyond their id; no name, email or answer text is read.</summary>
    public async Task<ResearchExportInput> LoadAsync(Guid org, ResearchSettings settings, Guid? examId, DateTimeOffset now, CancellationToken ct = default)
    {
        var consents = await ConsentsAsync(org, ct);
        var agreed = ResearchConsents.CurrentlyAgreed(consents).ToList();
        var orgSettings = await db.OrganizationSettings.AsNoTracking().Where(s => s.OrganizationId == org)
            .Select(s => new { s.InEuropeanUnion, s.VoiceMeasures }).FirstOrDefaultAsync(ct);

        // Every student with an ended attempt is counted (to report how many were left out); only consenting ones are read further.
        var allSessions = db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && (examId == null || s.ExamId == examId)
                                                                && (s.State == SessionState.Ended || s.State == SessionState.Failed) && s.StartedAt != null);
        var nonConsentingStubs = await allSessions.Where(s => !agreed.Contains(s.StudentUserId))
            .Select(s => new { s.ExamId, s.StudentUserId }).Distinct().ToListAsync(ct);

        var raw = await (from s in allSessions.Where(s => agreed.Contains(s.StudentUserId))
                         join a in db.ExamAssignments.AsNoTracking() on s.AssignmentId equals a.Id
                         where a.OrganizationId == org
                         select new
                         {
                             s.Id, s.AssignmentId, s.ExamId, s.StudentUserId, s.Attempt, s.State, s.EndReason, s.CreatedAt, s.StartedAt, s.EndedAt,
                             Redacted = s.RedactedAt != null, a.TimeMultiplier, a.GazeAccommodation, a.TypedAnswerAccommodation, a.StudentInEuropeanUnion,
                             Arm = db.StudyArmMembers.Where(m => m.AssignmentId == a.Id && m.OrganizationId == org).Select(m => (Guid?)m.ArmId).FirstOrDefault(),
                             Published = db.ApprovalRecords.Where(r => r.SessionId == s.Id && r.OrganizationId == org && r.Action == ApprovalAction.Publish)
                                 .Select(r => (DateTimeOffset?)r.At).Min(),
                             Final = db.FinalGrades.Where(f => f.SessionId == s.Id && f.OrganizationId == org).Select(f => new { f.Status, f.FinalPercent }).FirstOrDefault(),
                             s.TranscriptJson,
                         }).ToListAsync(ct);
        var sessions = raw.Select(s => new ExportSession(s.Id, s.AssignmentId, s.ExamId, s.StudentUserId, s.Attempt, s.State.ToString(), s.EndReason,
            s.CreatedAt, s.StartedAt, s.EndedAt, s.Redacted, s.TimeMultiplier, s.GazeAccommodation, s.TypedAnswerAccommodation, s.StudentInEuropeanUnion,
            s.Arm, s.Published, s.Final?.Status.ToString(), s.Final?.FinalPercent, s.TranscriptJson)).ToList();
        // The left-out students appear only as stubs, so the builder can count them; they carry no data.
        sessions.AddRange(nonConsentingStubs.Select(n => new ExportSession(Guid.Empty, Guid.Empty, n.ExamId, n.StudentUserId, 0, "", null,
            now, null, null, false, 1m, false, false, false, null, null, null, null, null)));

        var ids = raw.Select(s => s.Id).ToList();
        var exams = raw.Select(s => s.ExamId).Distinct().ToList();

        var turns = await (from t in db.SessionTurns.AsNoTracking()
                           where t.OrganizationId == org && ids.Contains(t.SessionId)
                           select new
                           {
                               t.SessionId, t.Id, t.Index, t.ConceptKey, t.Move, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt, t.AnswerMode, t.SpeechMetricsJson,
                               t.MasteryAfter,
                               J = db.Judgments.Where(j => j.TurnId == t.Id && j.OrganizationId == org)
                                   .Select(j => new { j.OverallScore, j.Agreement, j.NeedsHumanReview, Votes = j.Votes.Count, j.TextStyleScore }).FirstOrDefault(),
                           }).ToListAsync(ct);
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.OrganizationId == org && exams.Contains(o.ExamId))
            .Select(o => new ExportObjective(o.Id, o.ExamId, o.Key, o.Ordinal, o.Weight, o.MaxScore)).ToListAsync(ct);
        var panel = await db.AiObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId) && !g.IsMock)
            .Select(g => new { g.SessionId, g.ObjectiveId, g.Level, g.Score, g.MaxScore, g.Confidence }).ToListAsync(ct);
        var human = await db.HumanObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId))
            .Select(g => new { g.Id, g.SessionId, g.ObjectiveId, g.ReviewerUserId, g.ReviewerRole, g.Level, g.Score, g.MaxScore, g.WasBlind, g.SupersedesId, g.SubmittedAt })
            .ToListAsync(ct);
        var superseded = human.Where(h => h.SupersedesId is not null).Select(h => h.SupersedesId!.Value).ToHashSet();
        var disagreements = await (from d in db.GradeDisagreements.AsNoTracking()
                                   where d.OrganizationId == org && ids.Contains(d.SessionId)
                                   select new
                                   {
                                       d.SessionId, d.ObjectiveId, d.AiScore, d.HumanScore, d.DifferencePoints, d.Band, d.DetectedAt, d.ResolvedAt,
                                       R = db.GradeReconciliations.Where(r => r.DisagreementId == d.Id && r.OrganizationId == org).OrderByDescending(r => r.At)
                                           .Select(r => new { r.Cause, Second = r.SecondReviewerUserId != null, r.ResolvedAiScore, r.ResolvedHumanScore }).FirstOrDefault(),
                                   }).ToListAsync(ct);
        var events = await db.IntegrityEvents.AsNoTracking().Where(e => e.OrganizationId == org && ids.Contains(e.SessionId))
            .Select(e => new ExportIntegrityEvent(e.SessionId, e.Source, e.Type, e.Severity.ToString(), e.Confidence, e.StartedAt, e.EndedAt, e.TurnId))
            .ToListAsync(ct);
        var arms = await db.StudyArms.AsNoTracking().Where(a => a.OrganizationId == org && exams.Contains(a.ExamId))
            .Select(a => new ExportArm(a.Id, a.ExamId, a.Name)).ToListAsync(ct);
        var wordErrors = await WordErrorsAsync(org, ids, ct);

        return new ResearchExportInput
        {
            PseudonymKey = settings.PseudonymKey,
            MinimumGroupSize = settings.MinimumGroupSize,
            OrganizationInEu = orgSettings?.InEuropeanUnion ?? false,
            VoiceMeasures = orgSettings?.VoiceMeasures ?? VoiceMeasuresSetting.RegionDefault,
            ExportedAt = now,
            Consents = consents,
            Sessions = sessions,
            Turns = turns.Select(t => new ExportTurn(t.SessionId, t.Id, t.Index, t.ConceptKey, t.Move, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt,
                t.AnswerMode, t.SpeechMetricsJson, t.MasteryAfter, t.J?.OverallScore, t.J?.Agreement, t.J?.NeedsHumanReview, t.J?.Votes ?? 0, t.J?.TextStyleScore,
                wordErrors.GetValueOrDefault(t.Id))).ToList(),
            Objectives = objectives,
            PanelGrades = panel.Select(g => new ExportPanelGrade(g.SessionId, g.ObjectiveId, g.Level.ToString(), g.Score, g.MaxScore, g.Confidence)).ToList(),
            HumanGrades = human.Select(g => new ExportHumanGrade(g.SessionId, g.ObjectiveId, g.ReviewerUserId, g.ReviewerRole, g.Level.ToString(), g.Score, g.MaxScore,
                g.WasBlind, superseded.Contains(g.Id), g.SubmittedAt)).ToList(),
            Disagreements = disagreements.Select(d => new ExportDisagreement(d.SessionId, d.ObjectiveId, d.AiScore, d.HumanScore, d.DifferencePoints, d.Band.ToString(),
                d.DetectedAt, d.ResolvedAt, d.R?.Cause, d.R?.Second ?? false, d.R?.ResolvedAiScore, d.R?.ResolvedHumanScore)).ToList(),
            IntegrityEvents = events,
            Arms = arms,
        };
    }

    /// <summary>
    /// Per-turn word errors of the consenting students' spoken original answers against the human reference transcripts
    /// (GAD-T426, audit events "response.reference-transcribed"), as counts only (GAD-T467). <paramref name="sessionIds"/> are the
    /// consenting students' sessions; the recognized text and the reference are read here and never leave this method.
    /// </summary>
    private async Task<Dictionary<Guid, Gadfly.Sources.Verification.WordErrorCount>> WordErrorsAsync(Guid org, List<Guid> sessionIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Gadfly.Sources.Verification.WordErrorCount>();
        foreach (var chunk in sessionIds.Chunk(500))
        {
            var spoken = await db.StudentResponses.AsNoTracking()
                .Where(r => r.OrganizationId == org && chunk.Contains(r.SessionId) && r.Modality == "voice" && r.Kind == ResponseKind.Original)
                .Select(r => new { r.Id, r.TurnId, Recognized = r.SpeechRecognitionText ?? r.OriginalText }).ToListAsync(ct);
            if (spoken.Count == 0) continue;
            var references = await Gadfly.Server.Analytics.ReferenceTranscripts.LoadAsync(db, org, spoken.Select(r => r.Id).ToList(), ct);
            foreach (var turn in spoken.Where(r => references.ContainsKey(r.Id)).GroupBy(r => r.TurnId))
                if (ResearchExport.WordErrorsOf(turn.Select(r => ((string?)r.Recognized, (string?)references[r.Id].Text))) is { } e)
                    result[turn.Key] = e;
        }
        return result;
    }

    // ------------------------------------------------------------------ helpers

    private void Audit(TenantSnapshot actor, string action, string entityType, string? entityId, object? before, object? after, string reason) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = actor.OrganizationId, ActorUserId = actor.UserId, ActorRole = actor.Role is { Length: > 40 } r ? r[..40] : actor.Role,
            Action = action, EntityType = entityType, EntityId = entityId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            Reason = reason, At = clock.GetUtcNow(),
        });

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
