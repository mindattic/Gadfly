// Gadfly in-process sample: run a scripted Socratic session over a markdown source against the MOCK panel.
// No keys, no network, no cost. Replace MockVotingClient with Gadfly.Socrates.LegionVotingClient (and a
// GadflyConfiguration) to use the real multi-LLM panel.
using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;

ISocraticVotingClient panel = new MockVotingClient();

// 1. Read the source material into a throwaway store (a temp folder, deleted at the end).
var work = Path.Combine(Path.GetTempPath(), "gadfly-sample-" + Guid.NewGuid().ToString("N")[..8]);
try
{
    var store = new FileSourceStore(work);
    var source = Path.Combine(AppContext.BaseDirectory, "multiplication.md");
    var ingest = await new SourceIngestor(store, [new PlainTextExtractor(), new TutorIngestionExtractor()])
        .IngestAsync(source, "multiplication", "Understanding multiplication");
    var passages = await store.GetPassagesAsync("multiplication");
    Console.WriteLine($"Read {ingest.DocumentsIngested} document(s) into {passages.Count} passages.");

    // 2. The panel finds the concepts and writes a rubric for each.
    var retriever = new GroundingRetriever(passages);
    var map = await new ConceptMapBuilder(panel).BuildAsync("multiplication", passages);
    var rubrics = await new RubricBuilder(panel).BuildAsync(map, retriever);
    Console.WriteLine($"Concept map: {string.Join(", ", map.Concepts.Select(c => c.Title))}");

    // 3. The subject: the ISubjectMatter the engine examines. Implement the interface yourself to examine
    //    something other than documents; SourceMaterialSubject is the ready-made one for source material.
    var subject = new SourceMaterialSubject("Understanding multiplication", map, rubrics, retriever);
    ISubjectMatter asInterface = subject;
    Console.WriteLine($"Subject: {asInterface.Title} ({asInterface.Id}), prompt set {asInterface.PromptSet.Id} v{asInterface.PromptSet.Version}");

    // 4. Run the session with canned student answers.
    var policy = new SessionPolicy
    {
        ConceptIds = map.Concepts.Take(2).Select(c => c.Key).ToList(),
        MaxTurns = 6,
        TimeBudget = TimeSpan.FromMinutes(20),
    };
    var student = new ScriptedStudent(map);
    Console.WriteLine();
    var (session, report) = await new SourceExamRunner(panel, subject).RunAsync(policy, student);

    // 5. The report.
    Console.WriteLine();
    Console.WriteLine($"Session ended ({session.EndReason}) after {session.Turns.Count} turn(s). Recommendation: {report.Recommendation}, overall {report.OverallScore:P0}.");
    foreach (var c in report.Concepts.Where(c => c.Turns > 0))
        Console.WriteLine($"  {c.Title,-40} {c.Mastery:P0}  {c.Status}");
    Console.WriteLine();
    Console.WriteLine("--- report (markdown, first 25 lines) ---");
    Console.WriteLine(string.Join(Environment.NewLine, report.ToMarkdown().Split('\n').Take(25)));
}
finally
{
    try { Directory.Delete(work, recursive: true); } catch (IOException) { }
}

/// <summary>The student side: prints each question and gives the next canned answer.</summary>
sealed class ScriptedStudent(ConceptMap map) : IExamConversation
{
    private static readonly string[] Answers =
    [
        "Multiplication means adding equal groups. Three bags with four apples in each bag is four plus four plus four, which is twelve, because every group has the same size.",
        "I think it only works when the groups are equal, because repeated addition needs the same number each time. With unequal groups you have to add them one by one.",
        "The three is the number of groups and the four is how many are in each group, so they stand for different things even though three times four and four times three are both twelve.",
        "If I double the number of bags the total doubles too, because there are twice as many equal groups.",
        "I don't know.",
        "Scaling means changing one factor changes the product in the same proportion.",
    ];
    private int _next;

    public Task AskAsync(Turn turn, CancellationToken ct)
    {
        var concept = map.Find(turn.ConceptId)?.Title ?? turn.ConceptId;
        Console.WriteLine($"[{concept} / {turn.Move}] Examiner: {turn.Question}");
        return Task.CompletedTask;
    }

    public Task<Answer?> ListenAsync(Turn turn, CancellationToken ct)
    {
        var text = Answers[_next++ % Answers.Length];
        Console.WriteLine($"  Student: {text}");
        return Task.FromResult<Answer?>(new Answer { Text = text, CompletedAt = DateTimeOffset.UtcNow });
    }

    public Task OnJudgedAsync(Turn turn, CancellationToken ct)
    {
        var j = turn.Judgment;
        if (j is not null) Console.WriteLine($"  Judgment: {j.OverallScore:0.00} (agreement {j.Agreement:0.00}) {j.Summary}");
        Console.WriteLine();
        return Task.CompletedTask;
    }
}
