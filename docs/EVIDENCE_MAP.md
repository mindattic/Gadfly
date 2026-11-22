# Evidence-to-design map

This map links each exam-room mechanic to the research for and against it, using the notes in
[docs/research/](research/README.md) (GAD-T254) and their verification ledger. For each mechanic it says what the code
does now, what the evidence supports, what it argues against, and which proposed ticket would act on it. Where the owner
has decided something the research questions, the decision is recorded as it stands, with the evidence on both sides.
The owner decides. The tickets GAD-T257 filed were proposed and unapproved when it was written; the owner has since
approved every one of them and all are done.

Prepared 2026-11-21 (GAD-T257). Checked against the code on main again on 2026-11-22 (GAD-T454, after ElenchusAtScale
Appendix A, corrections 34, 42 and 43, and Section 6.7, which found this map describing behaviour the code no longer
had). Note numbers (01-06) refer to the research notes, and source keys (Wass2003 and so on) to the entries in them.
Nothing here changes code.

| # | Mechanic | Where it lives | Verdict | Tickets (all done) |
|---|---|---|---|---|
| 1 | Follow-up moves | `Gadfly/Engine/Moves.cs`, `QuestionPlanner.cs`, `ExamPrompts.cs` | Supported, with changes | GAD-T266, GAD-T267, GAD-T268 |
| 2 | Probing on hesitation | `AnswerDelivery.cs`, `QuestionGenerator.cs` (GAD-T233) | Content cues supported; strain cue not | GAD-T258, GAD-T267 |
| 3 | Recitation check | `RecitationCheck.cs` (GAD-T242) | Source-overlap supported; read-out pattern weak | GAD-T272 |
| 4 | Gaze fade and "look at me" | `wwwroot/js/examiner-gaze.js` (GAD-T240) | Legal, weak as evidence; owner decision (c) | GAD-T261, GAD-T305 |
| 5 | Voice strain | `Gadfly.Server/Voice/VoiceStrain.cs` | Not supported; legal risk; owner decision (b) reversed 2026-11-21 | GAD-T258, GAD-T259 |
| 6 | Time limits, the end-of-answer cue and the judging wait | `SessionPolicy.cs`, `SocraticSession.cs`, `voice-room.js`, `ExaminerFiller.cs` | Time budget supported; the two questioned choices changed | GAD-T263, GAD-T264, GAD-T290, GAD-T409 |
| 7 | Grading panel, blind human, reconciliation, release | `Gadfly.Server/Grading/`, `BlindGrading.cs` | Supported, with calibration and bias controls | GAD-T265, GAD-T269, GAD-T270, GAD-T273, GAD-T285, GAD-T405 |
| 8 | Learning Mode hints | `HintLadders`, `QuestionGenerator`, `MoveKind.Hint` | Supported only as a fixed ladder, kept apart | GAD-T271 |
| 9 | Text-style estimate | `Gadfly.Sources/TextStyleAssessor.cs` (GAD-T59) | Not supported (a detector in all but name); switched off 2026-11-22 | GAD-T284, GAD-T285, GAD-T412 |
| - | Owner decisions (a) to (d) | see below | Recorded, not overridden; (b) reversed by the owner 2026-11-21; (a) narrowed by the owner 2026-11-22 (typed answers as a granted accommodation) | GAD-T258/T259, GAD-T260, GAD-T261, GAD-T262 |

## 1. Follow-up moves

