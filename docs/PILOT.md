# Pilot with Erin

This is the plan for GAD-T66: Erin uses Gadfly Exams on her own course material, runs real sessions, and her
feedback becomes tickets. It is ready to start as soon as the owner says go. Nothing here runs until then.

## Before the pilot (owner)

| Step | Who | Notes |
|---|---|---|
| Decide live AI or mock (D23) | Owner | In mock mode the questions and judgments are simulated. That is enough to try the workflow and the screens, but not to judge question quality. A real pilot needs D23 = live for the named pilot sessions. |
| Pick the ElevenLabs voice (D4) | Owner | Needed for every live session: every exam is spoken by the examiner, also for a student granted typed answers. Mock mode needs no voice. |
| Answer D24 (staff MFA) | Owner | Erin's teacher account needs an authenticator app unless D24 = B. |
| Create Erin's organization and teacher account | Claude, on the owner's word | Her own organization, separate from the "Gadfly Classroom" test data. She gets an email invitation (or a temporary password if email isn't set up). |
| Students | Erin | Real students, or the owner and friends playing students. At least five sessions in total (the ticket's target). |

## What Erin does

1. Signs in at https://mindattic-gadfly.azurewebsites.net and sets up her authenticator app.
2. Creates a course and uploads her source material: Markdown, text, PDF, Word (.docx), EPUB or HTML, up to 20 MB each.
3. Processes it and reads the concept map. She fixes any concept that is wrong or missing and adjusts the rubric.
4. Builds an exam on two or three concepts and picks the integrity level (Strict adds the camera integrity checks, with the student's consent). Every exam is spoken to the examiner on camera; a student who cannot answer aloud can be granted typed answers on that one exam (GAD-T260), and the examiner still speaks every question.
5. Assigns it to her students and sends them the link.
6. After each session, grades it on the Grading page. She grades blind first, then sees the AI's view, reconciles any big disagreement, approves and publishes.
7. Fills in the feedback form below for each session, or just says it aloud and the owner writes it down.

The step-by-step screens are in [guides/INSTRUCTOR_GUIDE.md](guides/INSTRUCTOR_GUIDE.md). Students can read
[guides/STUDENT_GUIDE.md](guides/STUDENT_GUIDE.md).

## What to watch for

These are the questions the pilot exists to answer:

- **Does it find real understanding?** Did the questions follow up on what the student actually said? Could a student
  who only memorised the material get through? Could a student who understood it but phrased it badly still show it?
- **Is it fair?** Did the AI's preliminary grade agree with Erin's? Where they disagreed, who was right?
- **Is the source material used correctly?** Did questions and judgments stay within her material, and cite it?
- **Does cheating get harder?** Was there anything a student could paste from another AI that passed? Did the webcam
  and audio notes help or just add noise?
- **Is the voice usable?** Speech recognition errors, the talking head, the short "let me think" lines and the page-turn sound while the examiner judges an answer (GAD-T290, GAD-T409), and the eye-contact setup on the get-ready page (GAD-T305). A spoken answer is judged as heard: there is no read-back for the student to confirm or correct (GAD-T405), so note every answer that was mis-heard and whether the grade suffered for it.
- **Does speech recognition treat every student alike?** In grading, words the recognizer was unsure of are marked for
  the AI panel and the human so they are not scored as wrong content (GAD-T265). Analytics' "Speech recognition accuracy
  by group" section shows how often the recognizer was unsure, by a transcript-based proxy or by a grouping pasted in
  (accent, dialect, a self-described speech group; never stored). Its word error rate is measured against transcripts the
  student checked, and since the read-back was removed (GAD-T405) no spoken answer is checked, so for pilot sessions that
  column stays empty. Gadfly keeps no audio ([PRIVACY.md](PRIVACY.md)), so measuring it needs a person listening: where a
  student agrees, Erin or an observer sitting in writes down what was actually said for a few answers per session, and the
  owner compares that with the recorded transcript.
- **Workload:** how long did setup, each session and each grading take?

## Feedback form

Copy this once per session. Each "Problem" line becomes a fix ticket and each "Wish" a feature ticket.

```
Session: <date, student or alias, exam, text/voice>
Setup time: <minutes>   Session length: <minutes>   Grading time: <minutes>
Erin's grade vs AI's preliminary grade: <same / differ, and which was right>
Best question it asked:
Worst question it asked:
Did it ever leave her material or get a fact wrong? <where>
Problems: <one per line: what happened, where, what she expected>
Wishes: <one per line>
Would she use it for a real grade? <yes / not yet, because ...>
```

## After the pilot

The owner pastes the forms into the chat. Each problem and wish is filed as a ticket in Gadfly (linked to GAD-T66),
prioritised with the owner, and worked like any other ticket. GAD-T66 closes when at least five sessions are done
and their feedback is filed.

Before any exam's AI grades are relied on, its panel is calibrated (GAD-T270). Since GAD-T428 the double marking
happens in the app: on **Grading > Panel calibration**, open the exam, tick **Double marking** and, for the pilot,
**Human-only until calibrated** (GAD-T427: the panel then counts for nothing in the mark until a calibration is
accepted). Erin grades as usual (blind by default); a second examiner grades the same sessions from **Second marking**,
which shows only a session code, the rubric and the answers the panel saw, never the panel's or Erin's grade. The
calibration page then shows the human-human QWK, the panel's agreement with each marker and whether the thresholds
hold, and **Record as this exam's calibration** saves it (levels can still be posted from outside to
`POST /grading/calibration/{examId}`, docs/API.md, "Panel calibration"). The panel is accepted for that exam only when
panel-human QWK is at least .70, no more than .10 below the two examiners' own agreement, and |SMD| is under .15, on at
least 30 answers. No published source tests LLM grading of spoken Socratic answers, so these figures can only come
from Gadfly's own double-marked answers (research note 05). Mock-panel grades never accept a panel.

The same page turns the set into policy suggestions (ElenchusAtScale 3.8, 5.4.1): the AI weight whose combined grade
agrees best with the independent second marker (0 to 50%; 0 whenever the panel was not accepted), review and
reconciliation thresholds from how far the two examiners themselves differ, a boundary margin and a panel-spread
limit. They change nothing by themselves: adopt them with a grading-policy change request. Paste a "student email,
group" list to see the SMD by group (an overall SMD can hide a gap one group carries); the list is never stored.

A calibration records the model id of every panel member and the grading prompts' version, and **expires** when any of
those changes (GAD-T429, GAD-T469): grades
are provisional again and a gated exam goes back to human-only until it is re-run. After 180 days
(`Gadfly:Grading:RecalibrateAfterDays`) the page asks for a re-run of the calibration and the bias probes; nothing
re-runs by itself.

Before the panel's grades are trusted, also run its **bias probes** (GAD-T432; ElenchusAtScale 4.6.1, 5.3.2, 6.8) from
the exam's **Bias probes** page: the panel grades a sample of answers again padded, less fluent and with a spoken
instruction to the grader; the page shows the estimated provider cost and runs only when someone with the policy
approval permission ticks the cost and starts it (mock mode costs nothing). The human paraphrase probe on the same page
has each examiner mark one form of each answer blind, so a fluency effect shared with people is told apart from the
panel's own. Offline, `gadfly bias-check answers.json` runs the panel probes on the mock panel; `--live --approve-spend
<usd>` runs them for real after printing the estimate. Once enough sessions record who wrote each question, the
**Self-preference check** page (GAD-T431) shows whether a grading member favours answers to its own family's questions.

The same double-marked pilot sets how many objectives an examination needs before its score is used summatively
(GAD-T443; ElenchusAtScale 3.7). `SessionPolicy.MinSummativeObjectives` defaults to 4, which is a setting, not an
estimate. To estimate it, export one row per rater per answer, with every rater marking every student on every
objective, as a CSV with the columns `student,objective,rater,score`, and run:

```
gadfly reliability pilot-grades.csv --target 0.8
```

It prints how much of the score variance comes from the students, the objectives, the raters and their interactions,
the reliability of an examination of 1 to 20 objectives (relative, for ranking students, and absolute, for a fixed pass
mark), and the fewest objectives reaching the target. `--raters N` projects for a different number of markers per
answer. Students, objectives or raters with gaps are left out and named. With fewer than about 30 students the
estimate is rough: set the minimum from it, then re-run as more double-marked exams come in.

### The relay arm (GAD-T449)

A student who hears answers through a hidden earpiece and speaks them in their own voice is the one cheating case
Gadfly is not designed against. The webcam cannot see an in-ear device, the second-voice check cannot hear one, and
the recitation check does not fire on an answer spoken naturally. The only control is the follow-up question, written
from the student's own earlier answer, so whoever relays must answer each new question about what was just said, live.
Whether that works is hypothesis H1 (ElenchusAtScale 3.3): relayed answers are less coherent across turns, with more
contradictions in the claim ledger and more failed resolutions, than answers from students who understand the material,
at equal quality of the individual answers. No verified source supports it yet; the pilot tests it.

Run a set of consenting participants honestly and a set who relay answers from a helper or a model (by earpiece or any
other channel), each with `gadfly exam`, which saves a `*.session.json` and its `*.ledger.json` per run. Keep each
condition in its own folder, then:

```
gadfly coherence honest=runs/honest relay=runs/relay --out coherence.csv
```

It writes one row per session (answers, mean answer score, claims, contradictions, possible contradictions,
confrontations, failed resolutions, withdrawals) and prints each condition's counts and means. Compare contradictions
per ten claims and failed resolutions only between sessions of similar mean answer score, and estimate the detector's
miss rate first (`gadfly contradiction-audit`), since a detector that misses most contradictions hides any difference.
These measures are a research instrument: they are never computed for a real student, never shown as an integrity note
and never part of a grade. A room and ear check at the start of the exam is a separate, later owner decision (GAD-T244).

## Research consent, study arms and the research export (GAD-T451, GAD-T452)

The study after the pilot (ElenchusAtScale Chapter 7) runs on real exams in the server, not on CLI runs. An owner or
admin sets it up on **Research** (`/org/research`; docs/guides/ADMIN_GUIDE.md, "Research").

**Consent (7.5).** Research use is a separate, optional consent, asked below the exam notice on the get-ready page and
never needed to begin. The student's choice is stored append-only (`ResearchConsents`) with the notice's version and
the SHA-256 of the exact text shown; the latest choice counts. A student withdraws on **My research choice**, after
which none of their records go into any later export (a copy already handed over can't be recalled; it holds no
names). Erasing a student removes their choices too. Students who agree see their study code there, so a separate
study questionnaire (for example one asking about first language or speech, which Gadfly does not collect) can be
matched to their exam records without a name.

**Study arms (7.2).** Arms are per exam, one per assignment, fixed once the student starts an attempt, and can be
filled at random keeping the arms even. They are labels: Gadfly does not run an arm differently. The relay arm
(above) is run by telling its participants to relay; the feedback-withheld arm by not releasing their results until the
study's later attempt. An arm that would switch off an owner decision (the gaze fade, decision (c), ElenchusAtScale
5.7.3) is not built and needs the owner's approval first.

**Research export (7.1-7.3).** `GET /admin/research/export.zip[?exam={id}]` (owners and admins; docs/API.md) gives a
ZIP of CSVs with a README and a manifest. Only students whose current choice is "agreed" are in it. It never holds a
name, email, user name, answer or question text, audio, video, free-text comment, appeal text or database id; every
id is a keyed pseudonym (HMAC-SHA256 with the organization's research key), stable across exports until the key is
replaced. Times are relative. Voice measurements are blank wherever they were off, and always for an EU/EEA
organization or student. An exam with fewer consenting students than the smallest group (default 5) is held back, and
a rarer accommodation within an exam is shown as `suppressed`. Each download is audited (`research.exported`).

| File | One row per | Columns |
|---|---|---|
| `sessions.csv` | attempt | `session, student, exam, arm, attempt, state, end_reason, started_week, days_since_first_attempt, duration_s, answer_channel, extra_time, eye_contact_accommodation, typed_answer_accommodation, voice_measures, result_published, feedback_released_before_attempt, final_status, final_percent, turns, cues_total, integrity_notes, integrity_notes_info_or_low, words_redacted_by_retention` |
| `turns.csv` | question | `session, turn, concept, move, asked_at_s, answer_latency_s, answer_duration_s, answer_channel, word_count, recognizer_confidence, low_confidence_words, reference_words, wer_substitutions, wer_deletions, wer_insertions, word_error_rate, time_to_first_word_s, speech_rate_wpm, pause_ratio, long_pauses, disfluency_per_100_words, voice_vs_baseline, panel_score, panel_agreement, panel_needs_human_review, panel_votes, text_style_score, mastery_after, cue_count, cue_kinds, probe_pressure` |
| `objectives.csv` | objective | `objective, exam, key, ordinal, weight, max_score` |
| `panel_scores.csv` | AI panel grade per objective | `session, objective, level, score, max_score, confidence` (mock grades never) |
| `human_scores.csv` | human mark | `session, objective, rater, rater_role, level, score, max_score, blind, superseded, hours_after_session` (double marks are two rows) |
| `reconciliations.csv` | panel-human disagreement | `session, objective, panel_score, human_score, difference_points, band, hours_to_resolve, cause, second_reviewer, resolved_panel_score, resolved_human_score` |
| `integrity_events.csv` | integrity note | `session, source, type, severity, confidence, started_at_s, duration_s, turn` |
| `arms.csv` | study arm | `arm, exam, name, students` |

What each measure of 7.3 comes from: QWK and SMD from `panel_scores` against `human_scores` (and human against human
where an objective has two raters); the rate and causes of reconciliation from `reconciliations` (`cause` is the
reconciler's coded cause); per-objective change between attempts with and without released feedback from
`human_scores`/`panel_scores` by `attempt` and `feedback_released_before_attempt`; relay against honest from `arm`.
Word error rate per spoken answer (GAD-T467) is in `turns.csv` wherever a person typed a reference transcript of that
answer for the speech-recognition accuracy check (GAD-T426): the substitutions, deletions and insertions against the
reference and the rate, as counts only (neither transcript is exported), for consenting students under the same pseudonyms,
and blank once retention has removed the words. By speaker group still needs a speaker group the export does not carry:
the study collects it itself, with its own consent, and matches it by study code. The referenced answers are a sample
drawn to cover answers with many fillers and repetitions, so compare groups with each other, not with benchmarks.
Integrity notes are exported because 7.5 asks how they are treated; they are low-confidence notes, never a verdict and
never part of a grade, and the analysis must treat them so.
