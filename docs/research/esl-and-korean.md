# Research note: examining non-native English speakers, Korean first

Prepared 2026-11-22 for Gadfly (GAD-T553, part of GAD-T551 "Language profile and ESL", under GAD-T526 "Korean exams").
The owner asked how Gadfly should adapt so that a student's accent, grammar and fluency in a second language do not cost
them marks, starting with Korean speakers of English (the owner's wife is a native Korean speaker with English as her
second language), and what a Korean-language exam would need. This is a desk review: no paid API was called, nothing was
timed, and no code was changed. It builds on research note 02 (Koenecke2020, Mujtaba2024, Horwitz1986, LevAri2010) and
note 05 (LLM-as-judge biases), and does not repeat them.

**Scope rule from the owner (GAD-T551 note, 2026-11-22).** A student whose primary language is English and who takes the
exam in English gets exactly today's experience. Every adaptation below switches on only when the exam language differs
from the student's self-declared primary language (for example Korean primary, English exam). Call this **ESL mode** below.

Verification levels follow `references/VERIFICATION.md`: **Full text** (the work, or the relevant sections, was read),
**Abstract** (abstract, publisher or index page read), **Record** (only the bibliographic record or a secondary summary
was seen; the secondary source is named), **Docs** (vendor documentation read on the date above; vendor claims are not
independent evidence). Every journal DOI in Part 2 was resolved against Crossref on 2026-11-22.

---

## Part 1. Recommendations

Numbers marked *design* are Gadfly's choices, reasoned from the evidence but not values any source gives. Each should
be checked in the pilot (Part 3).

### R1. Ask for the language profile; never infer it

- Ask three things before the first exam (GAD-T552): the language the student speaks most comfortably (primary), any
  other languages, and the language the exam is taken in (fixed by the instructor or chosen by the student where the
  instructor allows). Offer "prefer not to say"; that answer means no ESL mode, exactly today's experience.
