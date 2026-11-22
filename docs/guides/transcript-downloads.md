# Transcript downloads

Every exam session has an official **transcript**: the record of the conversation, built from what Gadfly stored. It
has each question with its time and how far into the exam it came, each answer exactly as it was recorded, any question
about what an answer meant and the reply, every correction (the original and the corrected text), how long the student
waited before answering, and the words the speech recognizer was unsure of, marked [like this?]. There is no audio or
video in it: Gadfly never stores either.

## Getting your transcript (students)

Once your result is published, open it from **My exams**. Near the top of the result page, **Your transcript, with its
verification code** offers it as:

| Link | What you get |
|---|---|
| PDF | A document to keep or print |
| Word | A `.docx` you can open in Word or similar |
| print | A web page laid out for printing (opens in a new tab) |
| text | Plain text |
| VTT, SRT | Captions, timed to the conversation |
| JSON | Structured data, which can also be checked at the verify page |

The same page offers your **report** (PDF or Markdown) and your **completion certificate** (PDF). Before your result is
published there is no transcript to download; **Download my data** on **My exams** gives you everything Gadfly holds
about you at any time.

Your copy is the **student copy**: the questions and your answers, without the examiner's private reasoning, the AI
panel's scores or the integrity notes. The integrity notes recorded on your attempt, if any, are shown on the result page
itself, with an explanation.

## Staff copies

Staff download transcripts from the **Transcript** links on a session's review page, its grading page and the admin
desk. Which copy you get is decided by Gadfly from your role, not by the page:

| Copy | Who gets it | What it holds |
|---|---|---|
| Reviewer copy | Owners and admins; instructors, examiners and second reviewers once a session's grading is not blind | Everything, including integrity notes, voice measurements, the examiner's reason for each question and the AI panel's reading during the exam |
| Grader copy | Examiners, second reviewers and instructors while a session's grading is blind | No integrity notes, voice measurements or AI scores, so nothing colours the grading |
| Student copy | The student, once the result is published. Staff can download it too, with the **student copy** link, to see exactly what the student gets | The same as the grader copy |

You can always ask for a lesser copy than your own, never a greater one. See the [Instructor guide](INSTRUCTOR_GUIDE.md#transcripts)
for more.

## Checking a copy is genuine

Every copy carries a **verification code** (for example `7K3M-9QXA-B2CD-EFGH`) and a fingerprint (a SHA-256 hash) on
every page. Anyone holding a copy, such as a registrar, an appeals panel or an employer, can check it at
`/transcripts/verify`, without signing in: type the code, or upload the JSON file. The page never shows what the
transcript says. It answers one of:

- **Genuine and unchanged**: Gadfly issued this copy and its record still says exactly the same.
- **Genuine, but the record has changed since**: a later correction, or words removed when the retention period ended.
  A new download gets a new code.
- **Issued, but no longer held**: Gadfly issued it, but the session has since been deleted (for example at the
  student's request).
- **Changed after it was issued**: the uploaded file names a code Gadfly issued, but something in it was edited.
- **Not on record**: Gadfly never issued a copy with this code or content; check the code was typed correctly.

The same page checks a completion certificate's code.

Every download is recorded in the audit log as `transcript.issued`, with the copy, the format and its code (never the
transcript's words).

## Languages

The PDF uses the standard PDF fonts, which cover Western European languages. For other alphabets use the Word, print
or JSON copy, which keep every character.
