using Gadfly.Server.Data;
using Gadfly.Server.Research;
using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Speech-recognition measures in the research export's turns.csv: the per-answer word error rate against a person's reference
/// transcript (GAD-T467), and the answer cut-off measure (GAD-T470), which is a voice measurement and blank wherever those are off.
/// Consenting students only, the export's pseudonyms, counts only (never either transcript). Pure builder, no database.
/// </summary>
[TestFixture]
public sealed class ResearchExportSpeechTests
{
    private const string Key = "dGVzdC1rZXktZm9yLXJlc2VhcmNoLWV4cG9ydC0wMDAwMA==";
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Exam = Guid.NewGuid();

    private const string Recognized = "the light reaction splits water";
    private const string Reference = "the light reactions split the water";

    private static ResearchConsent Consent(Guid student, ResearchConsentDecision d) => new()
    {
        StudentUserId = student, Decision = d, RecordedAt = T0, NoticeVersion = ResearchConsentNotice.Version, ConsentTextSha256 = "x", Source = "test",
    };

    /// <summary>Six students; <paramref name="agrees"/> picks who consents. Every turn is spoken and has a reference's counts.</summary>
    internal static (ResearchExportInput Input, List<ExportSession> Sessions) Cohort(Func<int, bool> agrees, Func<int, string>? metrics = null,
        Func<int, string>? mode = null, Func<int, bool>? eu = null)
    {
        var students = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var sessions = students.Select((s, i) => new ExportSession(Guid.NewGuid(), Guid.NewGuid(), Exam, s, 1, "Ended", "Mastery", T0, T0, T0.AddMinutes(20),
            false, 1m, false, false, eu?.Invoke(i) ?? false, null, null, "Published", 72m, null)).ToList();
        var errors = SpeechAccuracy.WordErrors(Reference, Recognized);
        var turns = sessions.Select((s, i) => new ExportTurn(s.SessionId, Guid.NewGuid(), 0, "photosynthesis", "Elicit", T0.AddSeconds(5), T0.AddSeconds(9),
            T0.AddSeconds(40), mode?.Invoke(i) ?? "voice", metrics?.Invoke(i) ?? """{"wordCount":5}""", 0.6, 0.7, 0.9, false, 3, 0.1, errors)).ToList();
        var input = new ResearchExportInput
        {
            PseudonymKey = Key, MinimumGroupSize = 5, ExportedAt = T0.AddDays(10), Sessions = sessions, Turns = turns,
            Consents = students.Select((s, i) => Consent(s, agrees(i) ? ResearchConsentDecision.Agreed : ResearchConsentDecision.Declined)).ToList(),
        };
        return (input, sessions);
    }

    internal static string Cell(ResearchTable t, IReadOnlyList<string> row, string column) => row[t.Columns.ToList().IndexOf(column)];

    [Test]
    public void WordErrorRate_IsExportedPerAnswer_AsCountsOnly()
    {
        var (input, _) = Cohort(_ => true);
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);

