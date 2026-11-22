using System.IO.Compression;
using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Research;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The pilot research export (GAD-T451) and research consent (GAD-T452): only students who currently agree are exported, every
/// id is a keyed pseudonym, voice measurements stay out where they are off or the student is in the EU/EEA, and small groups are
/// held back. The pure builder needs no database.
/// </summary>
[TestFixture]
public sealed class ResearchExportTests
{
    private const string Key = "dGVzdC1rZXktZm9yLXJlc2VhcmNoLWV4cG9ydC0wMDAwMA==";
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Exam = Guid.NewGuid(), Objective = Guid.NewGuid(), Rater = Guid.NewGuid();

    private static ResearchConsent Consent(Guid student, ResearchConsentDecision d, int minutes) => new()
    {
        StudentUserId = student, Decision = d, RecordedAt = T0.AddMinutes(minutes), NoticeVersion = ResearchConsentNotice.Version, ConsentTextSha256 = "x", Source = "test",
    };

    private static ExportSession Session(Guid student, Guid? exam = null, bool eu = false, bool gaze = false, decimal time = 1m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), exam ?? Exam, student, 1, "Ended", "Mastery", T0, T0, T0.AddMinutes(20), false,
            time, gaze, false, eu, null, null, "Published", 72m, null);

    private static (ResearchExportInput Input, List<Guid> Students) Cohort(int n, Func<int, ResearchConsentDecision?> decision, Func<int, ExportSession>? make = null)
    {
        var students = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();
        var sessions = students.Select((s, i) => make?.Invoke(i) is { } custom ? custom with { StudentUserId = s } : Session(s)).ToList();
        var consents = students.Select((s, i) => decision(i) is { } d ? Consent(s, d, i) : null).OfType<ResearchConsent>().ToList();
        var turns = sessions.Select(s => new ExportTurn(s.SessionId, Guid.NewGuid(), 0, "photosynthesis", "Elicit", T0.AddSeconds(5), T0.AddSeconds(9), T0.AddSeconds(40),
            "voice", """{"wordCount":42,"verification.confidence":0.91,"speechRateWpm":130,"voice.pitchMedianHz":180,"strain.vsBaseline":0.4,"pauses.ratio":0.2}""",
            0.6, 0.7, 0.9, false, 3, 0.1)).ToList();
        var human = sessions.Select(s => new ExportHumanGrade(s.SessionId, Objective, Rater, "Instructor", "Proficient", 70, 100, true, false, T0.AddHours(30))).ToList();
        return (new ResearchExportInput
        {
            PseudonymKey = Key, MinimumGroupSize = 5, ExportedAt = T0.AddDays(10), Consents = consents, Sessions = sessions, Turns = turns, HumanGrades = human,
            Objectives = [new ExportObjective(Objective, Exam, "photosynthesis", 0, 1, 100)],
        }, students);
    }

    private static string AllText(ResearchDataset d) => string.Join("\n", d.Tables.Select(ResearchExport.ToCsv));

    [Test]
    public void Only_students_who_currently_agree_are_exported()
    {
        // 0-5 agree, 6 declined, 7 agreed then withdrew, 8 never answered.
        var (input, students) = Cohort(9, i => i switch { < 6 => ResearchConsentDecision.Agreed, 6 => ResearchConsentDecision.Declined, 7 => ResearchConsentDecision.Agreed, _ => null });
        input = input with { Consents = [.. input.Consents, Consent(students[7], ResearchConsentDecision.Withdrawn, 100)] };

        var data = ResearchExport.Build(input);

        Assert.That(data.Students, Is.EqualTo(6));
        Assert.That(data.StudentsNotConsenting, Is.EqualTo(3));
        var exported = data.Table(ResearchExport.SessionsFile).Rows.Select(r => r[1]).ToHashSet();
        Assert.That(exported, Is.EquivalentTo(students.Take(6).Select(s => ResearchExport.Pseudonym(Key, "S", s))));
        foreach (var left in students.Skip(6))
            Assert.That(exported, Does.Not.Contain(ResearchExport.Pseudonym(Key, "S", left)));
    }

    [Test]
    public void Latest_decision_wins_and_a_tie_is_read_as_the_more_restrictive()
    {
        var s = Guid.NewGuid();
        Assert.That(ResearchConsents.CurrentlyAgreed([Consent(s, ResearchConsentDecision.Declined, 0), Consent(s, ResearchConsentDecision.Agreed, 5)]), Does.Contain(s));
        Assert.That(ResearchConsents.CurrentlyAgreed([Consent(s, ResearchConsentDecision.Agreed, 0), Consent(s, ResearchConsentDecision.Withdrawn, 5)]), Is.Empty);
        Assert.That(ResearchConsents.CurrentlyAgreed([Consent(s, ResearchConsentDecision.Agreed, 5), Consent(s, ResearchConsentDecision.Withdrawn, 5)]), Is.Empty);
        Assert.That(ResearchConsents.CurrentlyAgreed([]), Is.Empty);
    }

    [Test]
    public void No_raw_id_appears_and_pseudonyms_are_stable_and_keyed()
    {
        var (input, students) = Cohort(6, _ => ResearchConsentDecision.Agreed);
        var data = ResearchExport.Build(input);
        var text = AllText(data);

        foreach (var id in students.Concat(input.Sessions.Select(s => s.SessionId)).Concat([Exam, Objective, Rater]))
        {
            Assert.That(text, Does.Not.Contain(id.ToString()), "a raw id leaked");
            Assert.That(text, Does.Not.Contain(id.ToString("N")), "a raw id leaked");
        }
        Assert.That(ResearchExport.Pseudonym(Key, "S", students[0]), Is.EqualTo(ResearchExport.Pseudonym(Key, "S", students[0])));
        Assert.That(ResearchExport.Pseudonym(ResearchExport.NewKey(), "S", students[0]), Is.Not.EqualTo(ResearchExport.Pseudonym(Key, "S", students[0])));
        Assert.That(ResearchExport.Pseudonym(Key, "S", students[0]), Is.Not.EqualTo(ResearchExport.Pseudonym(Key, "R", students[0])));
        Assert.That(text, Does.Contain(ResearchExport.Pseudonym(Key, "R", Rater)));
    }

    [Test]
    public void Raw_voice_features_never_leave_and_voice_measures_are_dropped_for_EU_students()
    {
        var (input, _) = Cohort(6, _ => ResearchConsentDecision.Agreed, i => Session(Guid.Empty, eu: i == 0));
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);
        var rate = turns.Columns.ToList().IndexOf("speech_rate_wpm");
        var strain = turns.Columns.ToList().IndexOf("voice_vs_baseline");
        var words = turns.Columns.ToList().IndexOf("word_count");

        Assert.That(data.Tables.SelectMany(t => t.Rows).SelectMany(r => r), Has.None.EqualTo("180"), "raw pitch is never exported");
        Assert.That(data.Tables.SelectMany(t => t.Columns), Has.None.Contains("pitch"));
        var euSession = ResearchExport.Pseudonym(Key, "X", input.Sessions[0].SessionId);
        foreach (var row in turns.Rows)
        {
            Assert.That(row[words], Is.EqualTo("42"));
            var eu = row[0] == euSession;
            Assert.That(row[rate], eu ? Is.Empty : Is.EqualTo("130"));
            Assert.That(row[strain], eu ? Is.Empty : Is.EqualTo("0.4"));
        }
    }

    [Test]
    public void Voice_measures_follow_the_organization_switch_and_the_EU_marker()
    {
        Assert.That(ResearchExport.VoiceExportable(VoiceMeasuresSetting.RegionDefault, false, false), Is.True);
        Assert.That(ResearchExport.VoiceExportable(VoiceMeasuresSetting.Off, false, false), Is.False);
        Assert.That(ResearchExport.VoiceExportable(VoiceMeasuresSetting.On, true, false), Is.False, "never for an EU organization, even when switched on");
        Assert.That(ResearchExport.VoiceExportable(VoiceMeasuresSetting.On, false, true), Is.False, "never for an EU student, even when switched on");
        var off = ResearchExport.FilterMetrics(new Dictionary<string, double> { ["speechRateWpm"] = 120, [VoiceStrain.OffKey] = 1, ["wordCount"] = 10 }, voiceAllowed: true);
        Assert.That(off.Keys, Is.EquivalentTo(new[] { "wordCount" }), "an answer marked unmeasured keeps only recognition metrics");
    }

    [Test]
    public void An_exam_with_fewer_consenting_students_than_the_minimum_is_held_back()
    {
        var (input, _) = Cohort(4, _ => ResearchConsentDecision.Agreed);
        var data = ResearchExport.Build(input);
        Assert.That(data.Sessions, Is.Zero);
        Assert.That(data.ExamsSuppressed, Is.EqualTo(1));
        Assert.That(data.Table(ResearchExport.ObjectivesFile).Rows, Is.Empty);
        Assert.That(data.Table(ResearchExport.HumanFile).Rows, Is.Empty);
    }

    [Test]
    public void A_rare_accommodation_is_blanked_within_its_exam()
    {
        var (input, _) = Cohort(8, _ => ResearchConsentDecision.Agreed, i => Session(Guid.Empty, gaze: i == 0, time: i < 5 ? 1.5m : 1m));
        var data = ResearchExport.Build(input);
        var sessions = data.Table(ResearchExport.SessionsFile);
        var gaze = sessions.Columns.ToList().IndexOf("eye_contact_accommodation");
        var time = sessions.Columns.ToList().IndexOf("extra_time");

        Assert.That(sessions.Rows.Select(r => r[gaze]), Is.All.EqualTo(ResearchExport.Suppressed), "one student with it among eight");
        Assert.That(sessions.Rows.Select(r => r[time]), Is.All.EqualTo(ResearchExport.Suppressed), "three without extra time among eight");
        Assert.That(data.AccommodationColumnsSuppressed, Is.EqualTo(2));
    }

    [Test]
    public void The_zip_holds_every_table_a_readme_and_a_manifest()
    {
        var (input, _) = Cohort(5, _ => ResearchConsentDecision.Agreed);
        var data = ResearchExport.Build(input);
        using var zip = new ZipArchive(new MemoryStream(ResearchExport.ToZip(data, T0, 5)));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.That(names, Does.Contain("README.txt").And.Contain("manifest.json").And.Contain(ResearchExport.SessionsFile).And.Contain(ResearchExport.TurnsFile));
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open(), Encoding.UTF8);
        Assert.That(reader.ReadToEnd(), Does.Contain("\"sessions\": 5"));
    }

    [Test]
    public void Cue_codes_never_carry_the_quoted_words()
    {
        Assert.That(ResearchExport.CueKind("hedging (\"I think\", \"maybe\")"), Is.EqualTo("hedging"));
        Assert.That(ResearchExport.CueKind("evidence against understanding (mixes up mass and weight)"), Is.EqualTo("evidence-against"));
        Assert.That(ResearchExport.CueKind("recited, not accepted (matches the textbook)"), Is.EqualTo("recited"));
        Assert.That(ResearchExport.CuesOf(null), Is.Empty);
        Assert.That(ResearchExport.CuesOf("{not json"), Is.Empty);
    }

    [Test]
    public void The_research_notice_hash_changes_with_its_wording()
    {
        var a = ResearchConsentNotice.Invitation("Org", "Pilot", "contact", null, 90);
        var b = ResearchConsentNotice.Invitation("Org", "Pilot", "contact", "IRB-1", 90);
        Assert.That(a.Sha256, Has.Length.EqualTo(64));
        Assert.That(a.Sha256, Is.Not.EqualTo(b.Sha256));
        Assert.That(a.Sha256, Is.EqualTo(ResearchConsentNotice.Invitation("Org", "Pilot", "contact", null, 90).Sha256));
    }
}

