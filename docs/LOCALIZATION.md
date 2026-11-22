# Localization: exams in English and Korean

Students can take a Gadfly exam in a language other than English, Korean first (GAD-T526). This page is the as-built
architecture of that work (GAD-T527): which language each part of the system works in, where the language is decided and
recorded, and what stays in English on purpose. It ends with how the student interface is localized, the Korean style and
the glossary every translator and agent follows.

Status, 22 November 2026: the data model, exam language variants, the student interface framework, speech (recognition,
voice, lip-sync, fillers), grading across languages and the fairness views are built. The engine's Korean prompts and
heuristics (GAD-T535 to T537), the get-ready language picker (GAD-T546), the language profile questions (GAD-T552) and the
ESL-aware examiner (GAD-T555) are in progress; their sections below say what is decided and what is still to land.

## The rule that keeps English exams unchanged

An attempt has an **exam language** and, if the student stated one, a **primary language**. An English exam taken by a
student whose primary language is English (or not stated) behaves exactly as it did before localization: same prompts (byte
for byte, same prompt-set hash), same recognizer settings, same voice, same heuristics. Every language-dependent branch is
keyed on one of those two values, never on the browser, the interface language or anything inferred from the student's voice
or name (owner rule; docs/research/esl-and-korean.md).

## Where the language lives (GAD-T534)

| Record | Field | Meaning |
|---|---|---|
| `Exam` | `Languages` | The languages a student may take the exam in, a comma list of BCP-47 primary tags, `en,ko` by default (GAD-T539). |
| `ExamLocalization` | one row per exam and non-English language | The exam's title, instructions and objective titles in that language, `MachineDraft` or `Approved` (GAD-T540). English stays on the `Exam` row. |
| `ExamSession` | `Language` | The language the attempt is conducted in, chosen before Begin and fixed for the attempt. `en` for every attempt before localization. |
| `ExamSession` | `PrimaryLanguage` | The student's primary language copied from their profile when the attempt started (null: not stated), so a later edit never changes how a finished attempt was run. |
| `StudentLanguageProfile` | per user, per organization | The languages a student speaks and the one they speak best, stated by the student (GAD-T552), plus per-language voice calibration (GAD-T594). |

`Gadfly.Engine.ExamLanguage` normalizes tags (`ko-KR` and `KO` become `ko`), lists the supported languages (`en`, `ko`)
and gives each language's English and native name. Supported languages are a code constant: adding one (Spanish, GAD-T596)
means a prompt rule block, heuristics, a voice check, a lip-sync module, a string file and a glossary, not configuration.

## What is translated, and what never is

| Part | Language | Why |
|---|---|---|
| Source material, concept map, rubric, grading policy | As written (English) | They are the standard the answer is judged against. Translating them would make each language a different exam. |
| Exam title, instructions, objective titles | Per language, instructor-approved | The texts a student reads before and during the exam (GAD-T540). |
| Examiner's questions, leak check, provisional judgment | The exam language, grounded in the English material | GAD-T535 (in progress): the live turn's prompts gain a language rule block; each language variant is its own prompt set with its own id and hash (`exam-ko`, `exam-en-plain`, `exam-ko-plain`). |
| Panel judgment of an answer | The answer as given, judged against the English rubric | GAD-T536 (in progress): no machine translation of the answer; each judge quotes the student's own words and glosses them in English so a person can check the reading. Judges are told the exam language only, never that the student is a second-language speaker. |
| Student interface | The student's chosen interface language; the exam room uses the attempt's language | GAD-T543/T544 (below). |
| Staff pages (assessment editor, admin, review, grading, analytics) | English | Staff read the original record and, on demand, a labelled machine translation (GAD-T548). |

## Offering an exam in a language (GAD-T539)

- **Where.** The assessment editor's Details step and the admin desk's exam edit row show a **Languages** panel
  (`Components/Pages/Assessments/ExamLanguagesPanel.razor`, `Gadfly.Server/Translation/ExamLanguageService.cs`).
- **Rules.** Staff with `ManageAssessments` turn languages on or off; at least one stays on; the list may change after
  publishing, since an attempt keeps the language it began in. Every change is audited (`exam.languages.changed`). A
  duplicated exam keeps its languages; its texts are drafted and approved on their own.
