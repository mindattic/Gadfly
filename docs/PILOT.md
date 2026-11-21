# Pilot with Erin

This is the plan for GAD-T66: Erin uses Gadfly Exams on her own course material, runs real sessions, and her
feedback becomes tickets. It is ready to start as soon as the owner says go. Nothing here runs until then.

## Before the pilot (owner)

| Step | Who | Notes |
|---|---|---|
| Decide live AI or mock (D23) | Owner | In mock mode the questions and judgments are simulated. That is enough to try the workflow and the screens, but not to judge question quality. A real pilot needs D23 = live for the named pilot sessions. |
| Pick the ElevenLabs voice (D4) | Owner | Only needed for voice sessions. Text sessions work without it. |
| Answer D24 (staff MFA) | Owner | Erin's teacher account needs an authenticator app unless D24 = B. |
| Create Erin's organization and teacher account | Claude, on the owner's word | Her own organization, separate from the "Gadfly Classroom" test data. She gets an email invitation (or a temporary password if email isn't set up). |
| Students | Erin | Real students, or the owner and friends playing students. At least five sessions in total (the ticket's target). |

## What Erin does

1. Signs in at https://mindattic-gadfly.azurewebsites.net and sets up her authenticator app.
2. Creates a course and uploads her source material: Markdown, text, PDF, Word (.docx), EPUB or HTML, up to 20 MB each.
3. Processes it and reads the concept map. She fixes any concept that is wrong or missing and adjusts the rubric.
4. Builds an exam on two or three concepts, picks text or voice, and the integrity level (Strict turns on the webcam check, with the student's consent).
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
- **Is the voice usable?** Speech recognition errors, the "is this what you said?" check, the talking head.
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
