# Research note: realtime speech-to-speech models for the examiner's turn-taking

Prepared 2026-11-22 for Gadfly (GAD-T500). The question: could a realtime speech-to-speech model (OpenAI Realtime or
GPT-Live, Google Gemini Live, ElevenLabs' agent products) run the conversational turn-taking so the examiner starts
replying within about 300-800 ms, while Gadfly's multi-model panel judges answers in parallel or afterwards? This is a
desk review of vendor documentation, pricing pages and published benchmarks, checked against Gadfly's docs
(`docs/VOICE.md`, `docs/research/stt-alternatives.md`, `docs/research/blueprint-gap-analysis.md`). No paid API was
called and no code was changed. Vendor latency figures are vendor claims unless marked otherwise.

## Recommendation

**Do not give the examiner's turn-taking to a speech-to-speech model.** None of the options fixes what makes the wait
long, and each one would break something the blueprint requires.

1. **The wait comes from the panel, not from speech.** The next question can only be written after the panel has
   judged the answer (M1, M3; `docs/VOICE.md`, "Prepared ahead"). A speech-to-speech model can get *some* sound out
   within about 0.3-0.8 s, but it cannot get *the next approved question* out any sooner. During the gap it would
   either say nothing useful or make up its own words. Making up its own words is the integrity risk: answer leaks,
   praise or hints that give away the grade (D10), and questions not grounded in the instructor's material.
2. **None of them reliably speaks our exact text.** GPT-Live "is trained to paraphrase the text" it is given. The
   older OpenAI Realtime models and Gemini Live can only be *asked* to say something word for word, in the prompt,
   which is not a guarantee. ElevenLabs' own speech-to-speech models (Eleven Realtime) cannot use our own language
   model at all.
3. **They break the voice, lip-sync and grading features.** OpenAI and Gemini speak only in their own built-in voices
   (OpenAI offers custom voices only through sales), so our ElevenLabs examiner voice is lost. Their audio comes
   without the per-character timings `<ma-avatar>` needs for lip-sync. Their transcripts have no per-word confidence
   and no speaker labels, and Gadfly's unsure-word flags (A15, B8) and second-voice note depend on both.
4. **They listen to tone of voice.** A native audio model hears the student's prosody and can change how it replies
   because of it. That contradicts B19 and M15 ("voice never steers the examiner") and moves toward the EU AI Act's
   ban on emotion recognition in education. Gemini 3.8 Live's "proactive audio" is always on and lets the model decide
   not to reply at all.

**What to do instead, in order:**

- **(a) Close the silence with what is already built.** After the end-of-answer cue, play the first neutral wait-filler
  from the phrase cache within about 0.5 s, instead of waiting 2 s as `ExaminerFiller` does today. That gives a
  300-800 ms first response with the examiner's own voice and lip-sync, and with a fixed line that gives nothing away
  (D10). This is a small change to a timing setting; it would need its own ticket.
- **(b) Measure before buying anything.** Use the GAD-T413 timings (`/healthz/call-timings`) to split the wait into
  recognizer commit-to-final, panel judging, question writing and time to first TTS audio. Then work on whichever part
  is largest: speculative question preparation and prefetch (MAA-T14), faster panel members, or a quicker recognizer
  (SR-T12).
- **(c) If a managed turn-taking layer is still wanted later, the one that fits is ElevenLabs Speech Engine.** It is a
  cascade, not a speech-to-speech model: ElevenLabs transcribes the student, handles turn-taking and barge-in, and
  speaks *our server's* text with our ElevenLabs voice. Our server stays the only source of what the examiner says.
  Spike it behind a setting only after (b), and only if it can be shown to keep per-word confidence, speaker labels,
  the timing data for the avatar, and Gadfly's adaptive end-of-answer silence (B5, E3). EU residency for it is
  Enterprise-only.

## Options compared

| | Current stack (Scribe + panel + Flash TTS) | ElevenLabs Speech Engine (cascade, our server is the "brain") | ElevenLabs Eleven Realtime v1 Mini / Max (speech-to-speech) | OpenAI GPT-Live-1 (voice layer + delegation) | OpenAI Realtime `gpt-realtime-2` / `2.1` (speech-to-speech) | Google Gemini 3.8 Live (native audio) |
|---|---|---|---|---|---|---|
| **Time to first audio** | Speech starts on the first TTS chunk; first filler at 2 s (could be ~0.5 s from cache); question after the panel finishes | Not published; LLM time is ours, so the question still waits for the panel | Vendor-reported via Artificial Analysis: Max 0.57 s, Mini 0.62 s average | Not published. Vendor: Full Duplex Bench 30 points better than Realtime 2.1 | Third-party: Cekura mean 1.58 s; a one-person May 2026 blog reports p50 232 ms (older `gpt-4o-realtime`) | Third-party: Cekura mean 3.05 s; blog reports p50 250 ms (older 2.0 Flash Live) |
| **Connection** | Browser → our SignalR hubs → Scribe WebSocket / ElevenLabs HTTP stream | Browser SDK to ElevenLabs; ElevenLabs calls *our* WebSocket server per conversation | WebSocket, OpenAI-Realtime-compatible (`/v1/convai/realtime`) or Agents SDK | WebRTC (recommended, browser) or WebSocket; server attaches by sideband WebSocket | WebRTC, WebSocket or SIP | WebSocket (WSS); browser with ephemeral token, or server-to-server |
| **Our ElevenLabs voice id** | Yes | Yes (ElevenLabs TTS) | ElevenLabs library voices; cloned voices not confirmed | No: 12 built-in voices; custom voices through sales | No: 10 built-in voices | No: built-in voices only |
| **Avatar timing data** | Yes: character alignment → word timings | Not documented for Speech Engine; Agents WebSocket `audio` events carry `chars` / `char_start_times_ms` (not sent over WebRTC) | Not documented | Transcript deltas with `start_ms`/`end_ms`; audio is on the WebRTC track (copy on sideband) | Audio deltas plus a transcript; no character timings | 24 kHz PCM plus output transcription; no timings |
| **Speaks only our approved text** | Yes | Yes: it speaks the `agent_response` text our server sends (verbatim not stated, but it is plain TTS of our text) | No: a custom LLM can't be used in Realtime mode | No: "trained to paraphrase" commentary; exact wording only through prompts or app-controlled playback | Only by asking: `response.create` with "Say exactly the following:" (prompt-level, not guaranteed) | Only by asking in the system prompt; proactive audio always on |
| **Barge-in** | Hold-to-talk interrupts; hands-free doesn't (echo) | Managed; stale `event_id` responses dropped | Managed | Full duplex | Server VAD / `semantic_vad`; auto-truncation over WebRTC | Automatic VAD, `interrupted` flag; tunable or manual |
| **Transcripts for grading** | Word timings, per-word confidence, speaker labels | Full history as text per turn; no confidence field | Managed ASR; not documented | Input/output transcript deltas; no confidence documented | Input transcription, output transcript | Input/output transcription, custom vocabulary |
| **EU residency** | Per GAD-T259 rules (voice measurements off) | ElevenLabs EU residency: Enterprise only | Mini and Max available under EU residency; zero retention: Max only | Yes: `/v1/live/sessions` in Europe (EEA + CH), zero-retention eligible, +10% | Yes: `/v1/realtime` in Europe, +10%; tracing not EU-compliant. Azure: EU regions Global Standard only, no EU Data Zone | Not shown for 3.8 Live; Vertex EU regions listed for 2.5 native audio, which retires 13 Dec 2026 |
| **Indicative cost** (assumptions below) | ~$0.011 per exam minute (speech only) | $0.08 per conversation minute + our own LLM | $0.08/min + Realtime-model tokens | $0.05 per active minute (+10% EU) + our backend | ~$0.03-0.05 per exam minute; third-party estimate $0.43/min for continuous two-way talk | ~$0.01-0.02 per exam minute ($0.005/min audio in, $0.018/min out) |
| **Fit with the blueprint** | Fits (M1, M3, D10, A15, B19) | Fits if the transcript-quality gaps are closed | Fails M1/M3 (its own LLM writes the words) | Fails M3/D10 (paraphrase, automatic spoken acknowledgements) | Fails voice and avatar, weak on M3 | Fails voice, M3, B19 (proactive audio) |

**Cost assumptions.** A 20-minute exam: the microphone open about 12 minutes, the examiner speaking about 5 minutes
(about 900 characters a minute). Panel LLM cost is left out because every option keeps it.

- *Current stack:* Scribe realtime costs $0.22 an hour, so 12 minutes is about $0.044. Flash TTS costs $0.04 per
  1,000 characters, so about 4,500 characters is about $0.18. Total about $0.22, or $0.011 per minute.
- *OpenAI Realtime:* audio input is 10 tokens a second at $32 per million and output is 20 tokens a second at $64 per
  million. That is about $0.23 for the student's audio, $0.38 for the examiner's, and a few cents for re-reading cached
  context, so about $0.65-1.00 per exam.
- *Gemini:* per-minute audio rates from Google's pricing page; the context it re-reads is extra.
- *Per-minute products:* GPT-Live and ElevenLabs bill the whole connected session, silence included.

Even the cheapest per-minute layer costs several times the current speech spend per minute, and it saves nothing on
the panel.

## Details

### Why the 300-800 ms goal can't be met for the next question

Gadfly's examiner is deliberately not a chat partner. Each follow-up is chosen by the deterministic planner from the
panel's judgment of the answer and the claim ledger. It is then drafted, leak-checked (fail-closed) and
presupposition-checked before it is spoken (A3; `QuestionPlanner`, `QuestionGenerator`). The panel runs several model
families with timeouts (`LegionVotingClient`). None of that work can start before the answer is committed. So only two
things can come back within 300-800 ms: a sound that commits to nothing (a filler, a nod, a breath), or a reply the
speech-to-speech model makes up itself. The first already exists as the fixed, cached filler lines (D10, GAD-T290,
GAD-T409). Starting them sooner gets the perceived speed at no cost. The second is what the exam must never do: the
examiner must not leak answers, must not hint at a grade, and must only ask grounded, approved questions.

The vendor designs make this concrete:

- **GPT-Live-1** (September 2026) splits the voice from a backend that does the reasoning. With client delegation our
  server would be the backend and receive `session.delegation.created`. But the model "is trained to paraphrase" what
  we send through `session.commentary.append`. By default it speaks an automatic acknowledgement while the backend
  works, and cancelling that "does not stop audio already generated or prevent independent model speech". OpenAI's own
  guidance for exact wording is to block or govern playback at the client. In other words, you stop the model from
  speaking freely and then bring back a separate path for the real words.
- **Realtime `gpt-realtime-2` / `2.1`** can be told "Say exactly the following:" in an out-of-band `response.create`.
  That is an instruction to a generative model, not a TTS call, so a paraphrase is still possible, and the leak guard
  never sees what was actually said. It can be run with `output_modalities: ["text"]`, which makes it a turn detector
  and recognizer feeding our own TTS. That is a cascade again, priced at speech-to-speech rates, and it adds nothing
  over Scribe plus our TTS.
- **Gemini 3.8 Live** supports only the `AUDIO` response modality. Proactive audio is always on (setting it to false
  returns an error), so the model may choose not to reply. Audio-only sessions are capped at 15 minutes and a
  connection lasts about 10 minutes, unless resumption and context compression are set up.
- **Eleven Realtime v1** (20 October 2026) is a single managed model. "Realtime mode isn't available while the agent's
  LLM is a custom LLM", so our panel could not write its lines.

### The ElevenLabs Speech Engine option in more detail

Speech Engine turns ElevenLabs into the voice layer for a text agent we run. ElevenLabs connects to a WebSocket on our
server and sends `user_transcript` (the whole conversation history plus an `event_id`). We stream back
`agent_response` chunks (`content`, `is_final`, matching `event_id`), and ElevenLabs drops any response whose
`event_id` is out of date. That is how barge-in works. Conversations can run up to 7,200 s (the default is 600 s).
Because our server writes every word, M1, M3 and D10 hold unchanged, and the examiner keeps her ElevenLabs voice.

Gaps that a spike has to close before this could replace anything:

- `user_transcript` is plain text with no per-word confidence or speaker label. Gadfly's unsure-word flags to the panel
  and to reviewers (A15, B8, GAD-T265, GAD-T405) and the second-voice note (GAD-T228) depend on Scribe's per-word
  `logprob` and diarization (`stt-alternatives.md`). Running Scribe alongside would bring those back, but at double
  the recognition cost.
- The managed turn-taking model ("turn eagerness": Eager, Normal, Patient) would replace Gadfly's adaptive
  end-of-answer silence (GAD-T264). The dissertation asks that the silence rule not cut off anxious or second-language
  speakers more often than others (B5, E3), so any change has to be measured by group (GAD-T434).
- Avatar timings: the Agents WebSocket `audio` event carries `alignment` (`chars`, `char_start_times_ms`,
  `char_durations_ms`), but it is not sent over WebRTC, and the Speech Engine docs don't say whether it is sent there.
  `<ma-avatar>` needs those timings for word-timed lip-sync and captions.
- The audio would go from the browser to ElevenLabs directly, not through `/hubs/speech`. That changes the CSP
  (`connect-src`; compare GAD-T493), the pause, resume and reload handling (GAD-T306, GAD-T473, GAD-T474), and the
  staff pause that closes the microphone (GAD-T471).
- EU: ElevenLabs data residency is Enterprise-only and needs agents to be recreated in the isolated EU environment.
  Custom-LLM style integrations can process data outside the region.
- Cost: $0.08 per connected minute, silence included. That is about $1.60 for a 20-minute exam, against about $0.22
  for speech today.

### Latency evidence

There is no independent, like-for-like browser benchmark of these exact products. What exists:

- **Eleven Realtime v1:** Max 0.57 s, Mini 0.62 s average time to start speaking. Vendor-published, attributed to
  Artificial Analysis.
- **Telnyx's comparison article** (modified October 2026) summarizes three benchmarks:
  - Openbenchmarks measured phone calls from the caller's side; ElevenLabs agents had a median of 1,424 ms and a p95
    of 1,768 ms.
  - Tested Media measured production calls; other vendors had medians of 680-720 ms.
  - Cekura v1 reports only means: GPT Realtime 1.58 s, Gemini Live 3.05 s.
  - Telnyx's own caveat: these measure different start and end points and should not be compared directly.
- **A May 2026 DEV Community post** reports p50 232 ms for OpenAI Realtime (`gpt-4o-realtime`) and 250 ms for Gemini
  2.0 Flash Live, against 410-870 ms for cascades. It is one author's test on older models, so treat it as weak
  evidence.
- **GPT-Live-1 and Gemini 3.8 Live:** no millisecond figures from the vendors; only relative claims.

Taken together: a speech-to-speech model can start a *reply* within about 0.25-0.6 s under good conditions, and
1.4-3 s is common end to end. A cascade like ours can start a *fixed* line just as fast from a cache. Neither can start
a panel-judged question sooner than the panel allows.

### Interruption and barge-in

Every option handles barge-in. Today Gadfly deliberately lets the student interrupt only in hold-to-talk mode, because
the examiner's own voice from the speakers would trigger a hands-free interrupt (`docs/VOICE.md`). The vendors solve
that echo with server-side echo handling and semantic turn detection. If interruption is ever wanted in hands-free mode,
the useful piece is a turn detector, not a speech-to-speech model. OpenAI's `semantic_vad` or ElevenLabs' turn-taking
model could be tried as end-of-answer cues, measured against the adaptive silence by group.

### Transcripts and audit

The record grading reads has to be what the student said, with the recognizer's uncertainty kept (M14 and A15 as
amended by the owner's GAD-T405 decision). It also has to be what the examiner actually said, word for word (E18:
versioned prompts, replayable planner). With a speech-to-speech model, the examiner's words are whatever the model
generated, recovered from its output transcript. That transcript would have to be checked after the fact against the
approved question, and a mismatch found afterwards is already a leak. With our own TTS, the approved text *is* the
record.