**What Gadfly does.** The planner picks a move per turn: Elicit ("what is it?"), Clarify, Probe, CausalReasoning,
Counterexample, ScenarioVariation, Apply, Contradiction (two of the student's own ledger claims), AssumptionTesting,
IndependentExplanation, Synthesize. Close is a move kind the planner never asks: an objective ends when the evidence
rules settle it or a limit closes it (ElenchusAtScale, correction 32). Each question is written from the student's own words, at most 40 words, and a
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
- A fixed opening per objective anchors comparability (02: Joughin2010). Gadfly fixes the opening move (Elicit) and the
  rubric, but each opening question is newly worded by the panel, so the stronger condition, the same opening question
  for every student, was not met (ElenchusAtScale, correction 31). Since GAD-T436 the instructor can fix it: an approved
  opening question per objective is asked word for word to every student (with no panel call), and only objectives without
  one get a newly worded opening. Approval runs the leak guard against the concept's rubric and fails closed.

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
**Done.** GAD-T266 (a contradiction is settled by how well the student resolves it, either revision being legitimate,
and the no-hint guard also catches a question whose form presupposes an answer point), GAD-T268 (transfer is credited
only from Apply, and each Apply question carries a transfer label), GAD-T267 (each objective's report shows how many
follow-ups it received, and `SessionPolicy.MinSummativeObjectives`, 4 by default, sets the fewest objectives an
examination must cover for summative use). The figure of four is a setting, not an estimate: no source gives it as
enough for any target reliability, and only a double-marked pilot can (ElenchusAtScale, correction 38).

**Blueprint safeguards (GAD-T421 audit).**
- No introspection questions (GAD-T445, prompt set v9; ElenchusAtScale 2.3.2). People's reports of how they reached an
  answer may be reconstructions (01: NisbettWilson1977), so a question such as "how did you arrive at that?" asks for
  process information the student may never have attended to. The question prompt and the leak guard forbid such
  questions, `QuestionGenerator.IsIntrospective` drops a draft that asks one before any guard call (it is redrafted, never
  asked), and the judge and the analysis score whether a justification is correct and connected, never whether it reports
  how the student thought.
- Claim retractions (GAD-T441, prompt set v10; M6, 3.4). Only the student's standing assertions are premises of a
  contradiction. The analysis (or, without one, the claims prompt) records an earlier claim the student explicitly
  withdraws ("I was wrong earlier"); `ClaimLedger.Retract` matches it to the ledger by word overlap, with no extra panel
  call. The withdrawn claim stays on record (marked withdrawn in the earlier-claims list the judges see, and on the review
  page) but is never checked against new claims, and a contradiction resting on it is not put to the student; one already
  put to them is still judged on how they resolved it.
- Contradiction detector honesty (GAD-T440; M7, 3.4, 4.4). The detector is a filter, so its silence is reported as "no
  contradiction detected", never "consistent": each objective's report states one of "no contradiction detected",
  "contradiction resolved", "contradiction withdrawn by the student", "contradiction pending" or "contradiction not
  resolved". Candidate pairs are wider (same concept at word overlap 0.1, another concept at 0.25, at most 4 per claim and
  8 per answer), all checked in the one parallel round of GAD-T404, so the wait per answer is still one check; no
  embeddings are computed on that path. The panel's confidence is used: a split panel's finding (half or less) is shown
  to the reviewer as a possible contradiction and is never put to the student. Every pair compared is kept in the claim
  ledger with its overlap and verdict, and `gadfly contradiction-audit <ledgers>` draws a stratified sample of claim
  pairs (flagged, possible, checked and clear, never checked) for a person to label, then estimates the false-flag and
  miss rates from the labels. The rates are unknown until a pilot is labelled.
- Apply novelty is checked, not only asked for (GAD-T442; M5, 5.2.2). Each Apply question's situation words (its content
  words, less the concept's and objective's own terms and the usual question wording) are compared with every sentence of
  the source passages it was grounded in and with each of the student's earlier answers (`ApplyNovelty.Check`). When one
  of them holds half or more of those words (at least three), the situation is not new: the turn records the match, the
  reviewer's "why this question" says so, and the answer is credited as extrapolation (application), never as transfer,
  so it cannot show transfer or lift an objective to Level 5. Word overlap only, no model call, so it adds no wait; the
  question is not redrafted for it, since a redraft is another panel round trip. A paraphrased reuse in other words can
  pass the check.

## 2. Probing on hesitation

**What Gadfly does.** Before each question, `AnswerDelivery.Read` collects cues from what the last answer said and how
it was judged (GAD-T258): two or more hedging phrases, two or more vague phrases ("stuff", "somehow", "and so on"), no
reason or example where the move asked for one (Probe, CausalReasoning, ScenarioVariation, AssumptionTesting,
IndependentExplanation, Apply, Synthesize, Counterexample, Contradiction), a judged overall score below 0.6 (not yet
positive evidence), the response analysis's evidence against understanding (a misconception, or contradicting evidence)
or unclear meaning, and the recitation flag. One cue sets the probing pressure to Some, two or more to Shaky, and a
recitation flag to Recited. The prompt (since prompt set version 8; version 9 now) then asks the examiner to press harder.

Nothing from the voice steers it: not the voice comparison (pitch, jitter, loudness, `strain.vsBaseline`), and not the
wait before the first word, the hesitation count or long pauses. The owner reversed decision (b) on 2026-11-21 (mechanic
5). The wait and the hesitations are timing, not emotion, but they were dropped as cues too: the evidence below calls them
weak, they move with anxiety, accent, second-language use, stuttering and accommodations, and keeping them would leave a
voice-derived steer that EU students (GAD-T283) do not get. They stay recorded with the answer, and reviewers see them as
raw numbers where voice measurements are on; they never reach the question prompt. The read-out pattern (mechanic 3) still
uses timing, but only to decide whether an answer is accepted as the student's own, under its own switch and
accommodation rule (GAD-T272); the Recited probe follows from that decision.

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
- Content cues are not neutral either: a second-language speaker may hedge or use plainer words, and the vague-phrase and
  reason checks are English word lists. They only steer the next question, never the score.

**Done.** GAD-T258 (approved by the owner on 2026-11-21): probing driven by content and the judges' reading, not by the
voice or its timing. This closes the gap ElenchusAtScale recorded as correction 40 (with voice measurements off, timing
still steered probing): on main no voice-derived cue reaches the question prompt, with measurements on or off. GAD-T267:
each objective's report shows how many follow-ups it got, so harder probing does not silently lower a score.