        Assert.That(turns.Columns, Does.Contain("word_error_rate").And.Contain("reference_words"));
        // "the light reactions split the water" (6 words) against "the light reaction splits water": 2 substitutions, 1 deletion.
        foreach (var row in turns.Rows)
        {
            Assert.That(Cell(turns, row, "reference_words"), Is.EqualTo("6"));
            Assert.That(Cell(turns, row, "wer_substitutions"), Is.EqualTo("2"));
            Assert.That(Cell(turns, row, "wer_deletions"), Is.EqualTo("1"));
            Assert.That(Cell(turns, row, "wer_insertions"), Is.EqualTo("0"));
            Assert.That(Cell(turns, row, "word_error_rate"), Is.EqualTo("0.5"));
        }
        var all = string.Join("\n", data.Tables.Select(ResearchExport.ToCsv));
        Assert.That(all, Does.Not.Contain("reaction"), "no transcript text, recognized or reference");
        Assert.That(all, Does.Not.Contain("water"));
    }

    [Test]
    public void WordErrorRate_OnlyForConsentingStudents()
    {
        // Five agree (the minimum group), one declined.
        var (input, sessions) = Cohort(i => i < 5);
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);

        Assert.That(turns.Rows, Has.Count.EqualTo(5));
        var declined = ResearchExport.Pseudonym(Key, "X", sessions[5].SessionId);
        Assert.That(turns.Rows.Select(r => r[0]), Does.Not.Contain(declined));
        Assert.That(turns.Rows.Select(r => r[0]), Is.EquivalentTo(sessions.Take(5).Select(s => ResearchExport.Pseudonym(Key, "X", s.SessionId))),
            "the export's own session pseudonyms");
    }

    [Test]
    public void WordErrorRate_IsBlankWithoutAReference_OrForATypedAnswer()
    {
        var (input, _) = Cohort(_ => true, mode: i => i == 0 ? "text" : "voice");
        input = input with { Turns = input.Turns.Select((t, i) => i == 1 ? t with { WordErrors = null } : t).ToList() };
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);
        var blanks = turns.Rows.Count(r => Cell(turns, r, "word_error_rate") == "" && Cell(turns, r, "reference_words") == "");
        Assert.That(blanks, Is.EqualTo(2));
    }

    // ------------------------------------------------------------------ answer cut-offs (GAD-T470)

    private const string CutOffJson = """{"wordCount":5,"cutoff.count":1,"cutoff.resumeSeconds":1.25}""";
    private const string WatchedJson = """{"wordCount":5,"cutoff.count":0}""";

    [Test]
    public void CutOff_IsExported_WatchedCutOffAndDelay()
    {
        var (input, _) = Cohort(_ => true, metrics: i => i == 0 ? CutOffJson : i == 1 ? WatchedJson : """{"wordCount":5}""");
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);
        var rows = turns.Rows.Select(r => (W: Cell(turns, r, "cut_off_watched"), C: Cell(turns, r, "cut_off"), S: Cell(turns, r, "cut_off_resume_s"))).ToList();

        Assert.That(rows, Has.Exactly(1).EqualTo(("1", "1", "1.25")));
        Assert.That(rows, Has.Exactly(1).EqualTo(("1", "0", "")));
        Assert.That(rows, Has.Exactly(4).EqualTo(("", "", "")), "an answer from before the measure: not watched, blank");
    }

    [Test]
    public void CutOff_OnlyForConsentingStudents()
    {
        var (input, sessions) = Cohort(i => i < 5, metrics: _ => CutOffJson);
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);
        Assert.That(turns.Rows, Has.Count.EqualTo(5));
        Assert.That(turns.Rows.Select(r => r[0]), Does.Not.Contain(ResearchExport.Pseudonym(Key, "X", sessions[5].SessionId)));
    }

    [Test]
    public void CutOff_IsBlankWhereVoiceMeasurementsAreOff()
    {
        // EU student, organization switch off, answer marked unmeasured, typed answer.
        Assert.That(ResearchExport.CutOffCells("voice", Metrics(CutOffJson), voiceAllowed: false), Is.EqualTo(new[] { "", "", "" }));
        var unmeasured = new Dictionary<string, double>(Metrics(CutOffJson)) { [Gadfly.Server.Voice.VoiceStrain.OffKey] = 1 };
        Assert.That(ResearchExport.CutOffCells("voice", unmeasured, voiceAllowed: true), Is.EqualTo(new[] { "", "", "" }));
        Assert.That(ResearchExport.CutOffCells("text", Metrics(CutOffJson), voiceAllowed: true), Is.EqualTo(new[] { "", "", "" }));
        Assert.That(ResearchExport.CutOffCells("voice", Metrics(CutOffJson), voiceAllowed: true), Is.EqualTo(new[] { "1", "1", "1.25" }));

        var (input, sessions) = Cohort(_ => true, metrics: _ => CutOffJson, eu: i => i == 0);
        var data = ResearchExport.Build(input);
        var turns = data.Table(ResearchExport.TurnsFile);
        var euSession = ResearchExport.Pseudonym(Key, "X", sessions[0].SessionId);
        foreach (var row in turns.Rows)
            Assert.That(Cell(turns, row, "cut_off"), row[0] == euSession ? Is.Empty : Is.EqualTo("1"), "never for an EU/EEA student");

        var off = ResearchExport.Build(input with { VoiceMeasures = VoiceMeasuresSetting.Off });
        var offTurns = off.Table(ResearchExport.TurnsFile);
        Assert.That(offTurns.Rows.Select(r => Cell(offTurns, r, "cut_off_watched")), Is.All.Empty, "the organization switched voice measurements off");
    }

    private static IReadOnlyDictionary<string, double> Metrics(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json)!;

    [Test]
    public void WordErrorsOf_SumsTheAnswersWithAReference_AndIgnoresTheRest()
    {
        Assert.That(ResearchExport.WordErrorsOf([]), Is.Null);
        Assert.That(ResearchExport.WordErrorsOf([(Recognized, null), (Recognized, "  ")]), Is.Null);
        var one = ResearchExport.WordErrorsOf([(Recognized, Reference)])!;
        Assert.That((one.Errors, one.ReferenceWords), Is.EqualTo((3, 6)));
        var two = ResearchExport.WordErrorsOf([(Recognized, Reference), ("cells divide", "cells divide"), ("x", null)])!;
        Assert.That((two.Errors, two.ReferenceWords), Is.EqualTo((3, 8)));
        Assert.That(two.Rate, Is.EqualTo(3 / 8.0).Within(1e-9));
    }
}
