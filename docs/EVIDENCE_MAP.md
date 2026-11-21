# Evidence-to-design map

This map links each exam-room mechanic to the research for and against it, using the notes in
[docs/research/](research/README.md) (GAD-T254) and their verification ledger. For each mechanic it says what the code
does now, what the evidence supports, what it argues against, and which proposed ticket would act on it. Where the owner
has decided something the research questions, the decision is recorded as it stands, with the evidence on both sides.
The owner decides. Every ticket listed here is **proposed and unapproved**.

Prepared 2026-11-21 (GAD-T257). Note numbers (01-06) refer to the research notes, and source keys (Wass2003 and so on)
to the entries in them. Nothing here changes code.

| # | Mechanic | Where it lives | Verdict | Proposed tickets |
|---|---|---|---|---|
| 1 | Follow-up moves | `Gadfly/Engine/Moves.cs`, `QuestionPlanner.cs`, `ExamPrompts.cs` | Supported, with changes | GAD-T266, GAD-T267, GAD-T268 |
| 2 | Probing on hesitation | `AnswerDelivery.cs`, `QuestionGenerator.cs` (GAD-T233) | Content cues supported; strain cue not | GAD-T258, GAD-T267 |
| 3 | Recitation check | `RecitationCheck.cs` (GAD-T242) | Source-overlap supported; read-out pattern weak | GAD-T272 |
| 4 | Gaze fade and "look at me" | `wwwroot/js/examiner-gaze.js` (GAD-T240) | Legal, weak as evidence; owner decision (c) | GAD-T261 |
| 5 | Voice strain | `Gadfly.Server/Voice/VoiceStrain.cs` | Not supported; legal risk; owner decision (b) | GAD-T258, GAD-T259 |
| 6 | Time limits and the end-of-answer cue | `SessionPolicy.cs`, `voice-room.js`, `StudentExams.cs` | Time budget supported; two choices questioned | GAD-T263, GAD-T264 |
| 7 | Grading panel, blind human, reconciliation, release | `Gadfly.Server/Grading/`, `BlindGrading.cs` | Supported, with calibration and bias controls | GAD-T265, GAD-T269, GAD-T270, GAD-T273 |
| 8 | Learning Mode hints | `QuestionGenerator.AssistAsync`, `MoveKind.Hint` | Supported only as a fixed ladder, kept apart | GAD-T271 |
| - | Owner decisions (a) to (d) | see below | Recorded, not overridden | GAD-T260, GAD-T261, GAD-T258/T259, GAD-T262 |

## 1. Follow-up moves