- **Students only see offered languages.** Student pages read `ExamLanguageService.Offered(exam.Languages)`; the get-ready
  picker and the session start must offer and accept only those (GAD-T546, in progress).

## Localized exam texts (GAD-T540)

- **Machine draft.** "Generate a machine-translated draft" translates the English title, instructions and objective titles
  into the language with `MachineTranslator` and stores them as `MachineDraft`. Live, the translator is the live turn's one
  fast model (`ILiveTurnModel`: Claude Sonnet 5 by default, never Opus) at temperature 0 with a faithful-translation prompt,
  metered to the organization as usage kind `translate` (purpose `exam-texts`). In mock mode nothing is called: each text is
  the English behind a marker, `[Korean, mock translation] ...`, so a mock draft can never pass for a real one.
- **Review and approval.** The panel shows each text beside its English, editable. "Save" keeps the status; "Save and
  approve" requires every text that exists in English to have a translation and makes the texts `Approved`. Regenerating over
  approved texts needs a second, explicit click. Drafts, edits and approvals are audited (`exam.localization.drafted`,
  `.saved`, `.approved`) with the before and after texts.
- **What students read.** `LocalizedExamTexts.ForAsync(db, org, examId, language)` returns each text in the language, falling
  back to English text by text, and `MachineDraft = true` while the texts are unapproved: the student page must then say they
  were machine-translated and not yet checked by the instructor, never present them as the instructor's own words.
- **Existing exams.** Live exams published before localization get their drafts through GAD-T541 (a data chore after
  deploy), which calls the same `DraftAsync` per exam.

## Speech (GAD-T528)

Covered in full in [VOICE.md](VOICE.md), "Exam language and second-language speakers". In short: a Korean attempt is heard
as `ko` (Scribe `language_code`, Whisper `language`) and an English attempt by a second-language speaker is pinned to `en`;
the server, not the browser, decides the language and the hub filter refuses any other. Keyterms are capped at Scribe's
realtime limits (50 terms, 1,000 characters) with the localized objective titles first. The examiner speaks Korean with the
same ElevenLabs voice and model unless configured per language; Korean lip-sync is Gadfly's own Hangul-to-viseme module
(`wwwroot/js/lipsync-ko.js`); the fixed lines (acknowledgements, fillers, backchannels, eye-contact remarks) exist per
language, reviewed as non-evaluative. Second-language speakers get longer end-of-answer silences and a slightly slower
examiner (GAD-T554, GAD-T555).

## The engine in the exam language (GAD-T533, in progress)

- **Prompts.** `Gadfly.Engine.ExamPromptLanguages` builds each language variant from the English prompt set by appending fixed
  rule blocks; the English wording is never edited, and each variant is recorded on the session by id and hash, so an attempt
  can never resume on a different language's prompts.
- **Heuristics.** `Gadfly.Engine.LanguageText`: word counts, end-of-answer completeness, "I don't know", hedging, vague wording,
  reasons and rephrase requests in English and Korean. Every Korean rule keys on Hangul, so English text keeps exactly the
  English rules, and English technical terms inside a Korean answer (the normal case) still get them.
- **Recitation and speech accuracy.** Korean answers are compared by character, not word, since Korean spacing varies
  (GAD-T554); English answers score exactly as before.

## Grading across languages (GAD-T547)

- **The original is the record.** Questions and answers are stored, shown and exported exactly as given, marked with their
  language (`lang` attribute, WCAG 3.1.2). The Markdown export of a non-English attempt names its exam language; downloads
  never contain a translation.
- **Machine translation on demand (GAD-T548).** On the review page (questions, answers and the AI report), the grading page's
  transcript and the blind second-mark sheet, a bar says the exam was taken in Korean and offers "Show a machine translation
  into English". Each translation appears under its original, labelled "Machine translation from Korean into English ... Not
  the record; the original above is." (with "(mock: no model was called)" in mock mode). `TranslationService` translates the
  page's texts in one batch for staff with `ViewTranscripts` or `ManageAssessments`, meters any live call to the organization
  and session (purpose `grader-translation`), and `MachineTranslator` caches results in memory by language pair, model and the
  text's SHA-256 (up to 10,000 entries per process), so a page opened twice, or by two graders, is translated once. Nothing is
  written back: the translation is not stored with the session, the turn, the transcript versions or the audit trail.
