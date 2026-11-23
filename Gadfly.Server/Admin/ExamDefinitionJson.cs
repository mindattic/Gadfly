using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;

namespace Gadfly.Server.Admin;

// An exam definition as a versioned JSON file (GAD-T297): everything that describes an exam (its details, session settings,
// learning objectives with their rubric criteria, level descriptors and hint ladders, and its grading policy) and nothing about
// who took it. Exported from one organization, imported into the same or another one as a new draft. The source material is not
// part of the file: the importer picks the source collection, and objectives whose concept is not in it arrive unlinked.

/// <summary>The file's top level. <see cref="Format"/> and <see cref="Version"/> identify it; everything else is in <see cref="Exam"/>.</summary>
public sealed class ExamDefinitionFile
{
    public string? Format { get; set; }
    public int? Version { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
    /// <summary>Informational: the app version label that wrote the file.</summary>
    public string? ExportedBy { get; set; }
    public ExamDefinition? Exam { get; set; }
}

public sealed class ExamDefinition
{
    public string? Title { get; set; }
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public string? Instructions { get; set; }
    /// <summary>"Examination" or "Learning".</summary>
    public string? AssessmentMode { get; set; }
    /// <summary>"Voice" or "Text".</summary>
    public string? Mode { get; set; }
    /// <summary>"Off", "Standard" or "Strict".</summary>
    public string? IntegrityLevel { get; set; }
    public string? VoiceId { get; set; }
    public string? AvatarId { get; set; }
    /// <summary>The examiner's voice quality/speed tier (GAD-T617): "fast" or "quality"; absent uses the organization's.</summary>
    public string? VoiceTier { get; set; }
    /// <summary>Informational: what the source material and course were called where the file was exported.</summary>
    public string? CourseTitle { get; set; }
    public string? CollectionTitle { get; set; }
    public ExamSettingsDefinition? Settings { get; set; }
    public GradingPolicyDefinition? GradingPolicy { get; set; }
    public List<ObjectiveDefinition>? Objectives { get; set; }
}

/// <summary>The session settings an instructor controls (a subset of <see cref="SessionPolicy"/>; the seed is never exported).</summary>
public sealed class ExamSettingsDefinition
{
    public int? DurationMinutes { get; set; }
    public int? QuestionLimit { get; set; }
    public int? MaxQuestionsPerConcept { get; set; }
    public int? Difficulty { get; set; }
    public double? MasteryThreshold { get; set; }
    public double? AporiaThreshold { get; set; }
    public int? MinTurnsBeforeAporia { get; set; }
    public int? MinCorroboratingAnswers { get; set; }
    public int? MaxAssistsPerObjective { get; set; }
    public int? MinSummativeObjectives { get; set; }
    public bool? ReadOutCheck { get; set; }
    /// <summary>Concept keys of an exam without learning objectives (the older exam builder), in order.</summary>
    public List<string>? ConceptKeys { get; set; }
    /// <summary>The spoken language expected on this exam (GAD-T357), e.g. "en" or "en-US"; null lets the recognizer detect it.</summary>
    public string? Language { get; set; }
}

/// <summary>The grading policy with weights as fractions (0.4 / 0.6), as <see cref="PolicySnapshot"/>.</summary>
public sealed class GradingPolicyDefinition
{
    public decimal? AiWeight { get; set; }
    public decimal? HumanWeight { get; set; }
    public decimal? AdditionalReviewAt { get; set; }
    public decimal? MandatoryReconciliationAt { get; set; }
    public bool? BlindHumanGrading { get; set; }
    /// <summary>"HumanReview" or "AdditionalExamination".</summary>
    public string? InconclusiveProcedure { get; set; }
    public bool? AllowAssistedEvidence { get; set; }
    /// <summary>GAD-T439: graders see a session code instead of the student's name until the human score is submitted.</summary>
    public bool? AnonymousMarking { get; set; }
}

public sealed class ObjectiveDefinition
{
    public string? Key { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ConceptKey { get; set; }
    public decimal? Weight { get; set; }
    public decimal? MaxScore { get; set; }
    public List<string>? HintLadder { get; set; }
    /// <summary>
    /// The fixed opening question approved for this objective (GAD-T436), for the record. An import does not approve it again:
    /// it has to be checked against the importing organization's own source material, on the Objectives step.
    /// </summary>
    public string? OpeningQuestion { get; set; }
    public List<CriterionDefinition>? Criteria { get; set; }
}

public sealed class CriterionDefinition
{
    /// <summary>An <see cref="AssessmentDimension"/> name, e.g. "ConceptualUnderstanding".</summary>
    public string? Dimension { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Weight { get; set; }
    /// <summary>Six descriptors, level 0 to level 5. A blank one takes the level's default wording.</summary>
    public List<string>? Levels { get; set; }
}

/// <summary>The result of reading a file: a valid definition, or every problem found (never just the first).</summary>
public sealed record ExamDefinitionReadResult(ExamDefinition? Definition, IReadOnlyList<string> Errors)
{
    public bool Ok => Definition is not null && Errors.Count == 0;
}

/// <summary>Writing and reading <see cref="ExamDefinitionFile"/> (GAD-T297). Pure: no database, so it is tested directly.</summary>
public static class ExamDefinitionJson
{
    public const string FormatName = "gadfly.exam";
    public const int CurrentVersion = 1;
    /// <summary>Largest file accepted on import.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxObjectives = 100;
    public const int MaxCriteriaPerObjective = 20;

