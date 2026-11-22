# Localization: the student interface in English and Korean

Gadfly's student interface speaks English and Korean (GAD-T526). This page is how the interface text is localized (GAD-T543),
how a student chooses a language, and the Korean glossary every translator and agent follows (GAD-T544). The exam itself (the
examiner's questions, speech recognition, grading) is localized separately: see the GAD-T526 tickets.

## How it works

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

## Adding a string

1. Add the key and English text to `en.json`, in its section.
2. Add the Korean to `ko.json` under the same key, following the style and glossary below.
3. Use `@T["key"]` in the component. Never build a sentence from fragments (word order differs); give the whole sentence one key
   with placeholders.

## Korean style

- Polite, warm and clear: the 해요체 (`-요`) for guidance, notices and buttons' explanations, and the 합쇼체 (`-습니다`)
  for formal statements (consent notices, rules, legal and privacy text, emails' formal lines). Never 반말.
- Buttons and labels are short nouns or noun phrases (`시험 시작`, `다시 확인`), not sentences.
- Address the student without "당신"; Korean leaves the subject out. Use "학생" only where a noun is needed.
- Keep sentences short; Korean exam instructions read best as one idea per sentence.
- Numbers, dates and times: `2026년 3월 5일 (목) 14:30`, `3회`, `5분`. Keep "UTC" as it is.
- Product names stay in Latin script: Gadfly, Gadfly Exams.

## Glossary (English to Korean)

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
