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

Before any exam's AI grades are relied on, its panel is calibrated (GAD-T270): a second examiner grades the pilot
answers blind and independently of Erin, and the panel's levels, Erin's and the second examiner's are posted to
`POST /grading/calibration/{examId}` (docs/API.md, "Panel calibration"). The panel is accepted for that exam only when
panel-human QWK is at least .70, no more than .10 below the two examiners' own agreement, and |SMD| is under .15, on at
least 30 answers. No published source tests LLM grading of spoken Socratic answers, so these figures can only come
from Gadfly's own double-marked answers (research note 05).