## 3. Recitation check

**What Gadfly does.** `RecitationCheck` (GAD-T242) flags an answer whose wording closely follows the source (six-word runs,
overlap at least 0.6, or a copied stretch of 20 words), or that fits a read-out pattern: 60 or more words, fluent, started
after a silent wait of at least 15 seconds and at least twice the student's usual. The read-out pattern is voice-derived (timing and fluency), so it is not applied when voice measurements are off
(GAD-T283); source overlap is. A flagged answer is left out of the
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
- The read-out pattern depends on timing and fluency. A long pause followed by a smooth answer has innocent explanations
  too (planning an answer before speaking, a confident speaker), and `IntegrityExplanations` tells reviewers so; no
  verified source ties the pattern to reading aloud. Explaining aloud is slow in general (01: Fox2011), which is about
  time, not about this pattern. Its false flag rate is unknown, and timing signals vary with anxiety and language
  background (02).
- Direct evidence that conversational follow-up reduces cheating was not verified (06: Sotiriadou2020 found but not read).
  Do not claim that Gadfly detects cheating.

**Done.** GAD-T272: the read-out pattern no longer flags a student with an extra-time accommodation (`TimeMultiplier`
above 1), and an exam can switch it off (`SessionPolicy.ReadOutCheck`). `RecitationCheck.Measure` and
`gadfly readout-rate <transcripts>` count how often it fires over session transcripts, including where it was suppressed;
over sessions from honest students every fire is a false flag. The rate stays unknown until it is run on pilot data. The
source-overlap check is kept as is.

## 4. Gaze fade and "look at me" (owner decision (c))

**What Gadfly does.** During an answer, in any exam whose integrity level is not Off, if the student looks away from the
examiner for 2 seconds (`GAZE.fadeStartMs`; 6 seconds early in an answer, see GAD-T261 below), the screen fades toward a
heavy dim and blur over 3 seconds, with a prompt to look back. A longer look away, a reading-like pattern, no face or a
second face makes the examiner say so out loud ("Please look at me while you answer", and a variant for each). Each
episode goes to reviewers as low-confidence context (confidence at most 0.4, severity at most Low), hidden from a blind
grader until their score is in (GAD-T285). Only notes and numbers leave the device, never frames.

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
- Looking away has many causes besides reading: recalling, planning an answer, a noise in the room, a habit, a
  disability, cultural norms about eye contact. No source in the verified research addresses gaze aversion while
  thinking, so this map does not claim that looking away *is* thinking, only that a webcam cannot tell it from reading
  (06: Rayner2009, above), and that explaining aloud is slow (01: Fox2011, which concerns time, not gaze; ElenchusAtScale
  Appendix A, corrections 34 and 42). A fade that starts after 2 seconds will therefore often fire on an honest student.
  The reviewer wording says the same (GAD-T446).
- Rates of face and eye detection failure can differ by skin tone and sex (06: YoderHimes2022). Anxious students already
  do worse under proctoring (06: WoldeabBrothen2019). A visual penalty adds time pressure and anxiety, which fall hardest
  on these students (02).

**Done (GAD-T261).** The decision stands and is fairer:
- **Grace at the start of an answer.** A look away that begins in the first 10 seconds of an answer
  (`GAZE.thinkingGraceMs`) starts the fade only after 6 seconds (`GAZE.thinkingFadeStartMs`), not 2, and the spoken
  request moves later by as much. The code calls it "thinking grace"; it is a precaution against fading the screen on a
  student gathering an answer, not a finding that such a look is thinking.
- **Accommodation.** An instructor can declare the eye-contact accommodation on any assignment (Exams, Assignments,
  "Declare accommodation"; audited). The screen then never fades and the examiner never asks for eye contact or remarks
  on reading-like eyes. Face presence and a second face are still checked. Episodes are still noted, marked as
  accommodated and kept at Info.
- **Rates by group.** Analytics shows look-aways, reading-like episodes, fades, time faded and spoken requests per 10
  answering minutes, by accommodation or by a grouping the institution pastes in (used in memory, never stored). Groups
  under 5 students are withheld, and rates that differ by more than a quarter between groups are flagged. Until they
  have been compared, eye-contact episodes are context only.

**Done (GAD-T305): fewer false look-aways, and an honest quality score.** The decision still stands; the mechanic now
measures better and says how far to trust it:
- **Gaze vector, not head direction alone.** Iris position plus head pose, followed to the camera's plane and mapped onto
  the page by a five-dot calibration (get-ready page or "Set up eye contact again"). Leaning or turning the head while
  looking at the examiner no longer counts as looking away (in a synthetic check, a 10 cm lean read as a 10 degree
  deviation by head direction and under 1 degree by the gaze vector). The examiner's region is her stage as shown on screen
  plus a margin that grows with the measured accuracy and noise, and a look-away needs both estimates to agree.