    private static readonly JsonSerializerOptions Write = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 32,
    };

    /// <summary>The definition of an exam as it is stored now.</summary>
    public static ExamDefinitionFile Build(Exam exam, IReadOnlyList<LearningObjective> objectives, GradingPolicy? grading,
        string? courseTitle, string? collectionTitle, DateTimeOffset exportedAt, string? exportedBy = null)
    {
        var p = ExamService.ReadPolicy(exam.PolicyJson);
        var g = grading is null ? PolicySnapshot.Default : PolicySnapshot.Of(grading);
        return new ExamDefinitionFile
        {
            Format = FormatName,
            Version = CurrentVersion,
            ExportedAt = exportedAt,
            ExportedBy = exportedBy,
            Exam = new ExamDefinition
            {
                Title = exam.Title,
                Subject = exam.Subject,
                Topic = exam.Topic,
                Instructions = exam.Instructions,
                AssessmentMode = exam.AssessmentMode.ToString(),
                Mode = exam.Mode.ToString(),
                IntegrityLevel = exam.IntegrityLevel.ToString(),
                VoiceId = exam.VoiceId,
                AvatarId = exam.AvatarId,
                VoiceTier = exam.VoiceTier,
                CourseTitle = courseTitle,
                CollectionTitle = collectionTitle,
                Settings = new ExamSettingsDefinition
                {
                    DurationMinutes = (int)Math.Round(p.TimeBudget.TotalMinutes),
                    QuestionLimit = p.MaxTurns,
                    MaxQuestionsPerConcept = p.MaxTurnsPerConcept,
                    Difficulty = p.Difficulty,
                    MasteryThreshold = p.MasteryThreshold,
                    AporiaThreshold = p.AporiaThreshold,
                    MinTurnsBeforeAporia = p.MinTurnsBeforeAporia,
                    MinCorroboratingAnswers = p.MinCorroboratingAnswers,
                    MaxAssistsPerObjective = p.MaxAssistsPerObjective,
                    MinSummativeObjectives = p.MinSummativeObjectives,
                    ReadOutCheck = p.ReadOutCheck,
                    ConceptKeys = [.. p.ConceptIds],
                    Language = p.Language,
                },
                GradingPolicy = new GradingPolicyDefinition
                {
                    AiWeight = g.AiWeight,
                    HumanWeight = g.HumanWeight,
                    AdditionalReviewAt = g.AdditionalReviewAt,
                    MandatoryReconciliationAt = g.MandatoryReconciliationAt,
                    BlindHumanGrading = g.BlindHumanGrading,
                    InconclusiveProcedure = g.InconclusiveProcedure.ToString(),
                    AllowAssistedEvidence = g.AllowAssistedEvidence,
                    AnonymousMarking = g.AnonymousMarking,
                },
                Objectives = objectives.OrderBy(o => o.Ordinal).Select(o => new ObjectiveDefinition
                {
                    Key = o.Key,
                    Title = o.Title,
                    Description = o.Description,
                    ConceptKey = o.ConceptKey,
                    Weight = o.Weight,
                    MaxScore = o.MaxScore,
                    HintLadder = [.. Sessions.SessionObjectives.HintLadder(o)],
                    OpeningQuestion = p.OpeningQuestions.TryGetValue(o.Key, out var opening) ? opening : null,
                    Criteria = o.Criteria.OrderBy(c => c.Ordinal).Select(c => new CriterionDefinition
                    {
                        Dimension = c.Dimension.ToString(),
                        Name = c.Name,
                        Description = c.Description,
                        Weight = c.Weight,
                        Levels = Enum.GetValues<MasteryLevel>().OrderBy(l => (int)l)
                            .Select(l => c.Levels.FirstOrDefault(x => x.Level == l)?.Descriptor is { Length: > 0 } t ? t : MasteryLevels.DefaultDescriptor(l))
                            .ToList(),
                    }).ToList(),
                }).ToList(),
            },
        };
    }