### Fit with the dissertation blueprint (`blueprint-gap-analysis.md`)

| Row | Requirement | Speech-to-speech examiner | Speech Engine cascade | Recommended path (a)+(b) |
|---|---|---|---|---|
| A1 / M1 | Examiner apart from grader; probing only from judged content | At risk: the model reacts to the answer before the panel has judged it | Holds | Holds |
| A3 / M3 | No question contains or presupposes its answer; leak guard fails closed | Fails: the spoken words bypass the guard | Holds | Holds |
| A15, B6, B8 / M14 | Uncertain recognition flagged, not scored as wrong | Lost (no per-word confidence) | Lost unless Scribe runs alongside | Holds |
| A16, B19 / M15 | Voice never steers the examiner; no emotion inference | Fails: native audio models condition on prosody; Gemini proactive audio | Holds (text-only brain) | Holds |
| B5, E3 | Adaptive end-of-answer silence; measure who gets cut off | Replaced by the vendor's VAD | Replaced by the vendor's turn-taking; must be measured | Holds |
| D10 | Fillers and face never signal the grade | At risk (automatic acknowledgements, free replies) | Holds | Holds, and sooner |
| E16 / EU | EU processing; AI Act | OpenAI: EU residency (+10%); Gemini 3.8: unverified; Azure: no EU Data Zone for realtime | Enterprise-only EU residency | Unchanged |
| E18 | Replayable record of what was asked | Examiner's words are model output | Holds | Holds |