- **Measured, not claimed.** Each calibration's accuracy is its leave-one-out error in degrees, checked again by a look at
  the examiner in the room; worse than 6 degrees gets one retry and is then not used (the old head-direction check is the
  fallback). This keeps within the webcam evidence above: the goal is fewer false look-aways, not precision.
- **Quality with every note.** Each eye-contact episode carries the session's tracking quality (0..100 from accuracy,
  steadiness, tracking coverage, lighting and framing, frame rate; at most 50 without a calibration). Reviewers read it in
  the note ("Eye-tracking quality this session: fair (55/100; head and eye direction only)"); a poor score keeps a
  look-away or reading note at Info, and the confidence sent scales with it, still capped at 0.4.
- **Setup advice.** The get-ready page checks lighting (including a bright light behind the student), camera angle,
  distance, how steadily the eyes can be followed (glare on glasses) and device speed, with plain advice. It never blocks
  the exam. Thinking grace and the accommodation are unchanged; under the accommodation there are no calibration dots.
- **Still to do.** The thresholds (quality bands, margins, the 6 degree cut-off) are reasoned from the literature and a
  synthetic face, not validated on real students; compare flag rates by quality band and by group once pilot data exists.

## 5. Voice strain (owner decision (b), reversed 2026-11-21)

**What Gadfly does.** `VoiceStrain` compares pitch, jitter, loudness, pitch variability, rate and pauses with the
student's first answers. Reviewers see the raw differences ("voice measurements against this student's first answers:
pitch +2.1 semitones*, ..."), never a named state (GAD-T259). Since GAD-T258 none of it steers probing, anywhere:
`strain.vsBaseline` is recorded but is no longer a cue, and probing comes from the answer's content (mechanic 2). Blind
grading hides it, and it is never part of the grade. Since GAD-T259 it is off by default for EU/EEA organizations and
students marked as living in the EU/EEA, and then nothing about the voice is measured or read (GAD-T283, which also stops
the read-out pattern there).

**Owner decision (b):** originally, voice strain and stress detection drives harder probing, as the owner asked. **The
owner reversed it on 2026-11-21** by approving GAD-T258: the measurements stay (where on) as raw numbers for reviewers,
and probing comes from what the answer says.

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
- `docs/PRIVACY.md` said "Nothing infers emotion". Note 06 flagged that statement as exposed while the strain readout exists;
  GAD-T259 corrected it.

**Done.** GAD-T259 (approved by the owner): off by default for EU tenants and EU-resident students, raw acoustic deltas
instead of "strain", and the PRIVACY.md wording fixed. GAD-T258 (approved by the owner on 2026-11-21, reversing decision
(b)): the voice comparison no longer drives probing anywhere, nor do the wait before the first word or the hesitation
count; they are recorded and shown to reviewers as raw numbers where voice measurements are on.

## 6. Time limits, the end-of-answer cue and the judging wait

**What Gadfly does.** Each session has a time budget (20 minutes by default), which `TimeMultiplier` can extend as an
accommodation (up to 4 times). It also has a turn cap and a per-concept turn cap. The clock starts when the examiner
begins the first question (GAD-T239), not while the room connects.

- **Pauses (GAD-T263).** While the exam is paused because the microphone, the camera or the examiner's connection was
  lost, the clock stands still: the paused time is added back to the server's clock, up to 10 minutes or half the time
  budget, whichever is less, per exam (`SocraticSession.PauseAllowance`). The cap exists because the browser reports the
  pause, so a microphone pulled on purpose cannot buy unlimited time. Any answer ends an open pause. Staff can also pause
  an exam from the live monitor, which stops the clock separately (GAD-T299).
- **End of an answer (GAD-T264).** In the voice room an answer is handed in after a silence that adapts to the student's
  own pauses: it starts at `EndOfAnswerSeconds` (4 seconds by default) and, once three or more pauses within answers have
  been seen, becomes 1.5 times the student's 80th-percentile pause, never below that floor and never above
  `EndOfAnswerMaxSeconds` (12 by default). It only ever lengthens; both bounds lengthen with the extra-time accommodation.
