using System.Globalization;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Gadfly.Sources.Grading;

namespace Gadfly.Cli;

/// <summary>
/// gadfly bias-check (GAD-T432): runs the grading panel's bias probes (padded answer, less fluent paraphrase, spoken
/// instruction to the grader) on answers from a JSON file. Mock by default: no provider is called and nothing is spent. A live
/// run needs --live and --approve-spend with at least the estimated cost the command prints first, so the owner approves the
/// spend knowingly. Never runs on a schedule.
/// </summary>
public static class BiasCheckCommand
{
    /// <summary>Rough USD per million tokens (input, output), the same fallbacks the server uses when no price is configured.</summary>
    private static readonly IReadOnlyDictionary<string, (decimal In, decimal Out)> Prices = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = (3m, 15m), ["openai"] = (2.5m, 10m), ["gemini"] = (1.25m, 5m), ["deepseek"] = (0.3m, 1.2m),
    };

    private sealed record FileObjective(string? Key, string? Title, string? Description, decimal? MaxScore);
    private sealed record FileAnswer(string? Question, string? Answer);
    private sealed record FileItem(FileObjective? Objective, List<FileAnswer>? Answers);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }
        if (args[0] == "--example")
        {
            Console.WriteLine(JsonSerializer.Serialize(new[]
            {
                new FileItem(new FileObjective("calvin", "The Calvin cycle", "Where and how carbon is fixed.", 100m),
                    [new FileAnswer("Where does the Calvin cycle happen?", "In the stroma of the chloroplast, using ATP and NADPH from the light reactions.")]),
            }, Json));
            return 0;
        }

        string? path = null;
        var live = false;
        decimal? approved = null;
        var tolerance = PanelBiasCheck.DefaultTolerancePercent;
        IReadOnlyList<string> probes = PanelBiasCheck.AllProbes;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live": live = true; break;
                case "--approve-spend" when i + 1 < args.Length && decimal.TryParse(args[i + 1].TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var usd) && usd >= 0:
                    approved = usd; i++; break;
                case "--tolerance" when i + 1 < args.Length && decimal.TryParse(args[i + 1], NumberStyles.Number, CultureInfo.InvariantCulture, out var t) && t >= 0:
                    tolerance = t; i++; break;
                case "--probes" when i + 1 < args.Length:
                    probes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    if (probes.Count == 0 || probes.Any(p => !PanelBiasCheck.AllProbes.Contains(p)))
                    {
                        Console.Error.WriteLine($"gadfly bias-check: --probes takes some of {string.Join(", ", PanelBiasCheck.AllProbes)}.");
                        return 1;
                    }
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal) || path is not null)
                    {
                        Console.Error.WriteLine($"gadfly bias-check: unknown or incomplete option '{args[i]}'.");
                        return 1;
                    }
                    path = args[i];
                    break;
            }
        }
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine("gadfly bias-check: give a JSON file of answers (see 'gadfly bias-check --example').");
            return 1;
        }

        List<ObjectiveGradingInput> inputs;
        try { inputs = Read(await File.ReadAllTextAsync(path)); }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException)
        {
            Console.Error.WriteLine($"gadfly bias-check: cannot read {path}: {e.Message}");
            return 1;
        }
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("gadfly bias-check: the file has no answers to probe.");
            return 1;
        }

        // The estimate, always printed before anything runs.
        using var liveClient = live ? new LegionVotingClient(GadflyConfiguration.Build()) : null;
        var models = liveClient?.ConfiguredModels ?? new MockVotingClient().ConfiguredModels;
        var promptChars = inputs.Sum(i => (long)PanelBiasCheck.PromptCharacters(i, probes));
        var calls = inputs.Count * PanelBiasCheck.CallsPerObjective(probes);
        decimal estimate = 0;
        Console.WriteLine($"Bias check: {inputs.Count} objective(s), probes {string.Join(", ", probes)}, {calls} grading call(s) per panel member.");
        foreach (var (provider, model) in models.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase))
        {
            var input = PanelUsage.Tokens(promptChars);
            var output = (long)calls * 900;
            var (pin, pout) = Prices.GetValueOrDefault(provider, (1m, 4m));
            var usd = live ? (input * pin + output * pout) / 1_000_000m : 0m;
            estimate += usd;
            Console.WriteLine($"  {provider,-9} {model,-28} ~{input:N0} in / ~{output:N0} out tokens  {(live ? $"~${usd:0.00}" : "mock: no cost")}");
        }
        Console.WriteLine(live ? $"Estimated provider cost: ${estimate:0.00} (rough: fallback prices, character-based token counts)." : "Mock panel: no provider is called and nothing is spent.");
        if (live && (approved is null || approved < Math.Round(estimate, 2)))
        {
            Console.Error.WriteLine($"gadfly bias-check: a live run needs the owner's approval of the spend. Re-run with --approve-spend {Math.Ceiling(estimate * 100) / 100:0.00} (or more) to start it.");
            return 2;
        }

        ISocraticVotingClient panel = liveClient is not null ? liveClient : new MockVotingClient();
        var check = new PanelBiasCheck(panel);
        var anyBias = false;
        foreach (var input in inputs)
        {
            var report = await check.RunAsync(input, tolerance, probes: probes);
            anyBias |= report.AnyBias;
            Console.WriteLine();
            Console.WriteLine($"{input.Objective.Title}: baseline {report.BaselineScore:0.##} of {report.MaxScore:0.##}{(report.IsMock ? " (MOCK: not an AI evaluation)" : "")}");
            foreach (var p in report.Probes)
                Console.WriteLine($"  {(p.Biased ? "BIAS " : "ok   ")} {p.Probe,-20} {p.ProbeScore,6:0.##}  ({p.Shift:+0.##;-0.##;0})  {p.Finding}");
        }
        Console.WriteLine();
        Console.WriteLine(anyBias ? "At least one probe moved the grade past the tolerance on identical content." : "No probe moved the grade past the tolerance.");
        return anyBias ? 3 : 0;
    }

    private static List<ObjectiveGradingInput> Read(string json)
    {
        var trimmed = json.TrimStart();
        var items = trimmed.StartsWith('[') ? JsonSerializer.Deserialize<List<FileItem>>(json, Json) ?? [] : [JsonSerializer.Deserialize<FileItem>(json, Json)!];
        var inputs = new List<ObjectiveGradingInput>();
        foreach (var (item, n) in items.Select((x, n) => (x, n)))
        {
            if (item?.Objective?.Title is not { Length: > 0 } title) throw new ArgumentException($"Item {n + 1} needs objective.title.");
            var answers = (item.Answers ?? []).Where(a => !string.IsNullOrWhiteSpace(a.Answer)).ToList();
            if (answers.Count == 0) throw new ArgumentException($"Item {n + 1} needs at least one answer.");
            var objective = new GradingObjective(item.Objective.Key ?? $"objective-{n + 1}", title, item.Objective.Description, item.Objective.MaxScore is > 0 ? item.Objective.MaxScore.Value : 100m,
                AiObjectiveGrader.DefaultCriteria());
            inputs.Add(new ObjectiveGradingInput(objective, answers.Select((a, i) =>
                new EvidenceItem(Guid.NewGuid(), i + 1, a.Question ?? "", a.Answer!.Trim(), VerificationState.Confirmed)).ToList()));
        }
        return inputs;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly bias-check <answers.json> [--probes padded-answer,fluency-paraphrase,spoken-injection] [--tolerance 10]");
        Console.WriteLine("gadfly bias-check <answers.json> --live --approve-spend <usd>");
        Console.WriteLine("gadfly bias-check --example");
        Console.WriteLine();
        Console.WriteLine("  Grades each objective's answers as they are and again padded with restatement, made less fluent, and with a");
        Console.WriteLine("  spoken instruction to the grader added (same content each time). A grade that moves past the tolerance (percent");
        Console.WriteLine("  of the objective's maximum) is reported as bias. Mock by default: nothing is spent. --live prints the estimated");
        Console.WriteLine("  provider cost first and runs only with --approve-spend of at least that amount. Exit code 3 when bias was found.");
        Console.WriteLine("  The same probes, and a blind human version, run per exam in the app under Grading > Panel calibration > Bias probes.");
    }
}
