# Research note: faster speech-to-text options already built in the Prose repo

Prepared 2026-11-22 for Gadfly (GAD-T414). The owner asked whether MindAttic's Prose app already has a speech-to-text
integration that would be faster than the ElevenLabs Scribe recognizer Gadfly uses for spoken answers, and whether it
could be offered as another value of the existing provider setting. This note lists every speech-to-text integration
in Prose, what each one is, what Prose's own code says about its speed and cost, and how it compares with what Gadfly
runs today. It is a desk review of code and comments in the two repositories; nothing was called, timed or billed.

How to read the evidence levels:
- **Read in code**: the behaviour was read in the source file named.
- **Claim in a code comment**: Prose's author states it in a comment; no benchmark or measurement backs it in the repo.
- **Not found**: searched for and not present.

## A. What Gadfly uses today

- **Library:** `..\MindAttic.SpeechRecognition` (relay hub `/hubs/speech`, wired in `Gadfly.Server/Voice/VoiceServices.cs`).
- **Setting:** `MindAttic:SpeechRecognition:Provider`. Values the library accepts (`RecognizerFactory.Create` in
  `src/MindAttic.SpeechRecognition.AspNetCore/ServiceCollectionExtensions.cs`): `Simulated`, `ElevenLabs`, `OpenAI`,
  `Fallback` (ElevenLabs, then OpenAI). Gadfly's mock mode always uses `Simulated`; live mode turns an empty or
  `Simulated` value into `ElevenLabs`. `docs/VOICE.md` already documents `ElevenLabs` (default in live), `OpenAI` and
  `Fallback`.
- **ElevenLabs Scribe** (`Providers/ElevenLabs/`, read in code): a streaming WebSocket session. The browser sends
  16 kHz PCM16 chunks as the student speaks, Scribe returns partial transcripts while they speak and a final segment on
  each commit, with word timings and per-word confidence (from `logprob`). Gadfly asks for model `scribe_v3_live`
  (overridable with `Gadfly:Voice:ScribeModel`) because only that model labels speakers, which Gadfly's
  second-voice note depends on (GAD-T228). Because audio streams during the answer, most of the transcription work is
  done by the time the answer ends; the wait the student feels is commit to final segment, not the length of the
  answer.
- **OpenAI Whisper** (`Providers/OpenAI/OpenAiWhisperRecognizer.cs`, read in code): batch. Audio is buffered until a
  commit, then posted to `v1/audio/transcriptions` (`whisper-1`, `verbose_json`, word and segment timestamps). No
  partials, no speaker labels, no per-word confidence (whisper-1 gives none), retries on 429 and 5xx. Its `BaseUri`,
  `Model` and key configuration key are options.
- **Latency numbers in Gadfly:** none for speech-to-text yet. GAD-T413 (`Gadfly.Server/Diagnostics/ApiCallTimings.cs`)
  now records the recognizer's start, commit and stop on live exams into `gadfly.ApiCallTimings`, readable at
  `/healthz/call-timings`. That is where a comparison should come from.

## B. Speech-to-text in Prose

Prose (`D:\Projects\MindAttic\Prose`) does **not** use MindAttic.SpeechRecognition. Its only speech-to-text is
`src/Prose.Core/Services/Discussion/SpeechService.cs`, used for dictating into the Writer's discussion panel
(`src/Prose.WriterUi/Services/SpeechUiService.cs`, `wwwroot/js/writer.js`, `window.proseMic`). One class, two hosted
providers behind one code path.

### B1. Groq-hosted Whisper (`whisper-large-v3-turbo`), Prose's primary
- **Shape (read in code):** batch, one request per utterance. `writer.js` records the whole utterance with
  `MediaRecorder` (webm/opus) into memory and sends it when the author stops; the comment says "An utterance is seconds
  long; a whole dictated chapter would need streaming." The server posts multipart form data to
  `https://api.groq.com/openai/v1/audio/transcriptions` with `response_format=json`, `temperature=0`, `language=en`
  and an optional `prompt` of invented names (capped at 700 characters because Whisper's 224-token prompt is truncated
  silently). Only the text comes back: no word timings, no confidence, no speaker labels.
- **Speed (claim in a code comment):** "Groq's `whisper-large-v3-turbo` is the fastest hosted transcription
  available." No benchmark, timing log or test of it was found in the repo. A local whisper.cpp build was considered
  and rejected because of setup cost and latency ("latency is the thing most likely to make this loop unusable").
- **Cost (claim in a code comment, rates dated 2026-09):** $0.04 per hour of audio, billed on audio duration.
- **Licence:** a paid hosted API under Groq's terms; the model weights (Whisper large-v3-turbo) are OpenAI's
  open-weights release, but Prose uses only the hosted endpoint. Key from MindAttic Vault (`groq` credential).
- **Fallback:** if the Groq call fails and an OpenAI key exists, Prose retries the same audio on OpenAI.

### B2. OpenAI Whisper (`whisper-1`), Prose's fallback
- **Shape (read in code):** identical request to `https://api.openai.com/v1/audio/transcriptions`; batch, text only.
- **Speed:** no claim; used only when Groq is missing or fails.
- **Cost (claim in a code comment, 2026-09):** $0.006 per minute ($0.36 per hour), about nine times Groq's rate.
- **Licence:** paid hosted API under OpenAI's terms.