- **The judging wait (GAD-T290, GAD-T409).** After every answer, while the panel judges it and writes the next question,
  the examiner says one short neutral line if the question is not ready within 2 seconds ("Let me think about that"), and
  one more after 20 seconds, over a quiet page-turn sound. The lines are fixed, picked at random and never about the
  answer, so they carry no hint of how it was judged (mechanic 1's never-hint rule), and they are never part of the
  transcript. The microphone stays shut throughout. The exam clock keeps running during this wait: the time the panel
  takes counts against the student's budget.

**Evidence for.**
- A bounded session, and caps per concept that spread time across objectives, support reliability, which comes from
  sampling many objectives (02: Wass2003, VanderVleuten2010).
- Starting the clock only at the first question, and extending time as an accommodation, are supported (02: Joughin2010
  on adjustments; anxiety reduction improves validity, Hembree1988, vonderEmbse2018).

**Evidence against.**
- Note 02 named two choices for re-examination: a clock that ran during a pause the student did not cause, and a fixed
  4-second silence. Both add time pressure that anxious, second-language and disfluent speakers feel most, and explaining
  aloud is normally slow (01: Fox2011). Both are changed (below).
- The judging wait is time the student does not control, and it still runs the clock. How long it lasts depends on the
  panel's speed, not on the student, so on a slow day every student loses some answering time. No source measures the
  effect; it is noted here as the remaining time pressure of the same kind.

**Done.** GAD-T263 (the clock stops while the exam is paused, with a cap on total pause time) and GAD-T264 (the
end-of-answer silence adapts to the student's own pauses, with a floor and a ceiling, and lengthens under an
accommodation). GAD-T290 and GAD-T409 fill the judging wait with neutral speech and sound; they do not stop the clock.

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
- Known judge biases need controls: position (randomise order), verbosity (length earns nothing; quote evidence before
  the score), self-preference (do not let the family that wrote the questions dominate), and language background (05:
  Zheng2023, Wang2024, Panickssery2024, Liang2023). GAD-T269 and GAD-T430 (below) cover position, verbosity and fluency;
  self-preference is not separately controlled.
- Speech recognition is the largest documented fairness risk. Word error rates differ by dialect and for people who
  stutter (02: Koenecke2020, Mujtaba2024), and a transcription error can be scored as wrong content. Since GAD-T405
  (owner decision 2026-11-22) a spoken answer is judged as heard, with no read-back for the student to confirm or correct
  and no later fix or change, because a second, considered pass changes the Socratic exchange. That removes the student's
  own check on a mishearing, so this risk now rests on the graders' instruction to ignore transcription errors and on the
  human reviewer, who sees the recognizer's low-confidence words and its advice to check (recorded on each answer). It
  also leaves the word error rate comparison of GAD-T265 without new references: its reference is a transcript the
  student checked, and no spoken answer taken since GAD-T405 has one. A human listener's transcript of a sample of
  answers is needed before it can be run on them (ElenchusAtScale, Appendix A, correction 43; `docs/PILOT.md`).
- Without feedback, repeated attempts give about no benefit when students mostly fail (03: Rowland2014, Dunlosky2013).
  Gains transfer weakly to objectives that were not tested (03: PanRickard2018).

**Done.** GAD-T269 (bias controls in the grading prompt: length, repetition and restatement earn nothing, fluency is never
penalized, evidence and a quote come before each level, rubric criteria and level order are shuffled per call, and
`PanelBiasCheck` probes the panel with padded and fluency-paraphrased answers), GAD-T270 (a panel is accepted per exam
only after a blind double-marked calibration set meets QWK at least .70, within .10 of human-human agreement and |SMD|
under .15, plus triggers that send a single objective to reconciliation when the panel is split or the score sits on a
grade boundary; the boundary trigger is a precaution by analogy, not a finding, ElenchusAtScale correction 41), GAD-T265
(words the recognizer was unsure of are named to the panel and marked for the human grader; word error rate by group in
Analytics, limited as above), GAD-T273 (released results say, objective by objective, what was missing).

**Done.** GAD-T285 (approved by the owner; found by GAD-T282), two gaps in the oversight above:
- **Integrity context waits for the blind score.** The blind grader used to see gaze episodes, recitation flags and
  the text-style event while grading, although the research notes say to show integrity context only after the
  content-based score (06) and a flag primes a judgment of the student rather than of the answer (05: Zheng2023).
  While a session's grading is blind, the review page, its CSV and Markdown exports and the review list now give a
  grader no integrity event of any kind (gaze and camera, browser, recitation, text style); the list shows "hidden"
  in place of a flag count. They appear once the human score is submitted. Owners and Admins, who do not score,
  keep the full view. A policy that turns blind grading off shows them from the start, as it does the AI's grade.
- **Separation of duties.** One person could score, reconcile and approve the same grade, which leaves the human
  oversight Article 14 asks for to a single judgment. `GradingService` now refuses reconciliation and approval by
  anyone who entered a human score on that grade (audited as `grade.separation-of-duties.refused`), on the page and
  the endpoints alike. An organization with one grader can turn on "Allow a single grader" (off by default, the
  change audited); every act it lets through is audited as `grade.separation-of-duties.waived`.

**Done.** GAD-T430 (approved by the owner; found by GAD-T421, ElenchusAtScale Sections 3.8 and 5.4.2, M1, M12): the
grading panel is kept apart from the examiner's exam-time judgments. The stance recorded on each answer during the exam
(supports, contradicts, misconception, uncertain) and its note no longer reach the grading prompt; they only raise the
"contradicting evidence" flag for the human reviewer. The answers are listed to the panel in a fresh random order on
every call, as the rubric criteria and levels already were, but each keeps a stable number, so a citation always
resolves to the same answer.

## 8. Learning Mode hints

**What Gadfly does.** In Learning Mode only, Hint and GuidedExplanation moves teach, and everything after them is tagged
as assisted evidence. Since GAD-T271 each hint is the next rung of the objective's fixed, instructor-written ladder
(`HintLadders`; a fixed default ladder when the instructor wrote none): the planner decides when, after a weak answer or a
misconception, and no model writes or rewords the text. Reports give the hints given beside the unaided level, and no
gain or learning-potential score is computed. Examination Mode refuses to teach.

**Evidence for.**
- Keeping hints out of the scored exam, and in a separate, labelled mode, is supported (04: Vygotsky; Haywood & Lidz;
  Stad2019). Help-driven improvement in practice is expected, not suspicious.

**Evidence against.**
- If the amount of help is ever reported or scored, everyone must get the same ladder: fixed, written by the instructor,
  given only after an inadequate answer (04: Caffrey2008). Free-form LLM hints are interactionist, so prompt counts
  cannot be compared between students. Gain and help-needed scores are confounded with starting level (04: De Beer 2006),
  and any "learning potential" number is experimental.

**Done.** GAD-T271 (approved by the owner): a fixed hint ladder per objective, where the planner picks when to give a hint,
never what it says, reported beside the unaided score.

## 9. Text-style estimate

**What Gadfly does.** `TextStyleAssessor` (GAD-T59) asks the panel how likely it is that a spoken answer "was read aloud
from AI-generated or written text rather than composed while speaking," compared with the same student's other answers
(at least two earlier answers, answers of 120 characters or more). The providers' estimates are averaged and stored with
the answer's exam-time judgment. At `TextStyleAssessor.ReviewerThreshold` (0.7) or more it is also recorded as a
low-severity integrity event (`text-reads-as-written`). It never enters the score and never changes what the examiner
asks. Found by GAD-T282 (dissertation Appendix A, correction 37), it was missing from this map.

**Switched off (GAD-T412, 2026-11-22).** Every live session now sets `AssessTextStyle = false`
(`SessionOrchestrator`), so no answer is assessed, no estimate is stored and no new `text-reads-as-written` event is
recorded, on spoken or typed answers. The owner turned it off to take one panel call off the critical path of every
answer, favouring a fluid conversation for now; it can come back later behind a setting. The code, the labels below and
the measuring command remain, so estimates and events recorded before the switch still show on the review page with
their "unvalidated" label and can still be counted. The rest of this section describes it as it ran, and applies again
if it is turned back on.

**Evidence for.**
- Comparing a student with their own other answers is the least unfair form such a signal could take: it does not compare
  them with a population whose style may differ by first language.
- Shown only to a human, with its ceiling stated, it informs rather than acts (06: integrity context informs a reviewer,
  it is not a finding).

**Evidence against.**
- It is a detector of machine-written text in all but name, applied to transcribed speech. Seven widely used detectors
  labelled more than half of a set of essays by non-native English writers as machine-generated (05: Liang2023, average
  false-positive rate 61.3%), and detectors as a class were found neither accurate nor reliable (06: WeberWulff2023). The research
  notes recommend that they never be used as integrity signals (05).
- No source tests a language-model estimate of this kind on transcribed speech, or within a student's own answers. Speech
  recognition errors and accent already differ by group (02: Koenecke2020), so a transcript that "reads like written text"
  may say as much about the recognizer as about the student.
- Shown to a grader while grading, it can prime the judgment of the student rather than of the answer (05: Zheng2023 on
  anchoring to a machine verdict).

**Done.** GAD-T284 (the owner chose to keep it, as reviewer-only context, over dropping it):
- It is labelled everywhere as an **unvalidated estimate** whose false-flag rate has not been measured: the reviewer's
  explanation and the per-answer line on the review page (`IntegrityExplanations`), and the event's summary.
- It is never an integrity verdict and never affects the score (unchanged; the score never read it).
- It is hidden from a blind human grader until their score is submitted: the estimate with the exam-time judgments (as
  before), and the integrity event with every other integrity flag (GAD-T285).
- `TextStyleAssessor.Measure` and `gadfly textstyle-rate <transcripts>` count how often the estimate reaches the reviewer
  threshold over session transcripts, from the estimates already stored (no model calls). Over sessions from students
  known to be answering honestly, every answer at or above the threshold is a false flag; run it once per group (for
  example, by first language) to compare rates. The rate stays unknown until it is run on pilot data
  (`docs/PILOT.md`). Until then, treat any high estimate as noise unless the answers around it say otherwise.

## Owner decisions the research questions

The owner has decided these. This map records each one and does not override it. Every ticket named below was approved
by the owner and is done. The owner reversed decision (b) on 2026-11-21 (GAD-T258) and narrowed (a) on 2026-11-22
(GAD-T260); (c) and (d) stand.

### (a) Voice and camera only; typed answers only as a granted accommodation (narrowed 2026-11-22)

**Decision:** exams are voice plus camera only, with no text mode ("there is no text only mode"), to stop cheating. **The
owner narrowed this on 2026-11-22** by approving GAD-T260: typing is allowed only as an accommodation an instructor or admin
grants to one named student on one assignment, the way GAD-T261's eye-contact accommodation works. Every grant and
withdrawal is audited (`assessment.accommodation.typed-answers.granted` / `.withdrawn`). A student can never switch to
typing: there is no student-facing switch, and the session hub refuses a typed answer (other than "I don't know") or a
typed change of answer from anyone without the grant. With typing granted, the camera and the examiner avatar still run, the
examiner still speaks every question, and the gaze check still applies unless GAD-T261's eye-contact accommodation is also
granted. Only the answer channel changes: no microphone is opened, and the answer is typed in the exam room's answer box.
Every other student's exam is unchanged.

- **For:** unproctored remote exams are inflated (06). A spoken answer, built on by follow-ups from the student's own
  words, is harder to paste from another source than a typed one. Voice also brings the delivery and recitation context
  reviewers use (mechanic 3).
- **Against:** note 02 supports a typed-answer accommodation that instructors grant per student. Joughin2010 recommends
  alternative arrangements for undue anxiety and adjustments for speech or hearing impairment. ASR error is worst for some
  dialects and for people who stutter (Koenecke2020, Mujtaba2024). The strongest integrity control the evidence supports
  is the Socratic follow-up itself (06), and that works on typed answers too. With the camera still on, an accommodation
  keeps most of the integrity design. Under the current rule, a student who cannot use the voice exam has no route.
- **Done (GAD-T260):**
  - Granted on the instructor's assignments page (Answers column, "Grant typed answers") or the admin desk's Student
    exams tab, both through `ExamService.SetTypedAnswerAccommodationAsync`, which needs the assign-exams permission, so a
    desk admin who is a student cannot grant it to themselves (`ExamAssignment.TypedAnswerAccommodation`).
  - The start page checks only the camera, offers a typed practice question, and shows the typed wording of the consent
    notice. The exam room keeps the examiner and the camera, pauses the exam if the camera is lost, and shows a typed answer
    box; a meaning clarification is typed too; at time-up what is typed is handed in.
  - Typed answers are stored with mode `text` and shown to reviewers as **typed** (review page, grading ledger, admin desk
    transcript). Voice-derived cues (delivery, recitation read-out pattern, voice measurements) never run on them; the
    content-based cues, the source-overlap recitation check and the Socratic follow-ups do. The text-style estimate
    would too, but it is switched off for every answer (mechanic 9, GAD-T412). Unlike a spoken answer, a typed one keeps
    its meaning clarification and typo fix (GAD-T405 removed those only for spoken answers).
  - **Copy and paste:** pasting into the typed answer box is recorded, not blocked. When the exam's integrity level is not
    Off, the browser integrity signals note each paste with its length (`paste`: Info, Low from 20 characters or three
    pastes, Medium from 80 characters), the student is told so in the exam room, on the start page and in the consent
    notice, and the reviewer explanation says to compare the pasted answer with the student's other answers. Blocking was
    rejected: many assistive tools (dictation, AAC devices, word prediction) insert text the way a paste does, so a block
    would fail the students the accommodation is for, and the evidence (06) puts the integrity weight on the follow-up
    questions built from the student's own words, which a pasted answer still has to survive. Copying the question stays
    noted as before.

### (b) Voice strain drives harder probing (reversed 2026-11-21)

**Decision:** voice strain and stress detection drives harder probing, as the owner asked. **The owner reversed this
decision on 2026-11-21** by approving GAD-T258: how hard the examiner presses now comes from what the answer says
(hedging, vague wording, a missing reason or example), the judges' score and evidence, and recitation, never from the
voice comparison, the wait before the first word or the hesitation count. The voice measurements stay where they are on
(outside the EU by default), recorded and shown to reviewers as raw numbers.

- **For:** pressing a student whose answer comes out shaky can bring out whether the concept is really understood
  (mechanic 2). Comparing with the student's own baseline is the least unfair way to read any delivery signal.
- **Against:** voice-stress measures have no demonstrated validity (06: Harnsberger2009, Damphousse2007, NRC2003). The EU
  guidelines name the detection of emotional arousal and anxiety during exams as prohibited emotion recognition in
  education (para 255). Jitter and pitch are stress features of a biometric signal. Anxious, accented and disfluent
  students are the ones most likely to be pressed (02). The same aim can be met from answer content, which is outside
  the ban.
- **Tickets:** GAD-T259, approved and done: off by default in the EU, relabelled as raw differences. GAD-T258, approved
  on 2026-11-21 and done: probing from content, reversing decision (b) everywhere.

### (c) Gaze fade and "look at me"

**Decision:** looking away fades the screen and the examiner says "look at me".

- **For** and **against:** see mechanic 4. The use is legal, but the signal is coarse, cannot tell reading from any other
  reason for looking away, will often fire on honest students, and may differ by group.
- **Tickets:** GAD-T261, approved and done. It keeps the decision and adds a grace period, an accommodation switch and
  group measurement. GAD-T305, done: a calibrated gaze vector and a per-session quality score sent with every note.

### (d) Clear scores holds the attempt out of grading

**Decision:** Clear scores holds the attempt out of grading. `DeskService.ClearScoresAsync` removes the grades, reports,
disagreements, reconciliations, approvals and appeals, un-releases results, and parks each ended attempt at Examination
In Progress, so the grading sweeper does not grade it again. The attempt and transcript stay, and it still counts as
used. The action is audited. Since GAD-T262 the removed records are first archived to one `desk.scores-archived` audit
row per attempt (tied to the session, redacted by privacy erasure), and the student's My exams page says the scores were
cleared for review and that the attempt still counts.

- **For:** an attempt an administrator has cleared should not be graded again by the AI, unseen, seconds later. A human
  decides whether and when it is graded, as Article 14 human oversight requires (05). The audit row records who cleared
  it.
- **Against:** this is an Annex III 3(b) use, and logging and record-keeping duties apply (05). Hard-deleting appeals,
  reconciliations and approval records loses the oversight trail that would show how a grade was reached and then
  removed. Clearing also withdraws the released feedback that makes a retake worthwhile (03: Rowland2014), while the
  attempt still counts against the student's limit.
- **Ticket:** GAD-T262, approved and done. It keeps the decision, archives the records before removing them, and tells
  the student the attempt still counts. The archive answers the record-keeping objection; the withdrawn feedback and the
  used attempt remain as the decision sets them.

## Tickets

All were filed by GAD-T257 as proposed and unapproved; GAD-T284 and GAD-T285 were filed by GAD-T282. The owner has
approved every one, and all are done.

| Ticket | Mechanic | Change | Conflicts with an owner decision |
|---|---|---|---|
| GAD-T258 | 2, 5 | Adaptive probing from answer content, not voice strain (approved 2026-11-21, reversing (b); done) | Yes, (b) |
| GAD-T259 | 5 | Voice strain off by default for the EU, relabelled as raw acoustic deltas, PRIVACY.md fixed | Yes, (b) |
| GAD-T260 | (a) | Instructor-granted typed-answer accommodation (approved 2026-11-22, narrowing (a); done) | Yes, (a) |
| GAD-T261 | 4 | Gaze fade: thinking grace, accommodation switch, rates by group | Touches (c) |
| GAD-T262 | (d) | Clear scores archives grading records before removing them | Touches (d) |
| GAD-T263 | 6 | Exam clock stops while the exam is paused | No |
| GAD-T264 | 6 | End-of-answer silence adapts to the student's own pauses | No |
| GAD-T265 | 7 | Word error rate by group, low-confidence segments marked for graders (the error rate has no new references since GAD-T405) | No |
| GAD-T266 | 1 | Score how a contradiction is resolved; no-hint guard checks presupposition | No |
| GAD-T267 | 1, 2 | Report probe counts per objective; minimum objectives for summative exams | No |
| GAD-T268 | 1 | Credit transfer only from Apply; label transfer items | No |
| GAD-T269 | 7 | Grading panel bias controls | No |
| GAD-T270 | 7 | Calibration thresholds for the panel, and reconciliation triggers | No |
| GAD-T271 | 8 | Fixed, instructor-written hint ladder in Learning Mode | No |
| GAD-T272 | 3 | Measure false flags from the read-out pattern; suppress it under an accommodation | No |
| GAD-T273 | 7 | Feedback per objective on released results | No |
| GAD-T284 | 9 | Text-style estimate kept only as unvalidated, reviewer-only context, with a command that measures its false-flag rate (filed by GAD-T282; approved and done) | No |
| GAD-T285 | 7, 9 | Integrity flags hidden from the blind grader until their score is in; a different person reconciles and approves (filed by GAD-T282; approved and done) | No |

Later changes by the owner that this map now records, not filed from it: GAD-T305 (calibrated gaze and a quality score,
mechanic 4), GAD-T290 and GAD-T409 (the judging wait, mechanic 6), GAD-T405 (no read-back of spoken answers, mechanic 7),
GAD-T412 (text-style estimate switched off, mechanic 9).

## What the evidence does not let Gadfly claim

- That Gadfly "detects cheating" or "detects stress" (06).
- That a Socratic oral exam is a more valid measure than other formats, or that the rate of contradictions detects
  cheating (01).
- That oral scores are free of anxiety or accent effects (02: Huxham2012 is too small to show this).
- Any expected agreement figure for the grading panel before Gadfly's own pilot (05).
- Large learning gains from retakes, especially on complex material (03: vanGogSweller2015, KarpickeAue2015).
