using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Tests.Fakes;

/// <summary>
/// A deterministic <see cref="ISocraticVotingClient"/> for engine and server tests (GAD-T104): every call is
/// answered by a handler chosen by what the prompt is (question, guard, judge, claims, contradiction, aporia,
/// text style, concept map, rubric), recognized from the exam prompt set's fixed wording. Defaults give a
/// well-behaved panel: questions are drafted, the guard says SAFE, nothing is aporia, nothing contradicts,
/// and two judges score every criterion <see cref="JudgeScore"/> out of 10. No network, no randomness.
/// </summary>
public sealed class ScriptedPanel : ISocraticVotingClient
{
    public enum Kind { Question, Guard, Judge, Claims, Contradiction, Aporia, TextStyle, ConceptMap, Rubric, Verification, Clarification, Analysis, Other }

    public static Kind Classify(string prompt) =>
        prompt.Contains("Write the next question for this move:", StringComparison.Ordinal)
            || prompt.Contains("You are the examiner in a", StringComparison.Ordinal)
            || prompt.Contains("You are a Socratic tutor in learning mode", StringComparison.Ordinal) ? Kind.Question
        : prompt.Contains("You check exam questions before a student hears them", StringComparison.Ordinal) ? Kind.Guard
        : prompt.Contains("You are one judge on a panel", StringComparison.Ordinal) ? Kind.Judge
        : prompt.Contains("You are one analyst on a panel", StringComparison.Ordinal) ? Kind.Analysis
        : prompt.Contains("Extract the factual or conceptual claims", StringComparison.Ordinal) ? Kind.Claims
        : prompt.Contains("A student made both of these claims", StringComparison.Ordinal) ? Kind.Contradiction
        : prompt.Contains("APORIA if the student's answer", StringComparison.Ordinal) ? Kind.Aporia
        : prompt.Contains("Estimate how likely it is that this spoken-exam answer", StringComparison.Ordinal) ? Kind.TextStyle
        : prompt.Contains("identify the concepts a student must understand", StringComparison.Ordinal) ? Kind.ConceptMap
        : prompt.Contains("Write the grading rubric for one concept", StringComparison.Ordinal) ? Kind.Rubric
        : prompt.Contains("You check whether the examiner understood a student's answer", StringComparison.Ordinal) ? Kind.Verification
        : prompt.Contains("You write one neutral clarification question", StringComparison.Ordinal) ? Kind.Clarification
        : Kind.Other;

    /// <summary>Every call, in order: what kind of prompt it was and its full text.</summary>
    public List<(Kind Kind, string Prompt)> Calls { get; } = [];

    public int Count(Kind kind) => Calls.Count(c => c.Kind == kind);

    /// <summary>Each criterion's score (0-10) from every default judge; a function of the answer text when set.</summary>
    public double JudgeScore { get; set; } = 8;
    public Func<string, double>? JudgeScoreFor { get; set; }

    /// <summary>Providers answering FanOut (judge, text style, concept map, rubric) by default.</summary>
    public string[] Providers { get; set; } = ["alpha", "beta"];

    /// <summary>Question drafting: the prompt in, the consensus question out.</summary>
    public Func<string, string> QuestionText { get; set; } = _ => "In your own words, how does this concept work?";

    /// <summary>A drafted neutral clarification question (empty: no usable draft).</summary>
    public Func<string, string> ClarificationText { get; set; } = _ => "";

    /// <summary>The guard's verdict for a candidate question (LEAKS / SAFE).</summary>
    public Func<string, string> GuardVerdict { get; set; } = _ => "SAFE";

    public Func<string, string> AporiaVerdict { get; set; } = _ => "ANSWER";
    public Func<string, string> ContradictionVerdict { get; set; } = _ => "CONSISTENT";

    /// <summary>Claims JSON a voter returns for a claims prompt.</summary>
    public Func<string, string> ClaimsJson { get; set; } = _ => "[]";

    /// <summary>When set, each voter's own claims JSON (exercises the majority merge) instead of one consensus.</summary>
    public Func<string, string[]>? ClaimsPerVoter { get; set; }

    /// <summary>When set, the question panel returns these individual drafts and no consensus (the draft ballot path).</summary>
    public Func<string, string[]>? QuestionDrafts { get; set; }

    /// <summary>The choice for any other DecideAsync ballot (e.g. picking among question drafts); default the first option.</summary>
    public Func<IReadOnlyList<string>, string>? BallotChoice { get; set; }

    /// <summary>Raw FanOut replies; overrides the default judge for any kind it returns non-null for.</summary>
    public Func<Kind, string, IReadOnlyList<PanelReply>?>? FanOut { get; set; }

    /// <summary>Simulates a panel that is down: every call throws.</summary>
    public bool Fail { get; set; }