- **What graders are not shown.** The student's primary language is never shown on the review, grading or second-mark pages,
  and never sent to the judges: it is not evidence of understanding and could prime a grade. It is used in aggregate only.
- **The review list** tags each non-English attempt with its language.

## Fairness by language (GAD-T549, GAD-T555)

- **Fairness views.** The analytics page's fairness section groups by accommodation (the default), **exam language**,
  **the student's primary language** (as stated; "Primary language not stated" when none) or a pasted grouping
  (`GroupFairness.Grouper(FairnessGrouping)`). The score gaps between panel and human (the panel-minus-human gap by language the
  ESL research asks for) and the path drivers (pressed harder, hedging, vague wording, cut-offs, long pauses) are then compared
  across languages.
- **Export.** The analytics CSV repeats the score-gap and path-driver tables `by_exam_language` and `by_primary_language`.
- **Calibration.** The panel calibration page checks the panel's systematic difference from the double markers (SMD) by exam
  language or primary language, as it already did for a pasted grouping.
- **Speech recognition.** The speech-recognition table compares accuracy by primary language (GAD-T554).
- **Same rules as every fairness view.** Owners and Admins only (they never grade blind); a group with fewer than five students
  keeps its name and size but no figure; totals are withheld whenever a group is (GAD-T480, GAD-T486). A gap is a reason to
  look, not a finding.

## Privacy

The exam language is part of the attempt record. The primary language and languages spoken are stated by the student, never
inferred, stored per organization, exported with the student's data and deleted on erasure; they are used to adapt the
examiner and recognizer and, in aggregate, for fairness, never to judge an answer. Machine translations of answers go to the
same LLM providers that already judge them and are not stored. See [PRIVACY.md](PRIVACY.md).

## Student interface (GAD-T543, GAD-T544)

How the interface text is localized, how a student chooses a language, and the Korean glossary.

### How it works

- **Strings.** Every interface string a student sees lives in `Gadfly.Server/Localization/Strings/<lang>.json`: a flat JSON
  object of stable dotted keys (`getReady.title`, `examRoom.paused`) to text. `en.json` is the source and the fallback, so a key
  Korean has not translated yet shows in English, never as a raw key. Keys starting with `_` are comments. The files are embedded
  in the app and read once at startup (`UiCatalog`).
- **In a component.** `@inject UiText T`, then `@T["key"]` or `@T["key", arg0, arg1]` (formatted with the language's culture).
  Dates: `T.DateTime(t)`, `T.Date(t)`; numbers: `T.Number(n)`. `UiText` is scoped per circuit.
- **Choosing the language.** Students get a language switch in the top bar (English / 한국어). It writes ASP.NET Core's
  culture cookie (`.AspNetCore.Culture`, a year) and reloads the page in that language. The request culture comes from that
  cookie, then a `?culture=ko-KR` query value, and **never** from the browser's Accept-Language: a student who has not chosen a
  language keeps today's English experience (owner rule: English primary plus an English exam is today's experience).
- **Live switch.** A page can switch language without a reload with `T.Use("ko")`; `T.Changed` tells listeners (the top bar
  and the language switch listen). The get-ready page does this when the student picks the exam language (GAD-T546), and also
  writes the cookie, so the exam room and the pages after it follow.
- **Per attempt.** An attempt is conducted in its own language (`ExamSession.Language`, GAD-T534), whatever the switch says.
  `T.For(language)` gives the text of a fixed language; the exam room uses the attempt's language. The switch is hidden in the
  exam room.
- **Scripts.** A JS module that writes text of its own fetches its strings: `GET /ui-strings/{lang}?prefix=examRoom.` returns
  a flat JSON object (English filling gaps), or `import("./js/ui-language.js").strings(lang, prefix)`. A component can also pass
  `T.Dictionary("examRoom.")` to its module.
