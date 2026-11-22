using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using MindAttic.Legion;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// The next question is spoken while it is written (GAD-T495): the live model's reply is split into sentences, each part is guarded
/// with everything before it and the dialogue, and only then said. A part that leaks is never said.
/// </summary>
[TestFixture]
public class QuestionStreamingTests
{
    /// <summary>Records what the voice was given.</summary>
    private sealed class RecordingSpeech : IQuestionSpeech
    {
        public List<string> Said { get; } = [];
        public string? Finished { get; private set; }
        public string? Abandoned { get; private set; }
        public void Say(string part, MoveKind move) => Said.Add(part);
        public void Finish(string question) => Finished = question;
        public void Abandon(string reason) => Abandoned = reason;
    }

    /// <summary>A live model that streams its reply a word at a time; the guard prompt gets <see cref="Guard"/>.</summary>
    private sealed class StreamingModel : ILiveTurnModel
    {
        public Func<string, string> Question { get; init; } = _ => "You said plants store energy. Where does that energy come from first?";
        public Func<string, string> Guard { get; init; } = _ => "SAFE";
        public List<string> GuardPrompts { get; } = [];
        public List<string> QuestionPrompts { get; } = [];
        public string ProviderId => "claude";
        public string Model => "claude-sonnet-5";

        public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var guard = userMessage.Contains("LEAKS or SAFE", StringComparison.Ordinal);
            lock (this) (guard ? GuardPrompts : QuestionPrompts).Add(userMessage);
            var reply = guard ? Guard(userMessage) : Question(userMessage);
            foreach (var piece in reply.Split(' ').Select((w, i) => i == 0 ? w : " " + w))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return piece;
            }
        }
    }

    private static QuestionRequest Request(string rubric = "- Light becomes chemical energy") => new()
    {
        Session = Session(),
        Decision = new PlanDecision { ConceptId = "photosynthesis", Move = MoveKind.Probe, TargetClaimIds = [] },
        ConceptTitle = "Photosynthesis",
        Grounding = new GroundingContext("[1] Photosynthesis makes glucose.", []),
        Ledger = new ClaimLedger(),
        RubricForGuard = rubric,
    };

    private static async Task<string> GenerateSpokenAsync(QuestionGenerator gen, IQuestionSpeech speech, QuestionRequest? request = null)
    {
        using (QuestionSpeech.Begin(speech)) return await gen.GenerateAsync(request ?? Request());
    }

    [Test]
    public async Task EachSentenceIsSaidOnceGuarded_AndTheQuestionIsExactlyWhatWasSaid()
    {
        var model = new StreamingModel();
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };

        var q = await GenerateSpokenAsync(gen, speech);

        Assert.That(speech.Said, Is.EqualTo(new[] { "You said plants store energy.", "Where does that energy come from first?" }));
        Assert.That(q, Is.EqualTo(string.Join(" ", speech.Said)));
        Assert.That(speech.Finished, Is.EqualTo(q));
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.Safe));
        Assert.That(gen.LastAuthor!.How, Is.EqualTo(QuestionAuthorship.LiveModelDraft));
        // One guard per part, each on everything heard so far: the last one is the whole question, as before streaming.
        Assert.That(model.GuardPrompts, Has.Count.EqualTo(2));
        Assert.That(model.GuardPrompts.Any(p => p.Contains("You said plants store energy. Where does that energy come from first?", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task ALeakingFirstPart_IsNeverSaid_AndTheQuestionIsRedrafted()
    {
        var drafts = 0;
        var model = new StreamingModel
        {
            Question = _ => ++drafts == 1 ? "Chlorophyll turns light into chemical energy, right? Why?" : "What happens to the light a leaf absorbs?",
            Guard = p => p.Contains("Chlorophyll", StringComparison.Ordinal) ? "LEAKS" : "SAFE",
        };
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };

        var q = await GenerateSpokenAsync(gen, speech);

        Assert.That(speech.Said.Any(s => s.Contains("Chlorophyll", StringComparison.Ordinal)), Is.False, "a leak is never handed to the voice");
        Assert.That(q, Is.EqualTo("What happens to the light a leaf absorbs?"));
        Assert.That(speech.Said, Is.EqualTo(new[] { q }));
        Assert.That(model.QuestionPrompts[1], Does.Contain("revealed expected answer points"));
    }

    [Test]
    public async Task ALaterPartThatLeaks_StopsThere_AndThePlainFallbackFollowsWhatWasSaid()
    {
        var model = new StreamingModel
        {
            Question = _ => "You said plants store energy. Isn't it light becoming chemical energy in the chloroplast? Explain.",
            Guard = p => p.Contains("chloroplast", StringComparison.Ordinal) ? "LEAKS" : "SAFE",
        };
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };

        var q = await GenerateSpokenAsync(gen, speech);

        Assert.That(speech.Said[0], Is.EqualTo("You said plants store energy."));
        Assert.That(speech.Said.Any(s => s.Contains("chloroplast", StringComparison.Ordinal)), Is.False, "the student never hears the leak");
        Assert.That(speech.Said, Has.Count.EqualTo(2));
        Assert.That(speech.Said[1], Is.EqualTo(QuestionGenerator.Fallback(MoveKind.Probe, "Photosynthesis", null)));
        Assert.That(q, Is.EqualTo(string.Join(" ", speech.Said)), "the question on record is exactly what she said");
        Assert.That(speech.Finished, Is.EqualTo(q));
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.LeakedReplaced));
        Assert.That(gen.LastOutcome, Does.Contain("fallback"));
    }

    [Test]
    public async Task AnUndecidedGuard_FailsClosed_PartByPart()
    {
        var model = new StreamingModel { Guard = p => p.Contains("Where does", StringComparison.Ordinal) ? "Hard to say." : "SAFE" };
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };

        await GenerateSpokenAsync(gen, speech);

        Assert.That(speech.Said.Any(s => s.Contains("Where does", StringComparison.Ordinal)), Is.False);
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.UndecidedReplaced));
    }

    [Test]
    public async Task AShortOpeningSentence_WaitsForTheNext_AndAnIntrospectiveOneIsNeverSaid()
    {
        var model = new StreamingModel { Question = _ => "Okay. What did you mean by stored energy? How did you arrive at that?" };
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };

        var q = await GenerateSpokenAsync(gen, speech);

        Assert.That(speech.Said[0], Is.EqualTo("Okay. What did you mean by stored energy?"), "the first part has at least four words");
        Assert.That(speech.Said.Any(QuestionGenerator.IsIntrospective), Is.False);
        Assert.That(q, Is.EqualTo(string.Join(" ", speech.Said)));
    }

    [Test]
    public async Task WithoutAVoice_TheQuestionComesWhole_AsBefore()
    {
        var model = new StreamingModel();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = model };
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo("You said plants store energy. Where does that energy come from first?"));
        Assert.That(model.GuardPrompts, Has.Count.EqualTo(1), "one guard on the whole question");
    }

    [Test]
    public async Task TheMockModel_StreamsInMockMode()
    {
        var speech = new RecordingSpeech();
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = new MockLiveTurnModel() };
        var q = await GenerateSpokenAsync(gen, speech);
        Assert.That(speech.Said, Is.Not.Empty);
        Assert.That(q, Is.EqualTo(string.Join(" ", speech.Said)));
        Assert.That(speech.Finished, Is.EqualTo(q));
    }

    [Test]
    public void TheReplyIsCleanedAsItStreams()
    {
        var reply = new QuestionGenerator.ReplyCleaner();
        var said = new List<string>();
        foreach (var piece in new[] { "Quest", "ion: \"You said", " energy is stored. ", "Where is it", " stored?\"\nNote: I avoided", " the rubric." })
            said.AddRange(reply.Add(piece));
        said.AddRange(reply.Finish());
        Assert.That(said, Is.EqualTo(new[] { "You said energy is stored.", "Where is it stored?" }));
        Assert.That(reply.LineEnded, Is.True);
    }

    [Test]
    public void SentencesAreSplitAsTheyArrive_NotOnAbbreviationsOrNumbers()
    {
        var splitter = new SentenceSplitter();
        var done = new List<string>();
        foreach (var piece in new[] { "At 3.", "5 volts, e.g. in a", " cell, what happens? Then", " what! And Dr. Smith's view" })
            done.AddRange(splitter.Add(piece));
        Assert.That(done, Is.EqualTo(new[] { "At 3.5 volts, e.g. in a cell, what happens?", "Then what!" }));
        Assert.That(splitter.Flush(), Is.EqualTo("And Dr. Smith's view"));
    }

    [Test]
    public async Task TheClaudeModel_StreamsTextDeltas_FromTheMessagesApi()
    {
        var sse = new StringBuilder()
            .Append("event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":120,\"output_tokens\":1}}}\n\n")
            .Append("event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Why does \"}}\n\n")
            .Append("event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"it hold?\"}}\n\n")
            .Append("event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":6}}\n\n")
            .Append("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n").ToString();
        var handler = new SseHandler(sse);
        using var legion = new LegionVotingClient(new VotingConfiguration());
        var model = new ClaudeStreamingLiveTurnModel(legion, "claude-sonnet-5", () => "test-key", handler);

        var deltas = new List<string>();
        IReadOnlyList<PanelCall> calls;
        using (PanelUsage.Track(out calls))
            await foreach (var d in model.StreamAsync("system", "Ask one question.")) deltas.Add(d);

        Assert.That(deltas, Is.EqualTo(new[] { "Why does ", "it hold?" }));
        Assert.That(handler.Body, Does.Contain("\"stream\":true"));
        Assert.That(handler.Body, Does.Contain("\"thinking\":{\"type\":\"disabled\"}"), "Sonnet 5 runs adaptive thinking unless told not to");
        Assert.That(handler.Body, Does.Not.Contain("temperature"), "the Claude 5 line rejects temperature");
        Assert.That(handler.Headers, Does.Contain("x-api-key: test-key"));
        Assert.That(calls.Single(), Is.EqualTo(new PanelCall("claude", "claude-sonnet-5", 120, 6, "live-turn", Estimated: false)));
    }

    [Test]
    public void TheClaudeModel_TurnsThinkingOffTheWayEachModelAccepts()
    {
        Assert.That(ClaudeStreamingLiveTurnModel.ThinkingOff("claude-sonnet-5"), Is.EqualTo("disabled"));
        Assert.That(ClaudeStreamingLiveTurnModel.ThinkingOff("claude-sonnet-5-5"), Is.EqualTo("between_tools"));
        Assert.That(ClaudeStreamingLiveTurnModel.ThinkingOff("claude-haiku-5-20260101"), Is.EqualTo("disabled"));
        Assert.That(ClaudeStreamingLiveTurnModel.ThinkingOff("claude-sonnet-4-6"), Is.Null);
        Assert.That(ClaudeStreamingLiveTurnModel.ThinkingOff("claude-opus-4-8"), Is.Null);
    }

    [Test]
    public void TheClaudeModel_FailsLoudly_SoThePanelTakesOver()
    {
        using var legion = new LegionVotingClient(new VotingConfiguration());
        var model = new ClaudeStreamingLiveTurnModel(legion, "claude-sonnet-5", () => "test-key", new SseHandler("", HttpStatusCode.TooManyRequests));
        Assert.ThrowsAsync<HttpRequestException>(async () => await model.CompleteAsync("system", "Ask."));
        var noKey = new ClaudeStreamingLiveTurnModel(legion, "claude-sonnet-5", () => null, new SseHandler(""));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await noKey.CompleteAsync("system", "Ask."));
    }

    private sealed class SseHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public string Headers { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Headers = string.Join("\n", request.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
}