    private void Record(Kind kind, string prompt)
    {
        lock (Calls) Calls.Add((kind, prompt));
        // Metered like the real panel, so usage tests see calls inside PanelUsage.Track scopes.
        PanelUsage.Record(new PanelCall("scripted", "scripted-model", PanelUsage.Tokens(prompt.Length), 50, kind.ToString()));
        if (Fail) throw new HttpRequestException("The panel is unavailable (test).");
    }

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
    {
        var kind = Classify(question);
        Record(kind, question);
        if (kind == Kind.Claims && ClaimsPerVoter?.Invoke(question) is { } perVoter)
        {
            var votes = perVoter.Select((json, i) => new VoteResult { VoterName = $"v{i}", ProviderId = $"p{i}", Decision = json }).ToList();
            return Task.FromResult(new VotingResult { Question = question, QuorumType = quorum, QuorumReached = true, IndividualVotes = votes });
        }
        if (kind == Kind.Question && QuestionDrafts?.Invoke(question) is { } drafts)
        {
            var votes = drafts.Select((d, i) => new VoteResult { VoterName = $"v{i}", ProviderId = $"p{i}", Decision = d }).ToList();
            return Task.FromResult(new VotingResult { Question = question, QuorumType = quorum, QuorumReached = false, Consensus = "", IndividualVotes = votes });
        }
        var consensus = kind switch
        {
            Kind.Question => QuestionText(question),
            Kind.Claims => ClaimsJson(question),
            Kind.Clarification => ClarificationText(question),
            _ => "",
        };
        return Task.FromResult(new VotingResult { Question = question, QuorumType = quorum, QuorumReached = true, Consensus = consensus });
    }

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
    {
        Record(Classify(question), question);
        var votes = voters.Select(v => new VoteResult { VoterName = v.Name, ProviderId = v.ProviderId, Decision = "" }).ToList();
        return Task.FromResult(new VotingResult { Question = question, QuorumReached = true, IndividualVotes = votes });
    }

    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default)
    {
        var kind = Classify(question);
        Record(kind, question);
        var choice = kind switch
        {
            Kind.Guard => GuardVerdict(question),
            Kind.Aporia => AporiaVerdict(question),
            Kind.Contradiction => ContradictionVerdict(question),
            _ => BallotChoice?.Invoke(options) ?? options.FirstOrDefault() ?? "",
        };
        return Task.FromResult(new DecisionResult { Question = question, Options = options.ToList(), Choice = choice, QuorumReached = choice.Length > 0 });
    }

    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default)
    {
        var kind = Classify(userMessage);
        Record(kind, userMessage);
        if (FanOut?.Invoke(kind, userMessage) is { } custom) return Task.FromResult(custom);
        IReadOnlyList<PanelReply> replies = kind switch
        {
            Kind.Judge => Providers.Select(p => new PanelReply(p, JudgeJson(JudgeScoreFor?.Invoke(userMessage) ?? JudgeScore), Model: p + "-model")).ToList(),
            Kind.TextStyle => Providers.Select(p => new PanelReply(p, """{"score": 0.1, "reason": "Sounds spoken."}""")).ToList(),
            Kind.Verification => Providers.Select(p => new PanelReply(p,
                """{"clarify": false, "trigger": "none", "phrase": "", "material": false, "reason": "The answer is clear."}""")).ToList(),
            _ => Providers.Select(p => new PanelReply(p, "{}")).ToList(),
        };
        return Task.FromResult(replies);
    }

    /// <summary>The text after <paramref name="marker"/> in a prompt (e.g. "Candidate question:"), or "" when absent.</summary>
    public static string After(string prompt, string marker)
    {
        var i = prompt.LastIndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? "" : prompt[(i + marker.Length)..];
    }

    /// <summary>The student's answer inside a judge prompt (the fenced block after "Student's answer:").</summary>
    public static string StudentAnswer(string judgePrompt)
    {
        var tail = After(judgePrompt, "Student's answer:");
        var open = tail.IndexOf("<student_text>", StringComparison.Ordinal);
        var close = tail.IndexOf("</student_text>", StringComparison.Ordinal);
        return open < 0 || close < open ? tail.Trim() : tail[(open + "<student_text>".Length)..close].Trim();
    }

    /// <summary>A judge reply scoring all four criteria <paramref name="score"/> (0-10), citing passage 1.</summary>
    public static string JudgeJson(double score, string verdict = "Shows the idea.") =>
        "{\"criteria\": ["
        + string.Join(", ", new[] { "accuracy", "reasoning", "consistency", "completeness" }.Select(c =>
            $"{{\"criterion\": \"{c}\", \"score\": {score.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"rationale\": \"{c} ok\", \"passages\": [1]}}"))
        + $"], \"verdict\": \"{verdict}\"}}";
}