The dissertation also says slow answers and long pauses are normal in a Socratic exam (Chapter 2, think-aloud
section). Its concern is fairness and evidence, not speed. The blueprint accepts technology limits, and the time the
panel takes is one (compare GAD-T440). A faster *first sound* fits the blueprint. A model that speaks before the panel
has judged the answer does not.

## Sources

Checked 2026-11-22.

- OpenAI, "Introducing GPT-Live-1 in the API" (10 Sep 2026): https://openai.com/index/introducing-gpt-live-1-in-the-api/
- OpenAI, Getting started with GPT-Live: https://developers.openai.com/api/docs/guides/live.md
- OpenAI, Delegation and tools in GPT-Live: https://developers.openai.com/api/docs/guides/live-delegation.md
- OpenAI, Voice server controls: https://developers.openai.com/api/docs/guides/voice-server-controls
- OpenAI, Migrate to GPT-Live: https://developers.openai.com/api/docs/guides/live-migration.md
- OpenAI, Realtime conversations: https://developers.openai.com/api/docs/guides/realtime-conversations
- OpenAI, Realtime costs: https://developers.openai.com/api/docs/guides/realtime-costs
- OpenAI, Voice latency and cost: https://developers.openai.com/api/docs/guides/voice-latency-cost
- OpenAI, model pages: https://developers.openai.com/api/docs/models/gpt-realtime-2,
  https://developers.openai.com/api/docs/models/gpt-realtime-2.1,
  https://developers.openai.com/api/docs/models/gpt-realtime-mini