**What Gadfly does.** The planner picks a move per turn: Elicit ("what is it?"), Clarify, Probe, CausalReasoning,
Counterexample, ScenarioVariation, Apply, Contradiction (two of the student's own ledger claims), AssumptionTesting,
IndependentExplanation, Synthesize, Close. Each question is written from the student's own words, at most 40 words, and a
guard redrafts any question that reveals expected answer points.

**Evidence for.**
- Writing each follow-up from the student's own answer follows the elenchus's rule of examining only what the answerer
  actually believes (01: Vlastos, Benson's doxastic constraint) and Piaget's clinical method: follow the answer, vary the
  wording, never suggest (01: Piaget1929).
- Counterexample works as Piaget's counter-suggestion. A belief that survives it is better evidence than one stated once
  (01: Piaget1929; Meno 85c). This supports testing a concept through several moves before marking mastery, which
  `MinCorroboratingAnswers` (at least 2) already requires.
- Why and how questions are a reasonable probe of understanding, and they help learning a little (03: Chi1994, Chi1989,
  Bisra2018 g = .55; Dunlosky2013 rates the evidence moderate). Question prompts work at least as well as instructions.
- The never-hint rule in the scored exam is supported (03: Bisra2018; 04: keep the unaided score unaided).
- The fixed opening Elicit question per objective anchors comparability (02: Joughin2010).

**Evidence against or limits.**
- A Contradiction shows that two claims are inconsistent. It does not show which one is wrong (01: Benson1995, Benson2000).
  Either revision is a legitimate resolution, so the judge should score how well the student resolves it, and use the
  rubric and the source to decide correctness.
- A question can hint by its form, if it presupposes the answer, without stating it (01: Piaget1929). The current guard
  checks only for stated answer points.
- A what-if inside the student's own scenario (ScenarioVariation) is extrapolation, which Bloom puts under Comprehension.
  It is not transfer. Only Apply, which uses a situation new to the student, probes transfer (03: Bloom1956,
  BarnettCeci2002). Gadfly measures transfer without resources or hints, which understates it (03: BransfordSchwartz1999).
- Adaptive follow-ups trade comparability for depth (02: Joughin2010). If the number of probes is not reported, how long a
  thread ran can turn into a score without anyone seeing it.
- Explaining to a listener changes performance, usually for the better (01: EricssonSimon1998, Fox2011). Gains late in a
  session are not proof of knowledge the student brought in.

**Proposed.** GAD-T266 (score how a contradiction is resolved, and extend the no-hint guard to presupposition), GAD-T268
(count transfer only from Apply, and label transfer items), GAD-T267 (report probe counts, and set minimum objectives
for summative exams).

## 2. Probing on hesitation

**What Gadfly does.** Before each question, `AnswerDelivery.Read` collects cues from the last answer: a long wait compared
with the student's usual, many hesitations or long pauses, voice strain above the student's own baseline, hedging phrases,
and a recitation flag. The number of cues sets the probing pressure (Steady, Some, Shaky, Recited). The prompt then asks
the examiner to press harder.

**Evidence for.**
- Steering probes by **answer content** is supported: hedging words, vagueness and missing causal links are text, not
  biometric data, and the only deception cues with some support are about content (06: DePaulo2003, "less compelling
  tales"). Pressing on shaky content fits the clinical-interview method (01).
- Comparing a student with their own baseline, not with other students, is the right shape for any delivery signal (01,
  02).

**Evidence against.**
- Slowing down and pausing are normal when people explain aloud (01: Fox2011). Waits and hesitations also change with
  anxiety, accent, second-language use and stuttering (02). They are weak evidence of shaky understanding.
- The baseline comes from the first two answers, and warm-up nerves and easier opening questions confound it (02, 06).
  If a student is most nervous at the start, later answers look calmer than they are.
- The voice-strain cue: see mechanic 5. Note 06 says not to let `strain.vsBaseline` decide who gets harder questions.

**Proposed.** GAD-T258 (drive probing from content, not strain), GAD-T267 (report how many probes each objective got,
so harder probing does not silently lower a score).

## 3. Recitation check

**What Gadfly does.** `RecitationCheck` (GAD-T242) flags an answer whose wording closely follows the source (six-word runs,
overlap at least 0.6, or a copied stretch of 20 words), or that fits a read-out pattern: 60 or more words, fluent, started
after a silent wait of at least 15 seconds and at least twice the student's usual. A flagged answer is left out of the
evidence, the next question asks the student to say it differently, apply it or explain why, and a reviewer sees the flag.
It is never a cheating verdict.

**Evidence for.**
- Answering a recitation by asking the student to explain, extend or apply it is the integrity control the evidence
  supports best. A scripted or AI-written answer does not carry over to a follow-up built from it (06). Content-based cues
  are the only ones with some support (06: DePaulo2003).
- Source overlap is a content measure, so it does not rely on biometrics.
- Not treating a recited answer as evidence of understanding is consistent with scoring justifications rather than
  reproductions (01: NisbettWilson1977 on reconstructions; 03 on transfer).

**Evidence against or limits.**
- The read-out pattern depends on timing and fluency. A long pause followed by a smooth answer is also what careful
  thinkers and some confident speakers do (01: Fox2011; `IntegrityExplanations` already says so to reviewers). Its false
  flag rate is unknown, and timing signals vary with anxiety and language background (02).
- Direct evidence that conversational follow-up reduces cheating was not verified (06: Sotiriadou2020 found but not read).
  Do not claim that Gadfly detects cheating.

**Done.** GAD-T272: the read-out pattern no longer flags a student with an extra-time accommodation (`TimeMultiplier`
above 1), and an exam can switch it off (`SessionPolicy.ReadOutCheck`). `RecitationCheck.Measure` and
`gadfly readout-rate <transcripts>` count how often it fires over session transcripts, including where it was suppressed;
over sessions from honest students every fire is a false flag. The rate stays unknown until it is run on pilot data. The
source-overlap check is kept as is.

## 4. Gaze fade and "look at me" (owner decision (c))

**What Gadfly does.** During an answer, if the student looks away from the examiner for 2 seconds (`GAZE.fadeStartMs`),
the screen fades toward a heavy dim and blur over 3 seconds, with a prompt to look back. A longer look away, a reading
pattern, no face or a second face makes the examiner say "Please look at me while you answer". Each episode goes to
reviewers as low-confidence context. Only notes leave the device, never frames.

**Owner decision (c):** looking away fades the screen and the examiner says "look at me". It stands.

**Evidence for.**
- Unproctored remote exams produce inflated scores (06: Alessio2017, DendirMaxwell2020), so some integrity design is
  justified.
- The EU guidelines give gaze-point tracking for unauthorised material as an example of a use that is not prohibited
  (06). Unlike voice strain, the mechanic is not in the banned category.
- Keeping gaze at capped confidence and Low severity, out of the grade, matches the evidence (06). The current code does
  this.

**Evidence against.**
- Webcam gaze error is about 4 degrees, or 100 to 250 pixels, and worse online (06: Papoutsaki2016,
  SemmelmannWeigelt2018). Reading involves jumps of 7 to 9 letters (06: Rayner2009). A webcam cannot reliably tell
  reading notes from thinking or looking away.
- People look away while thinking, and explaining aloud slows them down (01: Fox2011). A fade that starts after 2 seconds
  will often fire on honest thinking.
- Rates of face and eye detection failure can differ by skin tone and sex (06: YoderHimes2022). Anxious students already
  do worse under proctoring (06: WoldeabBrothen2019). A visual penalty adds time pressure and anxiety, which fall hardest
  on these students (02).

**Proposed.** GAD-T261 keeps the decision and makes it fairer: a longer grace before the fade at the start of each answer,
a switch to suppress it under a declared accommodation, and look-away and fade rates measured by group before anyone
relies on them.

## 5. Voice strain (owner decision (b))

**What Gadfly does.** `VoiceStrain` compares pitch, jitter, loudness, pitch variability, rate and pauses with the
student's first answers. Reviewers see "Possible vocal strain and hesitation". `strain.vsBaseline` of 0.5 or more is a
probing cue (GAD-T233). Blind grading hides strain, and it is never part of the grade.

**Owner decision (b):** voice strain and stress detection drives harder probing, as the owner asked. It stands.

**Evidence for.**
- None found for validity. The only support is procedural: showing strain to reviewers, not graders, as low-confidence
  context against the student's own baseline is the least harmful way to use it (01, 06).

**Evidence against.**
- Voice-stress products perform at chance even at detecting stress (06: Harnsberger2009). In the field they produced
  about nine false flags for each true one (06: Damphousse2007). The NRC found "little or no scientific basis"
  (06: NRC2003). Acoustic stress correlates are inconsistent (06: Giddens2013), and deception cues are weak (06:
  DePaulo2003, BondDePaulo2006).
- **EU AI Act.** Voice is biometric data (Art. 3(34); guidelines para 251), and "emotion" is read broadly (para 247). The
  Commission's 2025 guidelines give detecting "emotional arousal and anxiousness" during online exams as prohibited
  emotion recognition in education (para 255). Integrity and fraud prevention do not count as a safety reason (para 258).
  Jitter and pitch are standard stress features, so calling the output "strain" and using it to steer probing looks like
  inferring an inner state (06).
- Anxiety, accent and stuttering change these features (02). Making anxious students face harder questions adds pressure
  where the evidence says to reduce it.
- `docs/PRIVACY.md` says "Nothing infers emotion". Note 06 flags that statement as exposed while the strain readout exists.

**Proposed.** GAD-T258 (probing driven by content, not strain) and GAD-T259 (off by default for EU tenants and
EU-resident students, raw acoustic deltas instead of "strain", and the PRIVACY.md wording fixed). Both conflict with
decision (b) and wait for the owner.

## 6. Time limits and the end-of-answer cue

**What Gadfly does.** Each session has a time budget (20 minutes by default), which `TimeMultiplier` can extend as an
accommodation (up to 4 times). It also has a turn cap and a per-concept turn cap. The clock starts at the first question,
and keeps running while the exam is paused because a microphone or camera stopped. In the voice room, an answer is
submitted after `EndOfAnswerSeconds` of silence (4 seconds by default).

**Evidence for.**
- A bounded session, and caps per concept that spread time across objectives, support reliability, which comes from
  sampling many objectives (02: Wass2003, VanderVleuten2010).
- Starting the clock only at the first question, and extending time as an accommodation, are supported (02: Joughin2010
  on adjustments; anxiety reduction improves validity, Hembree1988, vonderEmbse2018).

**Evidence against.**
- Note 02 names both current choices for re-examination. A clock that runs during a pause the student did not cause, and
  a fixed 4-second silence, add time pressure that anxious, second-language and disfluent speakers feel most. Explaining
  aloud is normally slow (01: Fox2011).

**Proposed.** GAD-T263 (the clock stops while the exam is paused, with a cap on total pause time) and GAD-T264 (the
end-of-answer silence adapts to the student's own pauses, with a floor and a ceiling, and lengthens under an
accommodation).

## 7. Grading panel, blind human, reconciliation and release

**What Gadfly does.** After a session ends, responses that need transcript verification wait for a human. Then a panel
of models grades each objective (`AiGradingJob`), a human grades blind to the AI (`BlindGrading`), disagreements go to
reconciliation, and a human approves. The prompts fence student text as untrusted and tell graders never to penalize
grammar, accent or fluency. The AI grade is never final.

**Evidence for.**
- Panels drawn from different model families reduce single-model and same-family bias, and matched or beat a single large
  judge (05: Verga2024).
- Blind human grading, reconciliation and human approval are supported. Humans who saw an LLM verdict changed their own
  choice about a third of the time (05: Zheng2023). EU AI Act Annex III 3(b) and Article 14 require human oversight for
  AI that evaluates learning outcomes (05).
- Human marking of open answers is itself unreliable (05: MeadowsBillington2005; human QWK 0.61 to 0.85 in Shermis2014),
  so the right response to disagreement is reconciliation, not an automatic override either way.
- Retrieval practice beats restudying (03: Roediger2006, Rowland2014 g = 0.50). The exam can be a learning event, and
  repeated attempts make sense, if feedback is released.

**Evidence against or gaps.**
- No source tests LLM grading of spoken, transcribed Socratic answers (05). Gadfly's real agreement figures can only come
  from its own double-marked pilot (`docs/PILOT.md`).
- Known judge biases need controls that are not all in place yet: position (randomise order), verbosity (length earns
  nothing; quote evidence before the score), self-preference (do not let the family that wrote the questions dominate),
  and language background (05: Zheng2023, Wang2024, Panickssery2024, Liang2023).
- Speech recognition is the largest documented fairness risk. Word error rates differ by dialect and for people who
  stutter (02: Koenecke2020, Mujtaba2024), and a transcription error can be scored as wrong content.
- Without feedback, repeated attempts give about no benefit when students mostly fail (03: Rowland2014, Dunlosky2013).
  Gains transfer weakly to objectives that were not tested (03: PanRickard2018).

**Proposed.** GAD-T269 (bias controls in grading prompts, plus padded-answer and fluency checks), GAD-T270 (calibration
thresholds per exam: QWK at least .70, within .10 of human-human agreement, |SMD| under .15, plus triggers for
reconciling single answers), GAD-T265 (word error rate by group, and low-confidence segments marked for graders), GAD-T273
(feedback per objective on released results).

## 8. Learning Mode hints

**What Gadfly does.** In Learning Mode only, Hint and GuidedExplanation moves teach, and everything after them is tagged
as assisted evidence. The LLM writes each hint (`QuestionGenerator.AssistAsync`). Examination Mode refuses to teach.

**Evidence for.**
- Keeping hints out of the scored exam, and in a separate, labelled mode, is supported (04: Vygotsky; Haywood & Lidz;
  Stad2019). Help-driven improvement in practice is expected, not suspicious.

**Evidence against.**
- If the amount of help is ever reported or scored, everyone must get the same ladder: fixed, written by the instructor,
  given only after an inadequate answer (04: Caffrey2008). Free-form LLM hints are interactionist, so prompt counts
  cannot be compared between students. Gain and help-needed scores are confounded with starting level (04: De Beer 2006),
  and any "learning potential" number is experimental.

**Proposed.** GAD-T271 (a fixed hint ladder per objective, where the LLM picks when to give a hint, never what it says,
reported beside the unaided score).

## Owner decisions the research questions

The owner has decided these. This map records each one and does not override it. The tickets that would change them are
proposed and wait for the owner.

### (a) Voice and camera only, no typed answers

**Decision:** exams are voice plus camera only, with no text mode ("there is no text only mode"), to stop cheating.

- **For:** unproctored remote exams are inflated (06). A spoken answer, built on by follow-ups from the student's own
  words, is harder to paste from another source than a typed one. Voice also brings the delivery and recitation context
  reviewers use (mechanic 3).
- **Against:** note 02 supports a typed-answer accommodation that instructors grant per student. Joughin2010 recommends
  alternative arrangements for undue anxiety and adjustments for speech or hearing impairment. ASR error is worst for some
  dialects and for people who stutter (Koenecke2020, Mujtaba2024). The strongest integrity control the evidence supports
  is the Socratic follow-up itself (06), and that works on typed answers too. With the camera still on, an accommodation
  keeps most of the integrity design. Under the current rule, a student who cannot use the voice exam has no route.
- **Ticket:** GAD-T260, proposed, conflicts with decision (a). The default would stay voice-only.

### (b) Voice strain drives harder probing

**Decision:** voice strain and stress detection drives harder probing, as the owner asked.

- **For:** pressing a student whose answer comes out shaky can bring out whether the concept is really understood
  (mechanic 2). Comparing with the student's own baseline is the least unfair way to read any delivery signal.
- **Against:** voice-stress measures have no demonstrated validity (06: Harnsberger2009, Damphousse2007, NRC2003). The EU
  guidelines name the detection of emotional arousal and anxiety during exams as prohibited emotion recognition in
  education (para 255). Jitter and pitch are stress features of a biometric signal. Anxious, accented and disfluent
  students are the ones most likely to be pressed (02). The same aim can be met from answer content, which is outside
  the ban.
- **Tickets:** GAD-T258 and GAD-T259, proposed, both conflict with decision (b).

### (c) Gaze fade and "look at me"

**Decision:** looking away fades the screen and the examiner says "look at me".

- **For** and **against:** see mechanic 4. The use is legal, but the signal is coarse, often fires on thinking, and may
  differ by group.
- **Ticket:** GAD-T261, proposed. It keeps the decision and adds a grace period, an accommodation switch and group
  measurement.

### (d) Clear scores holds the attempt out of grading

**Decision:** Clear scores holds the attempt out of grading. `DeskService.ClearScoresAsync` deletes the grades, reports,
reconciliations, approvals and appeals, un-releases results, and parks each ended attempt at Examination In Progress, so
the grading sweeper does not grade it again. The attempt and transcript stay, and it still counts as used. The action is
audited.

- **For:** an attempt an administrator has cleared should not be graded again by the AI, unseen, seconds later. A human
  decides whether and when it is graded, as Article 14 human oversight requires (05). The audit row records who cleared
  it.
- **Against:** this is an Annex III 3(b) use, and logging and record-keeping duties apply (05). Hard-deleting appeals,
  reconciliations and approval records loses the oversight trail that would show how a grade was reached and then
  removed. Clearing also withdraws the released feedback that makes a retake worthwhile (03: Rowland2014), while the
  attempt still counts against the student's limit.
- **Ticket:** GAD-T262, proposed. It keeps the decision, archives the records instead of deleting them, and tells the
  student the attempt still counts.

## Proposed tickets

All were filed by GAD-T257 as proposed and unapproved. None changes code until the owner approves it.

| Ticket | Mechanic | Change | Conflicts with an owner decision |
|---|---|---|---|
| GAD-T258 | 2, 5 | Adaptive probing from answer content, not voice strain | Yes, (b) |
| GAD-T259 | 5 | Voice strain off by default for the EU, relabelled as raw acoustic deltas, PRIVACY.md fixed | Yes, (b) |
| GAD-T260 | (a) | Instructor-granted typed-answer accommodation | Yes, (a) |
| GAD-T261 | 4 | Gaze fade: thinking grace, accommodation switch, rates by group | Touches (c) |
| GAD-T262 | (d) | Clear scores archives grading records instead of deleting them | Touches (d) |
| GAD-T263 | 6 | Exam clock stops while the exam is paused | No |
| GAD-T264 | 6 | End-of-answer silence adapts to the student's own pauses | No |
| GAD-T265 | 7 | Word error rate by group, low-confidence segments marked for graders | No |
| GAD-T266 | 1 | Score how a contradiction is resolved; no-hint guard checks presupposition | No |
| GAD-T267 | 1, 2 | Report probe counts per objective; minimum objectives for summative exams | No |
| GAD-T268 | 1 | Credit transfer only from Apply; label transfer items | No |
| GAD-T269 | 7 | Grading panel bias controls | No |
| GAD-T270 | 7 | Calibration thresholds for the panel, and reconciliation triggers | No |
| GAD-T271 | 8 | Fixed, instructor-written hint ladder in Learning Mode | No |
| GAD-T272 | 3 | Measure false flags from the read-out pattern; suppress it under an accommodation | No |
| GAD-T273 | 7 | Feedback per objective on released results | No |

## What the evidence does not let Gadfly claim

- That Gadfly "detects cheating" or "detects stress" (06).
- That a Socratic oral exam is a more valid measure than other formats, or that the rate of contradictions detects
  cheating (01).
- That oral scores are free of anxiety or accent effects (02: Huxham2012 is too small to show this).
- Any expected agreement figure for the grading panel before Gadfly's own pilot (05).
- Large learning gains from retakes, especially on complex material (03: vanGogSweller2015, KarpickeAue2015).
