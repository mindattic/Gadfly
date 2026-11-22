using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Sources.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The objective path in the reviewer CSV exports (GAD-T461): the assessment report CSV gains path, coverage and exclusion
/// columns, and the review turns CSV each turn's objective path and whether its answer counted. A blind grader gets only the
/// questions (criteria and exclusions empty); a student report has no path at all. Pure: no database.
/// </summary>
[TestFixture]
public class ObjectivePathCsvTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static AnswerJudgment Judged(double score, RecitationFlag? recited = null) => new()
    {
        Criteria = [new CriterionScore { Criterion = "accuracy", Score = score }], OverallScore = score, Recitation = recited,
    };

    private static SocraticSession Session()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        s.StartClock(T0);
        foreach (var (move, judgment) in new[]
                 {
                     (MoveKind.Elicit, Judged(0.8)),
                     (MoveKind.Probe, Judged(0.7, new RecitationFlag { MatchesSource = true, Reason = "close to the source" })),
                     (MoveKind.Probe, Judged(0.6)),
                 })
        {
            s.Ask("c1", move, "Question?", T0, objectiveKey: "o1");
            s.RecordAnswer(new Answer { Text = "An answer.", CompletedAt = T0 });
            s.RecordJudgment(judgment, 0.5);
        }
        return s;
    }

    private static readonly List<RubricCriterion> Rubric =
    [
        new() { Name = "Definition", Weight = 1m, Ordinal = 0 },
        new() { Name = "Reasoning", Weight = 3m, Ordinal = 1 },
    ];

    private static AiGradeView Ai() => new(new AiObjectiveGrade { Level = MasteryLevel.BasicExplanation },
        [new CriterionGrade("Definition", AssessmentDimension.ConceptualUnderstanding, MasteryLevel.BasicExplanation, 1m, 2m, "", [], [], [], false)],
        [], [], [], [], []);

    private static ObjectivePath Path(bool aiVisible) => ObjectivePath.Build(Session(), "o1", Rubric, Ai(), aiVisible, assistedCounts: false);

    private static AssessmentReport Report(ObjectivePath? path, bool forStudent) => new()
    {
        SessionId = Guid.NewGuid(), StudentName = "Ada", ExamTitle = "Biology", Attempt = 1, EndedAt = T0, Status = "graded",
        FinalPercent = 70m, Label = "Pass", Policy = "", KnowledgeGaps = [], Approvals = [], Appeals = [], IsMock = false,
        ForStudent = forStudent, IsApproved = true,
        Objectives = [new ReportObjective("o1", "Cells", 10m, 1m, 7m, "Level 2", "high", false, false, null, null, null, 7m, null, null, [], null)],
        Paths = path is null ? new Dictionary<string, ObjectivePath>() : new Dictionary<string, ObjectivePath> { ["o1"] = path },
    };

    /// <summary>The report CSV's header and single objective row, as header name to cell (no commas in these test cells).</summary>
    private static Dictionary<string, string> Columns(AssessmentReport report)
    {
        var lines = report.ToCsv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(2));
        var header = lines[0].Split(',');
        var cells = SplitCsv(lines[1]);
        Assert.That(cells, Has.Count.EqualTo(header.Length));
        return header.Zip(cells).ToDictionary(p => p.First, p => p.Second);
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    [Test]
    public void The_report_csv_has_the_path_columns_for_a_reviewer()
    {
        var row = Columns(Report(Path(aiVisible: true), forStudent: false));
        Assert.That(row["path_questions"], Is.EqualTo("3"));
        Assert.That(row["path_follow_ups"], Is.EqualTo("2"));
        Assert.That(row["path_moves"], Is.EqualTo("opening question=1 | probe the reasoning=2"));
        Assert.That(row["criteria_reached"], Is.EqualTo("Definition"));
        Assert.That(row["criteria_not_reached"], Is.EqualTo("Reasoning"));
        Assert.That(row["coverage_percent"], Is.EqualTo("25"));
        Assert.That(row["exclusions"], Is.EqualTo("turn 2: " + ObjectivePath.Recited));
    }

    [Test]
    public void A_blind_grader_gets_only_the_questions_in_the_report_csv()
    {
        var row = Columns(Report(Path(aiVisible: false), forStudent: false));
        Assert.That(row["path_questions"], Is.EqualTo("3"));
        Assert.That(row["path_moves"], Is.Not.Empty);
        Assert.That(row["criteria_reached"], Is.Empty);
        Assert.That(row["criteria_not_reached"], Is.Empty);
        Assert.That(row["coverage_percent"], Is.Empty);
        Assert.That(row["exclusions"], Is.Empty);
    }

    [Test]
    public void A_student_report_csv_carries_no_path_even_if_one_is_set()
    {
        var row = Columns(Report(Path(aiVisible: true), forStudent: true));
        foreach (var h in ObjectivePathCsv.ObjectiveHeaders) Assert.That(row[h], Is.Empty, h);
    }

    [Test]
    public void Nothing_excluded_reads_none_not_empty()
    {
        var path = Path(aiVisible: true) with { Exclusions = [] };
        Assert.That(ObjectivePathCsv.ObjectiveCells(path)[^1], Is.EqualTo("none"));
    }

    [Test]
    public void Turn_cells_say_whether_each_answer_counted_and_hide_it_while_blind()
    {
        var shown = Path(aiVisible: true);
        Assert.That(ObjectivePathCsv.TurnCells(shown, 0)[2], Is.EqualTo("no"));
        Assert.That(ObjectivePathCsv.TurnCells(shown, 1)[2], Is.EqualTo("yes: " + ObjectivePath.Recited));
        Assert.That(ObjectivePathCsv.TurnCells(shown, 1)[1], Does.StartWith("Reached 1 of 2 criteria"));

        var blind = ObjectivePathCsv.TurnCells(Path(aiVisible: false), 1);
        Assert.That(blind[0], Does.StartWith("3 questions"));
        Assert.That(blind[1], Is.Null);
        Assert.That(blind[2], Is.Null);

        Assert.That(ObjectivePathCsv.TurnCells(null, 0), Is.All.Null);
    }

    [Test]
    public void Turn_objectives_come_from_the_stored_transcript()
    {
        var session = new ExamSession { TranscriptJson = SessionTranscriptSerializer.Serialize(Session()) };
        var map = ObjectivePathCsv.TurnObjectives(session);
        Assert.That(map.Values, Is.All.EqualTo("o1"));
        Assert.That(map.Keys, Is.EquivalentTo(new[] { 0, 1, 2 }));
        Assert.That(ObjectivePathCsv.TurnObjectives(new ExamSession { TranscriptJson = "not json" }), Is.Empty);
    }
}