- Store it as a self-declared exam setting, not a demographic attribute. Never infer it, or nationality, or ethnicity,
  from the voice, accent, name, face or the recognizer's language detection. Keep `include_language_detection=false`
  (the library's default option) for exam audio, and never store a detected language against a student.
- Use it for exactly two purposes: switching ESL mode on, and grouping the existing fairness views
  (`Gadfly.Server/Analytics/GroupFairness.cs`, `SpeechRecognitionAudit.cs`, `PathDrivers.cs`) by primary language, which
  today needs a pasted grouping. Keep it out of everything the grading panel and the blind human grader see (see R5).
- Section "Privacy and law" (E6) gives the legal reasoning.

### R2. Recognition for English exams in ESL mode

1. **Pin the language.** Always send `language_code=en` for an English exam (Gadfly already sends `language: "en"`
   from `voice-room.js`; keep it pinned, never auto-detect). Accented English with auto-detection risks segments being
   transcribed as another language.
2. **Keyterms are the strongest lever Gadfly has on a hosted recognizer.** Keep the exam's subject vocabulary as
   keyterms (`Gadfly.Server/Voice/VoiceVocabulary.cs`). Technical words are exactly where an accented pronunciation and
   a rare word combine, and they carry most of the content the rubric scores. ElevenLabs documents that the model uses
   context and does not force a keyterm everywhere (E1.4).
3. **Fix the cap mismatch (bug, filed separately).** ElevenLabs' current realtime limits are **50 keyterms and 1,000
   characters in total** (Docs, E1.4). Gadfly allows 60 terms of up to 60 characters (`VoiceVocabulary.MaxTerms = 60`,
   `MaxTermLength = 60`) and the library passes up to 100. Cap at 50 terms and 1,000 characters, most important first
   (terms from the objectives being examined before the rest). Whether the API rejects or silently drops the excess was
   not tested. Keyterms carry an extra charge (Docs), so confirm the cost before widening their use.
4. **Whisper fallback.** Send `language=en` (done) and keep the `prompt` under Whisper's 224-token window; Prose already
   caps its prompt at 700 characters for this reason (`stt-alternatives.md`). The library joins every keyterm into the
   prompt with no cap, and Whisper keeps only the last part of a long prompt. Put the most important terms last, or cap.
5. **Do not turn on `secondary_languages=["ko"]` for English exams by default.** It would help a student who drops a
   Korean word mid-answer, but it also gives the model a second language to drift into on accented English. Test it on
   the Korean-L1 evaluation set (R2.7) before using it.
6. **Do not fine-tune now.** Gadfly uses a hosted recognizer, which cannot be fine-tuned. Fine-tuning an open Whisper on
   Korean-L1 English works in research (LearnerVoice: 44.2% lower WER than vanilla whisper-small.en, E1.3), but the open
   Korean-L1 corpora are licensed non-commercially (L2-ARCTIC: CC BY-NC 4.0) or have unconfirmed terms (LearnerVoice,
   AI Hub), and naive fine-tuning can widen gaps for weaker learners (E1.3). Revisit only if R2.7 shows a gap that
   keyterms cannot close.
7. **Measure before tuning.** Build a small Korean-L1 English evaluation set with consent: at least 6 speakers to start
   (the owner's wife plus volunteers), 10 spoken answers each to real Gadfly questions, with human reference transcripts.
   Compare `scribe_v3_live` with and without keyterms, `scribe_v2_realtime`, and Whisper, on word error rate and on
   **keyterm recall** (share of rubric terms the student said that appear correctly in the transcript). The L2-ARCTIC
   Korean speakers (HJK, YDCK, HKK, YKWK) and its spontaneous "suitcase" subset are a research-only cross-check; check the
   non-commercial licence before using them inside a commercial product's evaluation.

### R3. Endpointing and pauses in ESL mode

Second-language speech has more and longer pauses, above all **inside clauses** (mid-thought), where native speakers
rarely pause (E2). Gadfly's end-of-answer silence (GAD-T264, GAD-T498, `VoiceSettings.cs`, `voice-room.js endCueMs`) is
built for that shape but starts from native defaults. In ESL mode:

| Setting | Today | ESL mode (*design*) | Why |
|---|---|---|---|
| `EndOfAnswerSeconds` (floor) | 4 s | **6 s** | Longer mid-clause pauses; the adaptive p80 needs a few pauses before it can lengthen the cue, so the first answers rely on the floor. |
| `EndOfAnswerCompleteSeconds` (after a "finished sentence") | 2 s | **3.5 s**, and only after 3 answers with no cut-off | The finished-sentence test reads the recognizer's punctuation and an English list of continuation words. L2 speakers pause after grammatically complete clauses while still planning the next (E2), so the short cue cuts them off more. |
| `EndOfAnswerMaxSeconds` (ceiling) | 12 s | **15 s** | Room for the p80 x 1.5 rule to follow a slow planner. |
| Adaptive rule | p80 of last 20 pauses x 1.5 | unchanged | It already adapts to the individual; only its starting point moves. |
| Cut-off backoff (GAD-T462) | per cut-off | **double the step** | A cut-off costs an L2 speaker more (they lose the thread while re-planning in L2). |

- Apply ESL mode as its own multiplier, separate from the extra-time accommodation, so an instructor's extra time still
  stacks on top and the hard cap (45 s) still applies.
- **Long pauses must not drive pressure.** `PathDrivers.cs` already counts long pauses per minute by group; the
  examiner must not treat pause length or count as a sign of weak understanding for anyone, and the grouping by primary
  language will show whether it does.
- **The backchannel** ("mm-hm") must not fire inside a mid-clause pause shorter than the ESL floor, or it reads as "your
  turn is over".
- The "Done answering" button stays the reliable way to finish; tell ESL-mode students so in the practice run.

### R4. How the examiner speaks in ESL mode

The evidence (E3) says the most useful thing is not slower speech or simpler sentences on their own but **interaction**:
being able to have a question repeated and rephrased. Concretely:

1. **Repeat, then rephrase, on request, at no cost.** "Repeat the question" exists (GAD-T474). Add "Say it another way":
   the examiner rephrases with the same technical terms, shorter sentences and no new information, and the rephrasing
   passes through the same fail-closed leak guard as any question. Neither request counts as a hint, a prompt on a
   dynamic-assessment ladder, a path-driver cue or a time penalty; the clock pauses while the examiner speaks.
2. **Plain question style** (instruction to the question writer in ESL mode; *design*, after ISO 24495-1 plain-language
   principles): one question per turn; at most about 20 words; keep the subject's technical terms (they are the
   construct) but use common words for everything else; no idioms, phrasal verbs ("figure out", "come up with"),
   sarcasm, rhetorical questions or negative questions ("Wouldn't it be...?"); name the thing rather than "this" or
   "that one"; say what kind of answer is wanted ("Give one reason", "Explain the steps").
3. **Speaking rate.** Slightly slower and with clear pauses between sentences, not slowed word by word: use the TTS
   speed setting at about **0.9** (*design*; the 0.7 to 1.2 range comes from a third-party page, check ElevenLabs' own
   voice-settings reference before relying on it). Lower-intermediate listeners lose comprehension at fast rates but gain
   little from much slower ones (E3.2). Keep the word captions of the examiner's question on screen in ESL mode.
4. **Check understanding by invitation, not by asking "Do you understand?"**, which invites a polite yes. At the start of
   the exam the examiner says once: "If a question is unclear, ask me to repeat it or to say it another way. That never
   counts against you." In the practice run, have the student try both requests.
5. **Do not correct the student's English, and do not echo it back corrected.** Recasting is a language-teaching move;
   in an exam it signals that form is being judged.

### R5. Grade content, not form

1. **Keep the rule universal; do not tell the judges the student is a second-language speaker.** GPT-4o scored essays
   less accurately for non-native writers when it had correctly inferred their first language (E4.3), so labelling the
   student invites the bias. The judge prompt already says grammar, accent, fluency and non-native phrasing are never
   penalized (GAD-T269); keep that for every student.
2. **Do not rely on the instruction alone.** Two open models kept penalizing non-native phrasing and informal language
   on essay tasks despite explicit counter-bias instructions in the grading prompt (E4.3). Two cheap mitigations:
   - **A content view for the judges** (*design*): give the panel the transcript with fillers ("um", "uh"), immediate
     word repetitions and abandoned false starts removed by a deterministic rule, never by a model rewrite (a rewrite can
     add or remove content). The verbatim transcript stays the record that the human grader and the integrity review see.
   - **Name the common Korean-L1 surface forms as irrelevant**, inside the universal rule, so the model does not have to
     infer it: missing or extra articles, singular/plural, tense and agreement slips, dropped subjects, word order,
     preposition choice. These are among the most frequent transfer patterns for Korean speakers of English.
3. **Measure it with the probe Gadfly already has.** Extend `PanelBiasCheck` / `BiasProbes.cs` with L2-form paraphrases
   of the calibration answers: the same content rewritten with the Korean-L1 surface forms above and with fillers. Pass
   rule (*design*): the mean panel score of the L2-form version is within **0.25 points on the 0 to 10 scale** of the
   fluent version, and no rubric criterion moves by more than 0.5. Run it before ESL mode is offered and after any change
   of judge model or prompt.
4. **Monitor by primary language** in the fairness views, with the existing minimum of 5 students per group: word error
   rate and keyterm recall; cut-off share; repeat and rephrase requests per question; path pressure raised; panel score;
   **panel-minus-human score difference** (the key one: a group whose panel scores fall below its blind human scores is
   being penalized by the panel, not by its understanding); human-review rate. Flag a group whose panel-minus-human gap
   differs from the English-primary group by more than 0.5 points (*design*).
5. **Human graders read the transcript, not the voice, by default.** Listener accent penalties are mixed in the
   literature (note 02, LevAri2010), but rater leniency or severity does vary with familiarity with the speaker's first
   language (E4.1). Audio should be opened only to resolve a recognition doubt.

### R6. Korean-language exams (GAD-T526)

1. **Recognition.** Scribe supports Korean; ElevenLabs reports 10.7% WER on FLEURS Korean for Scribe v1 and a lower,
   unversioned 3.1% on some pages (vendor claims, E5.1). Send `language_code=ko`. Add **`secondary_languages=["en"]`**:
   Korean academic speech borrows English technical terms freely, so code-switching is the normal case, not the
   exception. Give keyterms in both scripts (the Korean term and the English one). Measure Korean with **character error
   rate**, not WER: Korean spacing is inconsistent, so WER over-counts errors.
2. **Endpointing must be localized.** `finishedSentence` and the continuation-word list are English. Korean marks a
   finished sentence with sentence-final endings (for example -다, -요, -니다, -까) and a continuation with connective
   endings (-고, -는데, -서, -면, -지만); until a Korean rule exists, a Korean exam should use only the full floor, never
   the short "finished sentence" cue.
3. **Path drivers must be localized or switched off.** The hedging, vague-phrase and reason lists in
   `AnswerDelivery.Read` are English word lists; on Korean transcripts they fire wrongly or not at all.
4. **Examiner voice.** ElevenLabs `eleven_flash_v2_5` (Gadfly's default TTS) and `eleven_multilingual_v2` both support
   Korean (Docs), so the current pipeline works unchanged apart from choosing a voice that sounds natural in Korean. For
   mock mode or offline use, MeloTTS Korean is MIT-licensed (E5.2). Use one polite register consistently: 해요체 (the
   polite informal style) for questions is the natural choice for a one-to-one conversation; a native speaker (the
   owner's wife) should confirm it sounds like an examiner and not a customer-service agent.
5. **Judging Korean answers against English material.** Keep the rubric and source passages in English and judge the
   Korean answer directly (cross-lingual judging), rather than machine-translating the answer first, which can smooth
   or alter exactly the reasoning being judged. Cross-lingual judging with English references is workable (E5.3), and
   judges' Korean ability tracks their English judging ability (KUDGE). But judge agreement across languages is low
   (Fleiss' kappa about 0.3 across 25 languages) and judges prefer English answers in pairwise comparisons (E5.3), so:
   - keep the multi-family panel (ensembles improve cross-language consistency);
   - tell each judge to quote the student's Korean words in its evidence and give its own English gloss beside them,
     so a human can check the reading;
   - require **human approval of every Korean-exam score** until a double-marked Korean calibration set (bilingual
     human grader) shows panel-human agreement at least as good as on English answers;
   - run the leak guard on Korean questions with Korean test cases; a guard tuned on English may miss a Korean leak.

### R7. Privacy and law

- Primary language, asked and self-declared, is ordinary personal data, but it can reveal national or ethnic origin, so
  treat it with special-category safeguards: optional, purpose-limited to the two uses in R1, kept out of grading,
  reported only in aggregate with the 5-student minimum, deleted with the account, and described in `docs/PRIVACY.md`.
- Gadfly is high-risk under the AI Act (Annex III, point 3, evaluating learning outcomes; note 05), so it must examine
  its data for biases (Art. 10(2)(f), (g)). Article 4a, added by the Digital Omnibus (consolidated text of 27 July
  2026), permits processing special categories "to the extent strictly necessary" for bias detection, with six
  safeguards. Self-declared language is the less intrusive route, which is itself one of those conditions (no effective
  alternative).
- **Never infer language background from the voice.** Deducing ethnic origin or race from biometric data is a
  prohibited biometric categorisation under Art. 5(1)(g) of the AI Act; accent identification from voice comes close
  enough that Gadfly should not build it, and it is not needed when students can simply say.

---

## Part 2. Evidence

### E1. Recognition accuracy by accent and first language

**E1.1 Koenecke et al. (2020).** See note 02 (Abstract): five commercial recognizers, average WER 0.35 for black and 0.19
for white American speakers, the gap holding on identical phrases. Establishes that recognizer gaps come from acoustics,
not word choice. Dated vendors. https://doi.org/10.1073/pnas.1915768117

**E1.2 Graham, C., & Roll, N. (2024).** Evaluating OpenAI's Whisper ASR: Performance analysis across diverse accents and
speaker traits. *JASA Express Letters*. https://doi.org/10.1121/10.0024876 (accepted version
https://doi.org/10.17863/CAM.105762). **Abstract.** Paraphrase: native accents are recognized more accurately than
non-native ones; WER is related to speaker sex, L1 typology and L2 proficiency; read speech is recognized better than
conversational speech. Relevance: exam answers are spontaneous, the harder case. Confidence: Medium-High (peer-reviewed;
Whisper only).

**E1.3 Roll, N., & Graham, C. (2025).** Scaling conformation bias in automatic speech recognition. *Proc. SLaTE 2025*,
133-137. https://doi.org/10.21437/SLaTE.2025-27 (https://www.isca-archive.org/slate_2025/roll25_slate.pdf). **Full text.**
Ten models (NVIDIA Canary, Whisper, Phi-4) on 300+ English varieties. Verified: "as models increase in size and L1 English
performance, the relative disparity for L2 speakers worsens"; tonal-L1 speakers had a mean WER of about 10%, "more than
double" stress-accent L1 speakers (about 5.5%) and nearly three times native speakers. Relevance: a better recognizer
does not by itself close the L2 gap, so Gadfly must measure its own. Korean is not a tonal language (Seoul Korean), so the
tonal penalty does not transfer directly; Korean L1 speakers fall among the non-tonal L2 groups. Confidence: High for the
direction; Gadfly's provider (Scribe) was not tested.

**E1.4 McGuire, M. (2025).** Automatic speech recognition for non-native English: Accuracy and disfluency handling.
arXiv:2503.06924. https://arxiv.org/abs/2503.06924 **Abstract.** Five systems on L2-ARCTIC (six L1s including Korean):
best mean match error rate 0.054 on read speech (Whisper) and 0.063 on spontaneous narratives (RevAI); disfluency
handling varied widely between systems. No per-L1 figures in the abstract. Confidence: Low-Medium (preprint).

**E1.5 Kim, H., Myung, J., Kim, S., Lee, S., Kang, D., & Kim, J. (2024).** LearnerVoice: A dataset of non-native English learners' spontaneous speech.
*Proc. Interspeech 2024*. https://www.isca-archive.org/interspeech_2024/kim24v_interspeech.pdf, arXiv:2407.04280.
**Record** (abstract and search summary). 50.04 h of spontaneous speech from 58 Korean-L1 learners of English (tutoring
sessions on Ringle). Fine-tuning whisper-small.en reached 10.26% WER, 44.2% below the vanilla model; 54.2% of the vanilla
model's errors were tied to L2 spontaneous-speech features (fillers, self-repairs, ungrammatical expressions), and 48.1% of
those were reduced. Relevance: the most Gadfly-like data that exists (Korean L1, spontaneous, conversational); shows
fine-tuning helps and that much of the error is disfluency, which keyterms do not fix. Licence not confirmed. Confidence:
Medium.

**E1.6 Proficiency-aware adaptation (2025).** Proficiency-aware adaptation and data augmentation for robust L2 ASR.
arXiv:2510.10738. https://arxiv.org/abs/2510.10738 **Record** (search summary). Naive Whisper fine-tuning lowered average
WER but widened disparities and harmed lower-level learners; a LoRA variant moved WER from 10.2 to 7.2. Also: MAS-LoRA
(arXiv:2505.20006), one LoRA expert per accent on L2-ARCTIC, beat plain LoRA and full fine-tuning. Confidence: Low
(preprints, not read).

**E1.7 Corpora.**
- **L2-ARCTIC** (Zhao et al., 2018, *Proc. Interspeech*, https://doi.org/10.21437/Interspeech.2018-1110; corpus page
  https://psi.engr.tamu.edu/l2-arctic-corpus/). **Abstract + corpus page.** 24 speakers, six L1s including Korean (HJK,
  YDCK female; HKK, YKWK male), about one hour of read ARCTIC prompts each, 150 utterances per speaker annotated for
  pronunciation errors, later a spontaneous "suitcase" subset. CC BY-NC 4.0.
- **speechocean762** (Zhang et al., 2021, *Proc. Interspeech*, arXiv:2104.01378). **Abstract + dataset card.** 5,000
  utterances, 250 speakers, half children, all Mandarin L1; free for commercial use. Not Korean; useful only for
  pronunciation-scoring work, which Gadfly does not do.
- **Korean learners' L2 corpus** (Han, Chung & Kim, 2024, *LREC-COLING 2024*, 3772-3781,
  https://aclanthology.org/2024.lrec-main.334). **Abstract (from the PDF).** 1,200 h of L2 speech from Korean learners, 400
  h of it English, with expert pronunciation and prosody-fluency scores and self-reported proficiency; distributed via
  AI Hub (dataset pages linked in the paper). Read speech. AI Hub terms not checked.
- **KsponSpeech** (Bang et al., 2020, *Applied Sciences*, 10(19), 6936, https://doi.org/10.3390/app10196936). **Record.**
  About 969 h of spontaneous Korean conversation, the standard Korean ASR benchmark (relevant to R6, not to accented
  English).

**E1.8 ElevenLabs documentation (Docs, read 2026-11-22).** Keyterm prompting guide
(https://elevenlabs.io/docs/eleven-api/guides/how-to/speech-to-text/batch/keyterm-prompting): realtime models
`scribe_v3_live` and `scribe_v2_realtime` accept keyterms, at most 50 and 1,000 characters in total; keyterms "work best
for a few terms you know will appear"; they cost extra. Realtime API reference
(https://elevenlabs.io/docs/api-reference/speech-to-text/v-1-speech-to-text-realtime): `language_code`,
`secondary_languages`, `commit_strategy` (`manual`, `vad`, `turn_prediction`), `vad_silence_threshold_secs`,
`include_language_detection` (default false), `no_verbatim`, `keyterms`. (A LiveKit page says keyterms are not compatible
with `scribe_v2_realtime`; ElevenLabs' own docs say they are.)

### E2. Pauses and fluency of second-language speakers

**E2.1 Kahng, J. (2014).** Exploring utterance and cognitive fluency of L1 and L2 English speakers: Temporal measures and
stimulated recall. *Language Learning, 64*(4), 809-854. https://doi.org/10.1111/lang.12084 **Abstract** (and the MSU
dissertation record, https://d.lib.msu.edu/etd/3005). Korean learners of English versus L1 English speakers: they differed
in speed, length of run and silent pauses, with "a striking difference in the frequency of pauses within clauses";
stimulated recall tied L2 mid-clause pauses to difficulty in formulating speech. Relevance: the directly Korean-L1 evidence
that L2 speakers stop mid-thought, which is what an end-of-answer silence mistakes for a finished answer. Confidence:
High for direction; no millisecond values were extracted.

**E2.2 Huensch, A. (2023). Effects of speaking task and proficiency on the midclause pausing characteristics of L1 and L2 speech from the
same speakers.** *Studies in Second Language Acquisition, 45*(4), 1031-1055 (LANGSNAP corpus); https://doi.org/10.1017/S0272263123000323. **Abstract.** Both L1 and L2 speech pause at clause
boundaries, but L2 learners pause more often and longer within clauses; mid-clause pause rate was the one measure that
consistently separated L1 from L2 speech in interviews. Relevance: confirms E2.1 within speakers, controlling for
personal style. Confidence: Medium-High.

**E2.3 de Jong, N. H., & Bosker, H. R. (2013).** Choosing a threshold for silent pauses to measure second language
fluency. *Proc. DiSS 2013*, 17-20. https://hrbosker.github.io/publication/dejong-bosker-2013 **Record.** Thresholds in the
literature range from 100 to 1,000 ms; the authors relate pause measures at different thresholds to proficiency (the
commonly cited outcome is about 250 ms, after Goldman-Eisler). Relevance: fluency research counts pauses far shorter than
Gadfly's end-of-answer silence, so a 4 s floor is not cutting ordinary pauses; the risk is the long planning pause, which
is what E2.1 and E2.2 show L2 speakers make inside clauses. Confidence: Medium.

**E2.4 Stivers, T., et al. (2009).** Universals and cultural variation in turn-taking in conversation. *PNAS, 106*(26),
10587-10592. https://doi.org/10.1073/pnas.0903616106 **Abstract.** Ten languages, Korean among them: all avoid overlap
and minimize silence between turns; language means differ within about 250 ms of the cross-language mean. Relevance:
turn-taking timing is not culturally very different for Korean; the L2 effect is about planning in a second language,
not cultural pacing. Confidence: High.

No source gives an end-of-turn silence for L2 speakers in a spoken exam. The ESL values in R3 are design choices.

### E3. How to speak to second-language listeners

**E3.1 Pica, T., Young, R., & Doughty, C. (1987).** The impact of interaction on comprehension. *TESOL Quarterly, 21*(4),
737-758. https://doi.org/10.2307/3586992 **Abstract.** "Comprehension was best assisted when the content of the directions
was repeated and rephrased in interaction"; reducing linguistic complexity in pre-modified input was "not a significant
factor". Relevance: the basis for R4.1 (repeat and rephrase on request) over simplification alone. Confidence: Medium-High
(classic, small-sample, directions task).

**E3.2 Griffiths, R. (1992).** Speech rate and listening comprehension: Further evidence of the relationship. *TESOL
Quarterly, 26*(2), 385-390. https://doi.org/10.2307/3587015 **Record** (secondary account in a JALT paper,
https://jalt-publications.org/archive/proceedings/2001/532.pdf). With his 1990 study: faster speech reduced lower-
intermediate learners' comprehension, while slower speech offered few benefits. Relevance: R4.3, slightly slower and
better paused, not slow. Confidence: Low-Medium (not read directly).

**E3.3 Abedi, J., & Lord, C. (2001).** The language factor in mathematics tests. *Applied Measurement in Education,
14*(3), 219-234. https://doi.org/10.1207/S15324818AME1403_2 **Record** (summaries). Linguistically simplified NAEP maths
items gave small but significant gains for English learners in lower-level classes, with the maths unchanged. Relevance:
plain wording of questions helps a little and does not change the construct.

**E3.4 Kieffer, M. J., Lesaux, N. K., Rivera, M., & Francis, D. J. (2009).** Accommodations for English language learners
taking large-scale assessments: A meta-analysis on effectiveness and validity. *Review of Educational Research, 79*(3),
1168-1201. https://doi.org/10.3102/0034654309332490 **Record** (IES summary,
https://ies.ed.gov/ncee/edLabs/regions/appalachia/askarel/aar48.asp). Overall accommodation effect about 0.04 SD; only
English dictionaries or glossaries showed a clear benefit (g = 0.18); accommodations did not change non-learners' scores.
Relevance: no single wording trick closes the gap; R4 and R5 together do more than either. The glossary result supports
letting the examiner define a non-technical word on request (part of "say it another way"), never a technical one.
Confidence: Medium.

**E3.5 ISO 24495-1:2023**, Plain language, Part 1: Governing principles and guidelines. https://www.iso.org/standard/78907.html
**Record.** The source for the plain-question style in R4.2 (readers can find, understand and use what they need).

### E4. Rater bias and LLM judges

**E4.1 Winke, P., Gass, S., & Myford, C. (2013).** Raters' L2 background as a potential source of bias in rating oral
performance. *Language Testing, 30*(2), 231-252. https://doi.org/10.1177/0265532212456968 **Abstract.** 107 raters, 432
TOEFL iBT speaking samples from Spanish, Chinese and Korean L1 test takers; raters who had learned the test taker's L1
were more lenient (significant for Spanish and Chinese; no comparable effect reported for Korean). Relevance: human
ratings of speech depend on the rater's familiarity with the accent; R5.5 keeps humans on transcripts. Confidence: Medium.

**E4.2 Hofmann, V., Kalluri, P. R., Jurafsky, D., & King, S. (2024).** AI generates covertly racist decisions about people
based on their dialect. *Nature, 633*, 147-154. https://doi.org/10.1038/s41586-024-07856-5 **Abstract.** Language models
hold covert negative stereotypes about speakers of African American English that show in decisions (jobs, convictions)
while overt statements are positive, and human-feedback training widened the covert-overt gap. Relevance: a model's
stated fairness does not show its behaviour toward non-standard English; Gadfly must measure, not trust the prompt.
Confidence: High (different dialect, same mechanism).

**E4.3 Grading-specific studies (preprints).**
- Yang, K., Raković, M., Gašević, D., & Chen, G. (2025). Does the prompt-based large language model recognize students'
  demographics and introduce bias in essay scoring? arXiv:2504.21330. **Abstract.** GPT-4o on 25,000+ essays partly
  infers first language; "scoring error for non-native English speakers increases when the LLM correctly identifies them
  as non-native." Basis for R5.1.
- Jadhav, R., Danve, J., & Shaw, S. (2026). Implicit grading bias in large language models: How writing style affects
  automated assessment across math, programming, and essay tasks. arXiv:2603.18765. **Abstract.** LLaMA 3.3 70B and Qwen
  2.5 72B on 180 responses with grammar errors, informal language or non-native phrasing added at equal correctness:
  significant bias on essay tasks, little on maths and programming; it "persists despite explicit counter-bias
  instructions in the grading prompt." Basis for R5.2.
- Further (search summaries only): ESL essays of equal human-rated quality scored lower by a fine-tuned DeBERTa
  (arXiv:2601.16724); a fine-tuned GPT-3.5 more lenient than humans across four L1 groups (Liu, Qi & Lu, 2025, as cited in
  arXiv:2607.14605).
- Confidence: Medium for the direction (several independent preprints agree), Low for any size. None tests spoken,
  transcribed answers, and none tests Gadfly's panel; R5.3 is the test that would.

### E5. Korean-language exams

**E5.1 Korean recognition.** ElevenLabs' Korean page (https://elevenlabs.io/speech-to-text/korean, **Docs**) reports Scribe
v1 at 10.7% WER on FLEURS Korean and 14.1% on Common Voice, against Whisper large-v3 at 18.1%; some localized pages give
3.1% FLEURS without a model version. The Scribe v3 post (https://elevenlabs.io/blog/scribe-v3) claims 27% lower WER than
v2 across 102 FLEURS languages, with no Korean figure. A raw Whisper large-v3 CER of about 12% on KsponSpeech appears only
on a fine-tune's model card. All vendor-reported; no independent benchmark of Scribe on Korean was found.

**E5.2 Korean speech synthesis.** ElevenLabs models page (https://elevenlabs.io/docs/models, **Docs**): Korean is in the
Multilingual v2 set and Flash v2.5 covers all of it. MeloTTS Korean (https://huggingface.co/myshell-ai/MeloTTS-Korean),
MIT licence (**Docs**). No independent quality comparison was found.

**E5.3 Cross-lingual and Korean judging.**
- Hada, R., et al. (2024). Are large language model-based evaluators the solution to scaling up multilingual evaluation?
  *Findings of EACL 2024*, 1051-1070. https://aclanthology.org/2024.findings-eacl.71 **Abstract.** 20,000 human judgments,
  eight languages: GPT-4 evaluators inflate scores, especially in lower-resource and non-Latin-script languages; calibrate
  against native-speaker judgments.
- Son, G., et al. (2024). LLM-as-a-judge & reward model: What they can and cannot do. arXiv:2409.11239 (KUDGE).
  **Abstract.** 5,012 Korean human annotations: judges perform about as well on Korean as their English benchmark scores
  predict, but struggle on hard reasoning prompts and on cultural misrepresentation. MIT-licensed data.
- Doddapaneni, S., et al. (2025). Cross-lingual auto evaluation for assessing multilingual LLMs. *ACL 2025*.
  https://aclanthology.org/2025.acl-long.1419 **Abstract.** Questions and responses in the target language, reference
  answers, instructions and rubrics in English; the trained evaluator (Hercule) agreed with humans better than proprietary
  models. Direct support for R6.5's setup (Korean answer, English rubric).
- Fu, X., & Liu, W. (2025). How reliable is multilingual LLM-as-a-judge? arXiv:2505.12201. **Abstract.** Five models, 25
  languages: average Fleiss' kappa about 0.3 across languages; scale and multilingual training do not fix it; an ensemble
  helps.
- Fairness or fluency? An investigation into language bias of pairwise LLM-as-a-judge (2026). arXiv:2601.13649.
  **Record** (search summary). Most models prefer English answers, driven more by answer language than question language.
- Confidence: Medium. Together they say cross-lingual judging is feasible but needs a panel, human calibration and human
  approval at first.

### E6. Privacy and law

- **AI Act, Article 10** (https://ai-act-service-desk.ec.europa.eu/en/ai-act/article-10) and **Article 4a**
  (https://ai-act-service-desk.ec.europa.eu/en/ai-act/article-4a), **Docs** (consolidated text as at 27 July 2026, Article 4a
  marked new by the Digital Omnibus). Article 4a(1): providers of high-risk systems may exceptionally process special
  categories "to the extent strictly necessary" for bias detection and correction under Article 10(2)(f) and (g), with six
  cumulative conditions (no effective alternative such as synthetic or anonymised data; limits on re-use and
  pseudonymisation; strict access control; no transfer; deletion once corrected; reasons recorded). It "does not create
  any obligation". Annex III point 3 and the human-oversight duty: note 05.
- **GDPR Article 9** lists racial or ethnic origin, not language or nationality, as special categories; data that can
  reveal ethnic origin is treated cautiously in practice. Bias detection is not an explicit Article 9 exception (Bloomberg
  Law commentary,
  https://news.bloomberglaw.com/bloomberg-government-news/gap-in-the-eus-rules-for-ai-requires-a-well-documented-approach).
  This note is not legal advice; `docs/COMPLIANCE.md` and counsel should confirm R7 before an EU deployment.

---

## Part 3. Open questions and pilot metrics

**Open questions**
1. How large is Scribe's WER gap for Korean-L1 spontaneous English, and how much do keyterms close it? (R2.7; nothing
   published answers this for Scribe.)
2. Does the API reject, truncate or accept more than 50 keyterms or 1,000 characters? (R2.3.)
3. Do the ESL endpointing values cut off fewer answers without making the exam feel slow? (R3.)
4. Does the panel penalize Korean-L1 surface forms on spoken, transcribed answers, and does the content view remove it?
   (R5.3; no study tests transcribed speech.)
5. Should the panel be told nothing about the student's language (R5.1), or does a neutral universal instruction plus the
   content view leave a residual gap that only an explicit note fixes? Measure, do not assume.
6. For Korean exams: is cross-lingual judging accurate enough on Gadfly's own material, and which register do Korean
   students find natural for an examiner?
7. Is self-declared primary language a good enough grouping, or do proficiency differences within "Korean primary"
   dominate? (Consider an optional self-rated English proficiency, used only for fairness analysis.)

**Pilot metrics** (by primary language, minimum 5 students per group, ESL mode on versus off where ethically possible,
e.g. in practice runs):

| Metric | Source in Gadfly | Target (*design*) |
|---|---|---|
| Word error rate and keyterm recall on reference transcripts | `SpeechRecognitionAudit`, `ReferenceTranscripts` | Korean-L1 WER no more than 1.5x English-L1; keyterm recall at least 90% |
| Cut-off share (answers closed while the student went on) | `PathDrivers` (GAD-T462) | under 5% of answers, and no worse than English-L1 |
| Repeat and rephrase requests per question | new counter | reported, not targeted; a rise after a wording change shows the change hurt |
| Panel-minus-human score difference | `ScoreGaps` / grading records | group gap within 0.5 points of English-L1 |
| L2-form paraphrase probe | `PanelBiasCheck` | mean shift at most 0.25 of 10 |
| Path pressure raised and long pauses per minute | `PathDrivers` | no group difference attributable to pauses |
| Student-reported understanding of questions (1 to 5, after the exam) | new survey item | at least 4 on average for ESL-mode students |