### B3. Not speech-to-text
ElevenLabs (`ElevenLabsTtsService.cs`), Piper and the browser's speech synthesis in Prose are **text-to-speech** for
read-aloud and audiobook export. `VoiceFingerprintAnalyzer` is a prose-style analysis of written text, not audio. None
bears on this question. **Not found** in Prose: Deepgram, AssemblyAI, Vosk, Azure Speech, the browser's
`SpeechRecognition` API, or any streaming recognizer.

## C. Comparison

| | Gadfly: ElevenLabs Scribe (live default) | Gadfly: OpenAI Whisper (built in, off) | Prose: Groq Whisper turbo | Prose: OpenAI Whisper |
|---|---|---|---|---|
| Streaming | Yes, partials while speaking | No, upload on commit | No, upload per utterance | No |
| Word timings | Yes | Yes (`verbose_json`) | Not requested | Not requested |
| Per-word confidence | Yes | No | No | No |
| Speaker labels | Yes (`scribe_v3_live`) | No | No | No |
| Latency evidence | None measured yet (GAD-T413 now records it) | None | "Fastest hosted" (comment only) | None |
| Cost evidence in repo | Not stated | Not stated | $0.04/hour (comment) | $0.006/minute (comment) |
| In MindAttic.SpeechRecognition | Yes | Yes | No (filed as SR-T12) | Yes |

What this means for Gadfly:

1. **Nothing in Prose is measured.** The only speed evidence is a one-line claim in a comment. Gadfly's own
   timings from GAD-T413 are the first real numbers either repository will have.
2. **A batch recognizer is not obviously faster for a Gadfly answer.** Scribe has already transcribed most of an answer
   by the time it ends. A batch provider starts only at the commit, with the whole answer to upload and transcribe;
   Gadfly's answers run from several seconds to a minute or more, while Prose's dictation utterances are short. Groq may
   still win on commit-to-final time for short answers, but that has to be measured, not assumed.
3. **Switching would lose things Gadfly relies on.** Neither Whisper path gives partials (the live transcript and the
   end-of-answer cue use them, GAD-T286), per-word confidence (low-confidence words are named to the grading panel and
   marked for reviewers, GAD-T265 and GAD-T405), or speaker labels (the second-voice note). These are fairness and
   integrity features, not cosmetics, and any provider change has to keep them or replace them.
4. **The credible candidate is Groq, and the library does not have it as a provider.** Plain OpenAI Whisper already
   exists as an off-by-default choice in Gadfly and nothing suggests it is faster than Scribe. Groq's endpoint is
   OpenAI-compatible, so the existing `OpenAiWhisperRecognizer` could in principle be pointed at it through its
   `BaseUri`, `Model` and key options, but it would then report itself as `openai`, read the OpenAI key path unless
   reconfigured, and Groq's support for every field it sends (`timestamp_granularities[]`, segment log-probabilities)
   is unverified. That is library work, so per GAD-T414 it was filed rather than improvised in Gadfly:
   **SR-T12** (MindAttic.SpeechRecognition): a proper Groq provider modelled on `OpenAiWhisperRecognizer`, with its
   own provider name, Vault key (`MindAttic:Vault:LLM:groq:apiKey`), options, docs and tests.
5. **A cheaper experiment exists inside the current provider.** The library's own default Scribe model is
   `scribe_v2_realtime`; Gadfly uses `scribe_v3_live` only for speaker labels. Whether v2 is quicker to a final segment
   is not documented anywhere in either repository, but it can be compared through `Gadfly:Voice:ScribeModel` once
   GAD-T413's timings exist, at the price of the second-voice note.

## D. Recommendation

- Do not switch Gadfly's live provider. ElevenLabs Scribe stays the default; no Gadfly code or setting was changed for
  this note.
- Collect a week of live `/healthz/call-timings` rows for the recognizer (commit to final segment, by answer length)
  before deciding anything.
- If those show the recognizer is a large share of the wait between an answer and the next question, take SR-T12 into
  the library and then add `Groq` as another off-by-default value of `MindAttic:SpeechRecognition:Provider`, with a
  `docs/VOICE.md` line, and time it on the same answers. Decide on measured commit-to-final time, and only with a plan
  for the confidence, partial and speaker-label features a Whisper provider does not give.

## Sources checked

- `D:\Projects\MindAttic\Prose\src\Prose.Core\Services\Discussion\SpeechService.cs` (whole file)
- `D:\Projects\MindAttic\Prose\src\Prose.WriterUi\wwwroot\js\writer.js` (`window.proseMic`)
- `D:\Projects\MindAttic\Prose\src\Prose.Core\Services\ProseSettings.cs` (`groq` credential)
- `D:\Projects\MindAttic\MindAttic.SpeechRecognition\src\MindAttic.SpeechRecognition.AspNetCore\ServiceCollectionExtensions.cs`
  (`RecognizerFactory`)
- `D:\Projects\MindAttic\MindAttic.SpeechRecognition\src\MindAttic.SpeechRecognition\Providers\ElevenLabs\` and
  `Providers\OpenAI\OpenAiWhisperRecognizer.cs`
- `Gadfly.Server/Voice/VoiceServices.cs`, `docs/VOICE.md`, `Gadfly.Server/Diagnostics/ApiCallTimings.cs`
