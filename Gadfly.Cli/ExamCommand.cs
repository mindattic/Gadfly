using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Sources;
using MindAttic.Legion;

namespace Gadfly.Cli;

/// <summary>
/// <c>gadfly exam --source &lt;folder|file&gt;</c>: a text-only Socratic exam over source material in the
/// terminal (GAD-T38, milestone 1 of docs/SOCRATIC_PLATFORM.md). Ingests the material, builds (or
/// reuses) the concept map and rubrics, examines you concept by concept, and writes a report.
/// </summary>
public static class ExamCommand
{
    public static string DefaultExamsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "exams");

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(GadflyCli.IsHelp))
        {
            PrintUsage();
            return 0;
        }

        string? source = null, collectionId = null, reportPath = null;
        var tier = LegionTierResolver.DefaultTier;
        List<string>? providerOverride = null;
        List<string>? conceptKeys = null;
        int minutes = 20, maxTurns = 30, difficulty = 3, maxConcepts = 4;
        var rebuild = false;
        var verbose = false;
        var listOnly = false;
        var learning = false;
        var timeMultiplier = 1.0;

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--source": source = Next(); break;
                case "--collection": collectionId = Next(); break;
                case "--report": reportPath = Next(); break;
                case "--minutes": if (int.TryParse(Next(), out var m) && m > 0) minutes = m; break;
                case "--max-turns": if (int.TryParse(Next(), out var t) && t > 0) maxTurns = t; break;
                case "--difficulty": if (int.TryParse(Next(), out var d) && d is >= 1 and <= 5) difficulty = d; break;
                case "--max-concepts": if (int.TryParse(Next(), out var c) && c > 0) maxConcepts = c; break;
                case "--concepts":
                    conceptKeys = Next()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "--rebuild": rebuild = true; break;
                case "--verbose": verbose = true; break;
                case "--list-concepts": listOnly = true; break;
                case "--learning": learning = true; break;
                case "--time-multiplier":
                    if (double.TryParse(Next(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tm)
                        && tm is >= 1 and <= 4) timeMultiplier = tm;
                    else
                    {
                        Console.Error.WriteLine("exam: --time-multiplier takes a number from 1 to 4 (e.g. 1.5).");
                        return 2;
                    }
                    break;
                case "--tier":
                    if (Enum.TryParse<ModelTier>(Next(), ignoreCase: true, out var parsedTier)) tier = parsedTier;
                    break;
                case "--voters":
                    providerOverride = Next()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "--single-voter":
                    if (Next() is { } one) providerOverride = [one];
                    break;
                default:
                    if (source is null && !args[i].StartsWith('-')) source = args[i];
                    else
                    {
                        Console.Error.WriteLine($"exam: unknown option '{args[i]}'. Run 'gadfly exam --help'.");
                        return 2;
                    }
                    break;
            }
        }

        if (source is null)
        {
            Console.Error.WriteLine("exam: give the source material with --source <folder|file>.");
            return 2;
        }
        if (!Directory.Exists(source) && !File.Exists(source))
        {
            Console.Error.WriteLine($"exam: no file or folder at '{source}'.");
            return 1;
        }
        var untrusted = LegionTierResolver.UntrustedIds(providerOverride);
        if (untrusted.Count > 0)
        {
            Console.Error.WriteLine($"exam: untrusted provider id(s) {string.Join(", ", untrusted)}; --voters/--single-voter accept only {string.Join(", ", LegionTierResolver.TrustedProviderIds)}.");
            return 2;
        }

        collectionId ??= SlugFor(source);
        var store = new FileSourceStore();
        var files = new ConceptMapFiles(store);
        var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));

        Console.WriteLine($"Reading source material into collection '{collectionId}'...");
        var ingest = await new SourceIngestor(store, [new PlainTextExtractor(), new TutorIngestionExtractor()]).IngestAsync(source, collectionId, progress: progress);
        Console.WriteLine($"  {ingest.DocumentsIngested} new or changed, {ingest.DocumentsUnchanged} unchanged, {ingest.Skipped.Count} skipped (unsupported type).");
        if (ingest.Skipped.Count > 0)
            Console.WriteLine("  Readable types: .txt, .md, .pdf, .docx, .epub, .html.");
        foreach (var failure in ingest.Failures)
            Console.Error.WriteLine($"  could not read: {failure}");

        var passages = await store.GetPassagesAsync(collectionId);
        if (passages.Count == 0)
        {
            Console.Error.WriteLine("exam: no readable text in the source material.");
            return 1;
        }

        var config = GadflyConfiguration.Build(tier, providerOverride: providerOverride);
        using var client = new LegionVotingClient(config);
        var retriever = new GroundingRetriever(passages);

        var map = await files.LoadMapAsync(collectionId);
        // An empty saved map (an earlier build where every provider failed) is rebuilt rather than reused.
        if (map is null || rebuild || ingest.DocumentsIngested > 0 || map.Concepts.Count == 0)
        {
            Console.WriteLine("Building the concept map with the panel...");
            try
            {
                map = await new ConceptMapBuilder(client).BuildAsync(collectionId, passages, map, progress);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"exam: {ex.Message}");
                return 1;
            }
            // Never save an empty map from a failed build: it would be reused on every later run.
            if (map.Concepts.Count > 0) await files.SaveMapAsync(map);
        }
        if (map.Concepts.Count == 0)
        {
            Console.Error.WriteLine("exam: the panel found no concepts in the material (check provider keys, or try more text).");
            return 1;
        }

        var rubrics = await files.LoadRubricsAsync(collectionId);
        if (rebuild || ingest.DocumentsIngested > 0 || map.Concepts.Any(c => rubrics.All(r => r.ConceptKey != c.Key)))
        {
            Console.WriteLine("Writing rubrics with the panel...");
            rubrics = await new RubricBuilder(client).BuildAsync(map, retriever, rubrics, progress);
            await files.SaveRubricsAsync(collectionId, rubrics);
        }

        Console.WriteLine();
        Console.WriteLine($"Concepts ({map.Concepts.Count}) — edit {Path.Combine(store.CollectionDirectory(collectionId), "concept-map.json")} to change them:");
        foreach (var c in map.Concepts) Console.WriteLine($"  {c.Key,-32} {c.Title}");
        if (listOnly) return 0;

        var unknown = conceptKeys?.Where(k => map.Find(k) is null).ToList() ?? [];
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"exam: unknown concept key(s): {string.Join(", ", unknown)}.");
            return 2;
        }
        var policy = new SessionPolicy
        {
            ConceptIds = conceptKeys ?? map.Concepts.Take(maxConcepts).Select(c => c.Key).ToList(),
            TimeBudget = TimeSpan.FromMinutes(minutes),
            MaxTurns = maxTurns,
            Difficulty = difficulty,
            Mode = learning ? Gadfly.Assessment.AssessmentMode.Learning : Gadfly.Assessment.AssessmentMode.Examination,
            TimeMultiplier = timeMultiplier,
        };

        var subject = new SourceMaterialSubject(map.CollectionId, map, rubrics, retriever);
        Console.WriteLine();
        Console.WriteLine($"{(learning ? "Learning session" : "Exam")}: {policy.ConceptIds.Count} concept(s), up to {policy.EffectiveTimeBudget.TotalMinutes:0.#} minutes. Answer in your own words;");
        Console.WriteLine("type \"Aporia\" if you honestly don't know. An empty line ends your answer early; Ctrl+Z / Ctrl+D quits.");
        Console.WriteLine();

        var runner = new SourceExamRunner(client, subject)
        {
            Diagnostics = verbose ? new Progress<string>(msg => Console.WriteLine(msg)) : null,
        };
        var (session, report) = await runner.RunAsync(policy, new ConsoleConversation(subject));

        Directory.CreateDirectory(DefaultExamsDir);
        var stamp = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var sessionPath = Path.Combine(DefaultExamsDir, $"{stamp}.session.json");
        await File.WriteAllTextAsync(sessionPath, SessionTranscriptSerializer.Serialize(session));
        // The claim ledger beside it: what `gadfly contradiction-audit` reads (GAD-T440).
        if (runner.Ledger is { } ledger)
            await File.WriteAllTextAsync(Path.Combine(DefaultExamsDir, $"{stamp}.ledger.json"), ledger.ToJson());
        reportPath ??= Path.Combine(DefaultExamsDir, $"{stamp}.report.md");
        await File.WriteAllTextAsync(reportPath, report.ToMarkdown());
        var jsonReportPath = Path.ChangeExtension(reportPath, ".json");
        if (string.Equals(Path.GetFullPath(jsonReportPath), Path.GetFullPath(reportPath), StringComparison.OrdinalIgnoreCase))
            jsonReportPath = reportPath + ".json";
        await File.WriteAllTextAsync(jsonReportPath, report.ToJson());

        Console.WriteLine();
        Console.WriteLine($"Session ended ({session.EndReason}). Recommendation: {report.Recommendation}, overall mastery {report.OverallScore:P0}.");
        foreach (var c in report.Concepts.Where(c => c.Turns > 0))
            Console.WriteLine($"  {c.Title,-40} {c.Mastery:P0}  {c.Status}");
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Transcript: {sessionPath}");
        return 0;
    }

    private static string SlugFor(string path)
    {
        var name = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        var slug = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Length == 0 ? "collection" : slug;
    }

    /// <summary>The terminal as the student: prints questions, reads multi-line answers.</summary>
    private sealed class ConsoleConversation(SourceMaterialSubject subject) : IExamConversation
    {
        private string? _lastConcept;

        public Task AskAsync(Turn turn, CancellationToken ct)
        {
            if (turn.ConceptId != _lastConcept)
            {
                Console.WriteLine($"--- {subject.Concept(turn.ConceptId).Title} ---");
                _lastConcept = turn.ConceptId;
            }
            Console.WriteLine($"Examiner: {turn.Question}");
            return Task.CompletedTask;
        }

        public Task<Answer?> ListenAsync(Turn turn, CancellationToken ct)
        {
            Console.Write("You: ");
            var started = DateTimeOffset.UtcNow;
            var first = Console.ReadLine();
            if (first is null) return Task.FromResult<Answer?>(null);
            var lines = new List<string> { first };
            // Pasted multi-line answers keep arriving without a pause; a typed answer ends at Enter.
            while (InputPending())
            {
                var more = Console.ReadLine();
                if (string.IsNullOrEmpty(more)) break;
                lines.Add(more);
            }
            Console.WriteLine();
            return Task.FromResult<Answer?>(new Answer
            {
                Text = string.Join(" ", lines).Trim(),
                Mode = "text",
                StartedAt = started,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>True when more typed or pasted input is already waiting (never throws on redirected input).</summary>
    private static bool InputPending()
    {
        try { return !Console.IsInputRedirected && Console.KeyAvailable; }
        catch (InvalidOperationException) { return false; }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly exam --source <folder|file> [opts]");
        Console.WriteLine();
        Console.WriteLine("  A text-only Socratic exam over your own source material. Gadfly reads the material,");
        Console.WriteLine("  has the Legion panel build a concept map and a rubric per concept, then questions you");
        Console.WriteLine("  concept by concept: each follow-up is built from your previous answer, every answer is");
        Console.WriteLine("  judged against the source with citations, and contradictions in your own claims are");
        Console.WriteLine("  put back to you. Ends at mastery, aporia, the time budget or the turn limit, then");
        Console.WriteLine("  writes a report. Reads .txt and Markdown for now.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source <path>       Folder or file of source material (required).");
        Console.WriteLine("  --collection <id>     Collection id (default: the folder or file name).");
        Console.WriteLine("  --concepts a,b        Concept keys to examine (default: the first --max-concepts).");
        Console.WriteLine("  --max-concepts N      Concepts examined when --concepts isn't given (default 4).");
        Console.WriteLine("  --list-concepts       Build/show the concept map and exit.");
        Console.WriteLine("  --minutes N           Time budget (default 20).");
        Console.WriteLine("  --max-turns N         Question limit (default 30).");
        Console.WriteLine("  --difficulty 1-5      How hard follow-ups push (default 3).");
        Console.WriteLine("  --learning            Learning Mode: hints and guided explanations when you struggle (assisted, not exam evidence).");
        Console.WriteLine("  --time-multiplier N   Extra-time accommodation: the time budget times N (1 to 4, e.g. 1.5).");
        Console.WriteLine("  --rebuild             Rebuild the concept map and rubrics even if the source is unchanged.");
        Console.WriteLine("  --verbose             Show why each move was chosen and how each question was produced.");
        Console.WriteLine("  --report <path>       Where to write the Markdown report (a .json copy is written beside it).");
        Console.WriteLine("  --tier <t>            low | medium | high | higher | highest (default high).");
        Console.WriteLine("  --voters a,b,c        Narrow the panel within the trusted set (claude, openai, gemini, deepseek).");
        Console.WriteLine("  --single-voter <id>   Run everything through one provider (cheapest).");
    }
}

