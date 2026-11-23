# Exams in English, Korean and Spanish (for staff)

Students can take a Gadfly exam in **English, Korean or Spanish**. This page is for instructors, examiners and admins: how to
offer a language, how to check the translated texts, what changes for students who speak a second language, how to grade an
attempt taken in Korean or Spanish, and how to check fairness by language. Spanish is asked and translated in neutral Latin
American Spanish, addressing the student as *usted*.

The one rule behind all of it: **a student whose best language is English, taking an exam in English, gets exactly the exam
they always got.** Every adaptation below switches on only from the exam language and the primary language the student stated
themselves. Nothing is ever inferred from a voice, an accent, a name or the browser.

## What is translated, and what never is

| Part | Language |
|---|---|
| Source material, concept map, rubric, grading policy | As you wrote them. They are the standard every answer is judged against, so each language is the same exam. |
| Exam title, instructions, objective titles | Per language, checked and approved by you (below). |
| The examiner's questions and fixed lines | The exam language. Questions are worded live from the English material. |
| The student pages (My exams, get-ready, results, emails) | The student's chosen language (English, 한국어 or Español). The exam room follows the attempt's language: its buttons, status lines and notices, the examiner and the questions. |
| Staff pages | English, with machine translation of a Korean or Spanish attempt on demand. |

## Offering an exam in a language

The **Languages** panel is in the assessment editor's **Details** step and under **Edit** on the admin desk's Exams tab. New
exams are offered in English, Korean and Spanish; untick a language and press **Save languages** to stop offering it. At least
one stays on. You can change the list after publishing: an attempt keeps the language it began in. Every change is in the audit log
(`exam.languages.changed`). Students only ever see the languages you offer.

## The translated texts: draft, check, approve

Below the language list, each language other than English shows the exam's title, instructions and objective titles beside the
English.

1. **Generate a machine-translated draft.** Gadfly translates the English texts with the live turn's fast model, metered to your
   organization as translation usage. In mock mode no model is called: each text is the English behind a marker,
   `[Korean, mock translation] ...`, so a mock draft can never pass for a real one.
2. **Read each text against the English and correct it.** **Save** keeps your edits without approving them.
3. **Save and approve** needs every text that exists in English to have a translation, and marks the texts approved.

Until you approve, students who choose that language see the draft with a note that it was machine-translated and not yet
checked by their instructor. Generating a new draft over approved texts asks for a second, explicit click. Drafts, edits and
approvals are audited with the before and after texts. You can correct the texts after publishing; they change only what a
student reads.

## What changes for a student taking an exam in a second language

When the exam language differs from the primary language the student stated on the get-ready page (for example a Korean
speaker taking an English exam), Gadfly adapts. These follow the research in
[esl-and-korean.md](../research/esl-and-korean.md):

- **Recognition** is pinned to the exam language and waits longer before it treats a silence as the end of an answer (a 6 s
  floor instead of 4, up to 15 s as it adapts), because second-language speakers pause longer and mid-clause. A voice
  calibration reading on the get-ready page can lengthen this further for that student.
- **The examiner** speaks slightly slower (about 0.9 of the normal pace) and asks plainer questions: one question at a time,
  about twenty words, no idioms, phrasal verbs or negative questions. Captions are always on.
- **Unsure words are asked about, not misjudged.** If the recognizer was unsure of a word and the answer was not yet convincing,
  the examiner may ask one brief question about that word (at most three per exam, never two in a row).
- **The judges grade content, not form.** The AI panel reads the answer with fillers, repeats and false starts removed by a fixed
  rule (the verbatim answer stays the record). The judges are never told the student is a second-language speaker.

Every student, whatever their language, can press **Say it another way** in the exam room to hear the open question reworded.
The rewording passes the same leak check as any question, is free and outside scoring, and the question as first asked stays the
record.

## Grading an attempt taken in Korean or Spanish

- **The original is the record.** The grading page, the review page and the blind second-mark sheet show the questions and
  answers exactly as given, marked with their language.
- **Show a machine translation into English** puts a labelled translation under each original: "Machine translation ... Not the
  record; the original above is." Grade the original against the rubric; if a translation decides a grade, ask someone who
  reads the language. Translations are never saved with the session or put in any download.
- **You are not shown the student's primary language.** It is not evidence of understanding and could prime a grade. It is used
  only in aggregate, for the fairness views.
- **Korean and Spanish attempts are held for a person.** Until a bilingual calibration shows the AI panel agrees with human
  graders on answers in that language, every non-English attempt is marked **Needs instructor review**, and its AI objective
  grades are marked cross-lingual and uncalibrated. See [Attempts marked for instructor review](attempts-marked-for-review.md).

## Fairness by language

On **Analytics**, Owners and Admins can group the fairness section by **exam language** or by **the student's primary
language** (as stated; "Primary language not stated" when none), besides accommodation or a pasted grouping:

- the score gaps between the panel and human graders, and the path drivers (pressed harder, hedging, vague wording, cut-offs,
  long pauses), compared across languages;
- the analytics CSV repeats both tables by exam language and by primary language;
- the panel calibration page checks the panel's systematic difference from double markers by either;
- the speech-recognition table can compare recognition accuracy by primary language (**Compare by primary language**).

The usual rules hold: a group with fewer than five students keeps its name and size but shows no figures, and totals are withheld
whenever a group is. A gap is a reason to look at the examiner, the recognizer, the judges and the graders, never a finding about
the students.

## Privacy

The exam language is part of the attempt record. The languages a student speaks and their primary language are stated by the
student, stored per organization, included in their data export and deleted on erasure. Voice calibration keeps only a few
numbers (reading speed, pauses, recognizer confidence), never audio or words. See [Privacy in plain words](privacy-in-plain-words.md).

## Related

- For students: [Getting ready: the get-ready page and your languages](get-ready-and-languages.md)
  ([한국어](get-ready-and-languages-ko.md)).
- [Instructor guide](INSTRUCTOR_GUIDE.md), [Grading guide](grading-guide.md), [Admin guide](ADMIN_GUIDE.md).