- OpenAI, Data residency: https://developers.openai.com/api/docs/guides/data-residency.md
- Microsoft, Foundry model region availability (updated 19 Oct 2026):
  https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability
- Google, Gemini API pricing: https://aistudio.google.com/docs/pricing
- Google, Live API overview: https://aistudio.google.com/docs/live-api
- Google, Live API capabilities: https://aistudio.google.com/docs/live-api/capabilities.md
- Google, Live API session management: https://aistudio.google.com/docs/live-api/session-management.md
- Google, Gemini 3.8 Live model page: https://aistudio.google.com/docs/models/gemini-3.8-live.md
- Google Cloud, Vertex AI Gemini 2.5 Flash Live API (EU regions, retirement 13 Dec 2026):
  https://docs.cloud.google.com/vertex-ai/generative-ai/docs/models/gemini/2-5-flash-live-api
- ElevenLabs, Speech Engine: https://elevenlabs.io/docs/overview/capabilities/speech-engine
- ElevenLabs, Speech Engine upstream protocol:
  https://elevenlabs.io/docs/eleven-agents/api-reference/speech-engine-upstream/speech-engine-upstream
- ElevenLabs, Agents LLM options: https://elevenlabs.io/docs/agents-platform/customization/llm
- ElevenLabs, Custom LLM: https://elevenlabs.io/docs/eleven-agents/customization/llm/custom-llm
- ElevenLabs, Realtime models: https://elevenlabs.io/docs/eleven-agents/customization/realtime-models
- ElevenLabs, "Introducing Eleven Realtime" (20 Oct 2026): https://elevenlabs.io/blog/realtime
- ElevenLabs, Agents WebSocket API: https://elevenlabs.io/docs/eleven-agents/libraries/web-sockets
- ElevenLabs, Conversation flow (turn eagerness, interruptions):
  https://elevenlabs.io/docs/eleven-agents/customization/conversation-flow
- ElevenLabs, Data residency: https://elevenlabs.io/docs/overview/administration/data-residency
- ElevenLabs, API pricing: https://elevenlabs.io/pricing/api
- ElevenLabs, Agents pricing: https://elevenlabs.io/pricing/agents
- Telnyx, "Voice AI agents compared: latency" (summarizes Openbenchmarks, Tested Media, Cekura):
  https://telnyx.com/resources/voice-ai-agents-compared-latency
- K. Imoto, "I Benchmarked 5 Voice AI Stacks" (DEV Community, May 2026; single-author, older models):
  https://dev.to/kenimo49/i-benchmarked-5-voice-ai-stacks-only-2-stayed-under-300ms-2bka
- Explainx summary of Eleven Realtime v1 (secondary):
  https://explainx.ai/blog/elevenlabs-eleven-realtime-v1-mini-max-speech-to-speech-openai-compatible-api-2026
- Gadfly: `docs/VOICE.md`, `docs/research/stt-alternatives.md`, `docs/research/blueprint-gap-analysis.md`,
  `docs/PRIVACY.md`, `docs/COMPLIANCE.md`; dissertation `chapters/02-literature.md`