    public static string Serialize(ExamDefinitionFile file) => JsonSerializer.Serialize(file, Write);

    /// <summary>A safe file name for the download: the title's letters and digits, then ".gadfly-exam.json".</summary>
    public static string FileName(string? title)
    {
        var stem = new string((title ?? "").Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        while (stem.Contains("--", StringComparison.Ordinal)) stem = stem.Replace("--", "-", StringComparison.Ordinal);
        stem = stem.Trim('-');
        if (stem.Length > 60) stem = stem[..60].TrimEnd('-');
        return (stem.Length == 0 ? "exam" : stem) + ".gadfly-exam.json";
    }

    /// <summary>
    /// Reads and validates a file. Every problem is reported, each naming where it is (for example
    /// "Objective 2 (obj-2), criterion 1: give it a name."), so the person importing can fix the file in one go.
    /// </summary>
    public static ExamDefinitionReadResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, ["The file is empty."]);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) return new(null, [$"The file is larger than {MaxBytes / (1024 * 1024)} MB; an exam definition is much smaller than that."]);
        ExamDefinitionFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExamDefinitionFile>(json, Read);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1})" : "";
            return new(null, [$"This is not valid JSON{where}. Export the exam again, or check the file was not cut short or edited by hand."]);
        }
        if (file is null) return new(null, ["The file is empty."]);
        var errors = Validate(file);
        return new(errors.Count == 0 ? file.Exam : null, errors);
    }

    /// <summary>Every problem with the file, in reading order; empty when it can be imported.</summary>
    public static List<string> Validate(ExamDefinitionFile file)
    {
        var errors = new List<string>();
        if (!string.Equals(file.Format, FormatName, StringComparison.Ordinal))
            errors.Add($"This is not a Gadfly exam file (its \"format\" should be \"{FormatName}\").");
        if (file.Version is not { } version) errors.Add("The file does not say which version of the format it uses (\"version\").");
        else if (version > CurrentVersion) errors.Add($"The file uses format version {version}, newer than this app reads ({CurrentVersion}). Update Gadfly, or export it again from an older version.");
        else if (version < 1) errors.Add($"Format version {version} is not a real version.");
        var e = file.Exam;
        if (e is null)
        {
            errors.Add("The file has no \"exam\" section.");
            return errors;
        }

        var title = e.Title?.Trim() ?? "";
        if (title.Length == 0) errors.Add("Details: the exam needs a title.");
        else if (title.Length > 200) errors.Add("Details: keep the title under 200 characters.");
        if ((e.Subject?.Trim().Length ?? 0) > 200) errors.Add("Details: keep the subject under 200 characters.");
        if ((e.Topic?.Trim().Length ?? 0) > 200) errors.Add("Details: keep the topic under 200 characters.");
        if ((e.Instructions?.Length ?? 0) > 4000) errors.Add("Details: keep the instructions under 4000 characters.");
        if ((e.VoiceId?.Trim().Length ?? 0) > 100) errors.Add("Details: the examiner voice id is longer than 100 characters.");
        else if (!string.IsNullOrWhiteSpace(e.VoiceId) && !Voice.ExaminerVoice.IsWellFormed(e.VoiceId))
            errors.Add("Details: the examiner voice id is not an ElevenLabs voice id (letters and digits only).");
        if ((e.AvatarId?.Trim().Length ?? 0) > 100) errors.Add("Details: the examiner avatar id is longer than 100 characters.");
        if (!string.IsNullOrWhiteSpace(e.VoiceTier) && Voice.VoiceTier.Find(e.VoiceTier) is null)
            errors.Add($"Details: the voice tier \"{e.VoiceTier.Trim()}\" is not one of {string.Join(", ", Voice.VoiceTier.All.Select(t => t.Id))}.");
        EnumOrNull<AssessmentMode>(e.AssessmentMode, "Details: the assessment mode", errors);
        EnumOrNull<ExamMode>(e.Mode, "Details: the answer mode", errors);
        EnumOrNull<IntegrityLevel>(e.IntegrityLevel, "Details: the integrity level", errors);

        if (e.Settings is { } s)
        {
            if (s.DurationMinutes is { } d && (d < ExamService.MinDurationMinutes || d > ExamService.MaxDurationMinutes))
                errors.Add($"Settings: the duration must be {ExamService.MinDurationMinutes} to {ExamService.MaxDurationMinutes} minutes (it is {d}).");
            if (s.QuestionLimit is { } q && (q < 1 || q > AssessmentService.MaxQuestionLimit))
                errors.Add($"Settings: the question limit must be 1 to {AssessmentService.MaxQuestionLimit} (it is {q}).");
            if (s.ConceptKeys is { } keys && keys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 120))
                errors.Add("Settings: every concept key must be 1 to 120 characters.");
            try
            {
                ToPolicy(s, new SessionPolicy()).Validate();
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Settings: {ex.Message}");
            }
        }

        if (e.GradingPolicy is { } g)
        {
            if (!string.IsNullOrWhiteSpace(g.InconclusiveProcedure) && !Enum.TryParse<InconclusiveProcedure>(g.InconclusiveProcedure, true, out _))
                errors.Add($"Grading policy: \"{g.InconclusiveProcedure}\" is not a known inconclusive procedure (HumanReview or AdditionalExamination).");
            else if (AssessmentService.ValidatePolicy(ToSnapshot(g)) is { } problem)
                errors.Add($"Grading policy: {problem}");
        }

        var objectives = e.Objectives ?? [];
        if (objectives.Count > MaxObjectives) errors.Add($"Objectives: at most {MaxObjectives} learning objectives (the file has {objectives.Count}).");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(objectives.Count, MaxObjectives); i++)
        {
            var o = objectives[i];
            if (o is null) { errors.Add($"Objective {i + 1}: it is empty."); continue; }
            var key = o.Key?.Trim() ?? "";
            var at = $"Objective {i + 1}{(key.Length > 0 ? $" ({key})" : "")}";
            if (key.Length == 0) errors.Add($"{at}: it needs a key (for example \"obj-{i + 1}\").");
            else if (key.Length > 120) errors.Add($"{at}: its key is longer than 120 characters.");
            else if (!seen.Add(key)) errors.Add($"{at}: another objective already uses the key \"{key}\"; keys must be unique.");
            var otitle = o.Title?.Trim() ?? "";
            if (otitle.Length == 0) errors.Add($"{at}: give it a title.");
            else if (otitle.Length > 300) errors.Add($"{at}: keep its title under 300 characters.");
            if ((o.Description?.Length ?? 0) > 4000) errors.Add($"{at}: keep its description under 4000 characters.");
            if ((o.ConceptKey?.Trim().Length ?? 0) > 120) errors.Add($"{at}: its concept key is longer than 120 characters.");
            if (o.Weight is { } w && (w < 0 || w > 1000)) errors.Add($"{at}: the weight must be 0 to 1000.");
            if (o.MaxScore is { } m && (m <= 0 || m > AssessmentService.MaxObjectiveScore)) errors.Add($"{at}: the maximum score must be above 0 and at most {AssessmentService.MaxObjectiveScore}.");
            if (o.HintLadder is { } ladder)
            {
                var (_, ladderError) = HintLadders.Parse(string.Join('\n', ladder.Where(r => r is not null)));
                if (ladderError is not null) errors.Add($"{at}: {ladderError}");
                if (ladder.Any(r => r?.Contains('\n') == true)) errors.Add($"{at}: a hint ladder rung must be one line.");
            }
            if ((o.OpeningQuestion?.Length ?? 0) > SessionPolicy.MaxOpeningQuestionLength)
                errors.Add($"{at}: keep its opening question under {SessionPolicy.MaxOpeningQuestionLength} characters.");
            var criteria = o.Criteria ?? [];
            if (criteria.Count > MaxCriteriaPerObjective) errors.Add($"{at}: at most {MaxCriteriaPerObjective} rubric criteria.");
            for (var j = 0; j < Math.Min(criteria.Count, MaxCriteriaPerObjective); j++)
            {
                var c = criteria[j];
                var cat = $"{at}, criterion {j + 1}";
                if (c is null) { errors.Add($"{cat}: it is empty."); continue; }
                if (!Enum.TryParse<AssessmentDimension>(c.Dimension?.Trim(), true, out var dim) || !Enum.IsDefined(dim))
                    errors.Add($"{cat}: \"{c.Dimension}\" is not a known dimension ({string.Join(", ", Enum.GetNames<AssessmentDimension>())}).");
                var name = c.Name?.Trim() ?? "";
                if (name.Length == 0) errors.Add($"{cat}: give it a name.");
                else if (name.Length > 200) errors.Add($"{cat}: keep its name under 200 characters.");
                if ((c.Description?.Length ?? 0) > 4000) errors.Add($"{cat}: keep its description under 4000 characters.");
                if (c.Weight is { } cw && (cw <= 0 || cw > 1000)) errors.Add($"{cat}: the weight must be above 0 and at most 1000.");
                if (c.Levels is { } levels)
                {
                    if (levels.Count != 6) errors.Add($"{cat}: describe all six levels, 0 to 5 (the file has {levels.Count}).");
                    if (levels.Any(l => l?.Length > 2000)) errors.Add($"{cat}: keep each level descriptor under 2000 characters.");
                }
            }
        }
        return errors;
    }

    /// <summary>The session settings applied over <paramref name="basis"/> (fields the file leaves out keep the basis's value).</summary>
    public static SessionPolicy ToPolicy(ExamSettingsDefinition? s, SessionPolicy basis)
    {
        if (s is null) return basis;
        return basis with
        {
            TimeBudget = s.DurationMinutes is { } d ? TimeSpan.FromMinutes(d) : basis.TimeBudget,
            MaxTurns = s.QuestionLimit ?? basis.MaxTurns,
            MaxTurnsPerConcept = s.MaxQuestionsPerConcept ?? basis.MaxTurnsPerConcept,
            Difficulty = s.Difficulty ?? basis.Difficulty,
            MasteryThreshold = s.MasteryThreshold ?? basis.MasteryThreshold,
            AporiaThreshold = s.AporiaThreshold ?? basis.AporiaThreshold,
            MinTurnsBeforeAporia = s.MinTurnsBeforeAporia ?? basis.MinTurnsBeforeAporia,
            MinCorroboratingAnswers = s.MinCorroboratingAnswers ?? basis.MinCorroboratingAnswers,
            MaxAssistsPerObjective = s.MaxAssistsPerObjective ?? basis.MaxAssistsPerObjective,
            MinSummativeObjectives = s.MinSummativeObjectives ?? basis.MinSummativeObjectives,
            ReadOutCheck = s.ReadOutCheck ?? basis.ReadOutCheck,
            ConceptIds = s.ConceptKeys is { } keys ? keys.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList() : basis.ConceptIds,
            Language = s.Language is null ? basis.Language : (s.Language.Trim() is { Length: > 0 } lang ? lang : null),
        };
    }

    /// <summary>The grading policy the file describes (fields it leaves out take the default policy's value).</summary>
    public static PolicySnapshot ToSnapshot(GradingPolicyDefinition? g)
    {
        var d = PolicySnapshot.Default;
        if (g is null) return d;
        return new PolicySnapshot(g.AiWeight ?? d.AiWeight, g.HumanWeight ?? d.HumanWeight, g.AdditionalReviewAt ?? d.AdditionalReviewAt,
            g.MandatoryReconciliationAt ?? d.MandatoryReconciliationAt, g.BlindHumanGrading ?? d.BlindHumanGrading,
            Enum.TryParse<InconclusiveProcedure>(g.InconclusiveProcedure, true, out var proc) && Enum.IsDefined(proc) ? proc : d.InconclusiveProcedure,
            g.AllowAssistedEvidence ?? d.AllowAssistedEvidence, g.AnonymousMarking ?? d.AnonymousMarking);
    }

    /// <summary>An enum value by name, or <paramref name="fallback"/> when the text is blank.</summary>
    public static T EnumOr<T>(string? text, T fallback) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(text) && Enum.TryParse<T>(text.Trim(), true, out var v) && Enum.IsDefined(v) ? v : fallback;

    private static void EnumOrNull<T>(string? text, string what, List<string> errors) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Enum.TryParse<T>(text.Trim(), true, out var v) || !Enum.IsDefined(v))
            errors.Add($"{what} \"{text}\" is not one of {string.Join(", ", Enum.GetNames<T>())}.");
    }
}