- **Accessibility.** `<html lang>` is the page's language (WCAG 3.1.1) and the switch keeps it in step. Text in another
  language than the page (a language's own name in a picker, the Korean pangram on an English page) carries its own `lang`
  attribute (WCAG 3.1.2).
- **Checks.** `Gadfly.Tests/Server/LocalizationTests.cs`: every key the code uses exists in English, every Korean key exists in
  English, Korean covers every key, and each translation keeps the English text's `{0}` placeholders.

### Adding a string

1. Add the key and English text to `en.json`, in its section.
2. Add the Korean to `ko.json` under the same key, following the style and glossary below.
3. Use `@T["key"]` in the component. Never build a sentence from fragments (word order differs); give the whole sentence one key
   with placeholders.

### Korean style

- Polite, warm and clear: the 해요체 (`-요`) for guidance, notices and buttons' explanations, and the 합쇼체 (`-습니다`)
  for formal statements (consent notices, rules, legal and privacy text, emails' formal lines). Never 반말.
- Buttons and labels are short nouns or noun phrases (`시험 시작`, `다시 확인`), not sentences.
- Address the student without "당신"; Korean leaves the subject out. Use "학생" only where a noun is needed.
- Keep sentences short; Korean exam instructions read best as one idea per sentence.
- Numbers, dates and times: `2026년 3월 5일 (목) 14:30`, `3회`, `5분`. Keep "UTC" as it is.
- Product names stay in Latin script: Gadfly, Gadfly Exams.

### Glossary (English to Korean)

| English | Korean | Notes |
|---|---|---|
| exam | 시험 | |
| my exams | 내 시험 | top bar and page title |
| attempt | 응시 | "attempt 2" = 2회차 응시; "attempts used 1 of 3" = 응시 1/3회 |
| examiner (the AI) | 시험관 | never 감독관 (that is a proctor) |
| AI examiner | AI 시험관 | |
| instructor | 담당 교수자 | 교수자 alone where short |
| student | 학생 | |
| get ready (page) | 시험 준비 | |
| begin exam | 시험 시작 | |
| resume | 이어서 하기 | |
| practice | 연습 | "practice run" = 연습 시험 |
| question | 질문 | not 문제 (a Socratic exam asks questions) |
| answer | 답변 | the student's spoken answer |
| follow-up question | 후속 질문 | |
| I don't know | 잘 모르겠어요 | the button and the spoken phrase |
| eye contact | 시선 맞추기 | |
| eye tracking | 시선 추적 | |
| look away | 시선을 돌리다 | |
| camera / webcam | 카메라 | |
| microphone | 마이크 | |
| system check | 시스템 점검 | |
| consent / notice | 동의 / 안내 | "notice and consent" = 안내 및 동의 |
| accommodation | 편의 지원 | e.g. typed answers = 입력 답변 편의 지원 |
| result | 결과 | |
| grade | 성적 | the published grade |
| score | 점수 | |
| published (result) | 공개됨 | |
| appeal | 이의 신청 | |
| pause / paused | 일시 정지 / 일시 정지됨 | |
| integrity note | 부정행위 방지 기록 | low-confidence notes for a reviewer; never "cheating" |
| reviewer | 검토자 | |
| transcript | 대화 기록 | |
| source material | 학습 자료 | |
| language you speak best (primary language) | 주 사용 언어 | |
| exam language | 시험 언어 | |
| help | 도움말 | |
| settings / preferences | 설정 | |

## Ticket breakdown (GAD-T526), in build order

| Order | Ticket | What | State, 22 Nov 2026 |
|---|---|---|---|
| 1 | GAD-T553 | ESL and Korean research (docs/research/esl-and-korean.md) | done |
| 2 | GAD-T534 | Session language, exam languages, localization and profile tables | done |
| 3 | GAD-T543 | UI localization framework | done |
| 4 | GAD-T539, GAD-T540 | Exam languages setting; localized exam texts with machine draft and approval | done |
| 5 | GAD-T529 to T532, T597 | Korean recognition, voice, lip-sync, fillers; keyterm cap | done |
| 6 | GAD-T548, GAD-T549 | Bilingual transcripts for graders; fairness by language | done |
| 7 | GAD-T535 to T537 | Live turn, panel judging and text heuristics in Korean | in progress |
| 8 | GAD-T544, GAD-T546, GAD-T593, GAD-T595 | Student pages in Korean; get-ready language, device and examiner pickers | in progress |
| 9 | GAD-T552, GAD-T554, GAD-T555, GAD-T594 | Language profile questions; accent-aware recognition; ESL-aware examiner; voice calibration reading | in progress |
| 10 | GAD-T541 | Draft Korean texts for exams already live (after deploy) | after deploy |
| 11 | GAD-T550 | Multilingual docs and the dissertation section | with this page |
| 12 | GAD-T596 | Spanish, later: the same steps for a third language | later |