/// <summary>Consent gating against the database (GAD-T452): research off refuses a choice, a stale text is refused, withdrawal works.</summary>
public sealed class ResearchConsentGatingTests : ServerTest
{
    [Test]
    public async Task A_choice_needs_research_on_and_the_text_actually_shown()
    {
        var org = Guid.NewGuid();
        var student = Guid.NewGuid();
        await using var db = OrgDb(org);
        var svc = new ResearchService(db, Clock);

        var off = await svc.RecordAsync(org, student, ResearchConsentDecision.Agreed, new string('0', 64), "test");
        Assert.That(off.Ok, Is.False, "research is off: nothing to agree to");

        db.ResearchSettings.Add(new ResearchSettings { OrganizationId = org, Enabled = true, StudyTitle = "Pilot", PseudonymKey = ResearchExport.NewKey(), UpdatedAt = Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var status = await svc.StatusAsync(org, student);
        Assert.That(status.Current, Is.Null);

        var stale = await svc.RecordAsync(org, student, ResearchConsentDecision.Agreed, new string('0', 64), "test");
        Assert.That(stale.Ok, Is.False, "a hash that isn't the current wording is refused");

        var agreed = await svc.RecordAsync(org, student, ResearchConsentDecision.Agreed, status.Invitation!.Sha256, "test");
        Assert.That(agreed.Ok, Is.True);
        Assert.That((await svc.StatusAsync(org, student)).Current, Is.EqualTo(ResearchConsentDecision.Agreed));
        Assert.That((await svc.StatusAsync(org, student)).StudyCode, Does.StartWith("S-"));

        var name = await db.Organizations.Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync() ?? "Your organization";
        Clock.Now = Clock.Now.AddMinutes(1);
        var withdrawn = await svc.RecordAsync(org, student, ResearchConsentDecision.Withdrawn, ResearchConsentNotice.Withdrawal(name).Sha256, "test");
        Assert.That(withdrawn.Ok, Is.True);
        Assert.That(ResearchConsents.CurrentlyAgreed(await svc.ConsentsAsync(org)), Does.Not.Contain(student));
        var audit = await db.AuditEvents.AnyAsync(a => a.OrganizationId == org && a.Action.StartsWith("research.consent"));
        Assert.That(audit, Is.False, "a student's own choice is not put in the audit log that graders' admins read");
    }

    [Test]
    public async Task An_export_without_research_set_up_is_refused_and_one_with_it_is_audited()
    {
        var org = Guid.NewGuid();
        var admin = Guid.NewGuid();
        await using var db = Db(Member(org, admin, OrgRoles.Admin));
        var svc = new ResearchService(db, Clock);
        Assert.That(await svc.ExportAsync(Member(org, admin, OrgRoles.Admin), null), Is.Null);

        db.ResearchSettings.Add(new ResearchSettings { OrganizationId = org, Enabled = true, StudyTitle = "Pilot", PseudonymKey = ResearchExport.NewKey(), UpdatedAt = Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var export = await svc.ExportAsync(Member(org, admin, OrgRoles.Admin), null);
        Assert.That(export, Is.Not.Null);
        Assert.That(export!.Value.Data.Sessions, Is.Zero, "nobody has agreed yet");
        Assert.That(await db.AuditEvents.AnyAsync(a => a.OrganizationId == org && a.Action == "research.exported" && a.ActorUserId == admin), Is.True);
    }
}
