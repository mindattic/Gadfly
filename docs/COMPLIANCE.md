# Gadfly Exams: EU AI Act and licensing review (draft)

Status: draft (GAD-T453, from the ElenchusAtScale blueprint audit, GAD-T421; dissertation 5.8.2-5.8.4, Appendix A.6 and
A.8). **Not legal advice.** Every conclusion here, marked **[counsel]** where it is a legal reading, needs review by
qualified counsel before it is relied on, published or put in a contract. The privacy side is in
[PRIVACY.md](PRIVACY.md); this document covers the AI Act duties, the third-party licences and the copyright of the
quotations in the product copy. Facts about the build were checked against the code on 2026-11-22.

## 1. Classification

Gadfly is a high-risk AI system under Regulation (EU) 2024/1689 (the AI Act), Annex III, point 3, on two counts
**[counsel]**:

- **3(b), evaluating learning outcomes.** The AI panel grades every objective, its grade carries a fixed weight in the
  combined score (0.4 by default), and its judgments during the exam choose the next question.
- **3(d), monitoring and detecting prohibited behaviour during tests.** The integrity signals (gaze, faces,
  identity drift, browser focus, a second voice, text style, recitation) monitor the exam.

The Art. 6(3) exception (a system that does not materially influence the outcome, for example a narrow procedural or
purely preparatory task) is unlikely to apply: the panel's grade enters the score and steers the questions, and four
integrity signals act on the exam (the gentle gaze reminders: head tilt, a light 35% dim around the examiner, one warm spoken line,
never while the student is thinking; the camera pause; the recitation check that excludes evidence). The heavy fade and the
"look at me" remark of earlier versions were superseded by the GAD-T513 research (GAD-T557/T561). Gadfly should be treated as high-risk.

MindAttic is the **provider** (it places the system on the market under its own name). Each customer organization
that uses it is a **deployer**. A university that changes the intended purpose or puts its own name on the system
could become a provider itself (Art. 25) **[counsel]**.

## 2. When the duties apply

| What | Applies from | Source |
|---|---|---|
| Prohibited practices (Art. 5), AI literacy (Art. 4) | 2 February 2025 | Art. 113(a) |
| General-purpose AI model duties (for the LLM providers, not Gadfly) | 2 August 2025 | Art. 113(b) |
| Transparency to people interacting with an AI system (Art. 50(1)) | 2 August 2026 | Art. 113 |
| Marking of AI-generated audio, image, video and text (Art. 50(2)) | postponed to December 2026 by the Digital Omnibus on AI | see below |
| High-risk duties for **Annex III** systems such as Gadfly | **2 December 2027** (was 2 August 2026) | Digital Omnibus on AI |
| High-risk duties for Annex I products | 2 August 2028 | Digital Omnibus on AI |

The dissertation left the dates after the 2025-2026 amendments open (A.6). The Digital Omnibus on AI was adopted in
2026 (Council approval 29 June 2026), published in the Official Journal on 24 July 2026 and in force from 27 July
2026; secondary sources agree that it moves the Annex III high-risk date to 2 December 2027 and the Annex I date to
2 August 2028. These dates are taken from law-firm and EU summaries, not from the consolidated text, and must be
confirmed against the Official Journal **[counsel]**. The prohibitions already apply: Gadfly's Art. 5 position
(section 5) matters now, not in 2027.

## 3. Territorial scope

The Act applies to providers placing a system on the EU market wherever they are established, to deployers
established in the EU, and to providers and deployers outside the EU where the system's output is used in the EU
(Art. 2(1)(a), (b), (c)) **[counsel]**.

- **Provider duties do not depend on any per-student marker.** Once MindAttic offers Gadfly to any organization in
  the EU, the provider duties in section 4 apply to the system as a whole. The build's EU markers do not and cannot
  switch them off.
- **The organization marker** (Organization settings, "in the EU or EEA") tracks Art. 2(1)(b), a deployer
  established in the Union, reasonably well. Its help text also says "or examines students there", which goes
  beyond 2(1)(b) towards 2(1)(c) **[counsel]**.
- **The student marker** ("lives in the EU or EEA", set per assignment) is not the Act's test. The Act asks where the
  output is used, not where the student lives. A student living outside the EU who sits the exam for an EU university
  is in scope through the deployer. A student living in the EU who sits an exam for a non-EU organization may be in
  scope through Art. 2(1)(c) if the result is used in the EU (for example, for admission to an EU programme). The
  marker is a sensible conservative default for the voice-measure switch (section 5) and nothing more. **Open:**
  counsel to say whether the help text should read "sits the exam from, or will use the result in, the EU/EEA".
- EEA states (Iceland, Liechtenstein, Norway) apply the Act through the EEA Agreement once it is incorporated. Treating
  the EEA like the EU, as the build does, is the safe reading **[counsel]**.

## 4. High-risk duties: what Gadfly does today and what is open

Provider duties (Chapter III, Section 2 and Art. 16 onwards). Where a duty falls on the deployer, the table says so.

| Duty | What it asks (summary) | What Gadfly does today | Open |
|---|---|---|---|
| **Risk management** (Art. 9) | A documented, maintained process across the lifecycle: identify and evaluate foreseeable risks to health, safety and fundamental rights, including misuse; take measures; test against defined metrics; consider effects on minors and vulnerable groups | Risks are analysed in docs/EVIDENCE_MAP.md, docs/SCIENCE.md and the dissertation (Chapter 5: construct-irrelevant variance, fairness, integrity, the owner's decisions and their evidence). Measures are built: accommodations (time, eye contact, typed answers), the thinking grace, the EU voice default, blind grading, separation of duties. The ticket history records each decision | **No risk-management file in the Art. 9 form**: a register of risks, estimates, measures, residual risks and their acceptance, testing plan and review cycle. Minors are out of scope today (decision D6, adults only); the file should say so |
| **Data and data governance** (Art. 10) | Training, validation and testing data that are relevant, representative and examined for bias; documented data governance | Gadfly trains no model. Its testing data are the per-exam calibration sets (GAD-T270: blind double-marked answers, QWK/SMD thresholds), the bias probe and the speech-recognition audit; the pilot research export (GAD-T451) feeds the study | **Document** the calibration sets and pilot data as Art. 10 testing data: where they come from, who they represent, which groups are missing. Speaker-group data for bias checks (Art. 10(2)(f), 10(5)) is not collected by Gadfly; a study would collect it under its own consent (docs/PILOT.md) |
| **Technical documentation** (Art. 11, Annex IV) | A technical file drawn up before placing on the market, kept up to date | Much of the content exists in docs/ (architecture, SOCRATIC_PLATFORM, SCIENCE, EVIDENCE_MAP, VOICE, PRIVACY, API), the versioned prompt sets, and the dissertation | **Assemble the Annex IV file**: intended purpose, versions, hardware and software, design choices and trade-offs, the LLM providers and models, validation and testing results, accuracy metrics, human-oversight measures, the post-market monitoring plan |
| **Record-keeping / logging** (Art. 12; provider keeps logs under its control at least six months, Art. 19; deployer keeps logs at least six months, Art. 26(6)) | Automatic logging of events over the system's lifetime, enough to trace its functioning and identify risks | Append-only audit log of every grading and admin action (including refusals); the session record with the prompt-set SHA-256, the frozen policy and panel calibration status; the planner's reasons per question; every outside call timed (GAD-T413); cleared scores archived first (GAD-T262) | **Six-month floor built (GAD-T466).** The organization's retention period (as short as 7 days) redacts answers, judge text and integrity detail, including the words copied into answer audit events, but deletes no log row: audit rows are never deleted, and call-timing rows go only after `Gadfly:Logs:CallTimingDays` (a year by default, never under 184 days). An erasure keeps the student's log rows without their before/after detail or timing error text and records itself (`privacy.erased`); see PRIVACY.md, "Logs are kept at least six months". Open: whether the outcome-and-ids log that survives is "logs" enough for Art. 12, and whether keeping pseudonymous ids after an erasure is right under GDPR Art. 17(3) **[counsel]** |
| **Transparency and instructions for use** (Art. 13) | Deployers get instructions for use: provider identity, intended purpose, accuracy and its metrics, known risks, human-oversight measures, input-data specifications, expected lifetime and maintenance, log mechanisms | The admin, instructor and student guides, the public Science page and docs/SCIENCE.md describe the method, its limits and the oversight design | **Write "Instructions for use"** in the Art. 13(3) form, with declared accuracy figures per exam type once the pilot gives them |
| **Human oversight** (Art. 14) | Designed so natural persons can effectively oversee it, understand its limits, stay aware of automation bias, decide not to use or override its output, and stop it | The human weight can't be zero; human grading is blind by default; only a person approves and publishes; the scorer can't reconcile or approve unless the organization allows a single grader, audited (GAD-T285); the reconciler can't approve while someone else could (GAD-T444); the panel's calibration status is shown to graders (GAD-T270); clearing scores holds the attempt for a person to decide (GAD-T262) | Blind grading is a per-exam policy that can be turned off; graders see the integrity context while grading (the dissertation, 5.8.3, notes the anchoring). Deployers must assign competent overseers (Art. 26(2)); the instructor guide should say so in those words |
| **Accuracy, robustness, cybersecurity** (Art. 15) | Appropriate, declared accuracy; resilience to errors and to attempts to alter use or outputs (including manipulation of inputs) | Per-exam panel calibration with published thresholds; a multi-model panel with agreement recorded; the reliability tool for the number of objectives (GAD-T443); fail-closed leak guard on questions; recitation check; tenant isolation on every query, MFA, rate limits, signed webhooks | **No accuracy figure can be declared yet**: the pilot (GAD-T66) has not run. A panel calibration is not reopened when a provider changes the model behind a name (dissertation 6.6). Prompt injection through a spoken answer is guarded in the prompts but not tested as an adversarial suite |
| **Quality management system** (Art. 17) | A documented QMS: compliance strategy, design control, testing, data management, risk management, post-market monitoring, incident reporting, communication with authorities, record-keeping, resources, accountability | Ticket workflow, code review, build gates and the evidence-checked docs exist informally | **Open.** Write the QMS (proportionate to a small provider, Art. 17(2)) |
| **Documentation keeping** (Art. 18) | Keep the technical file, QMS documents and declaration ten years after placing on the market | Git history keeps docs | **Open:** a retention policy for the compliance file |
| **Corrective action, information, serious incidents** (Arts. 20, 73) | Fix or withdraw non-conforming systems; tell deployers and authorities; report serious incidents within the deadlines | Notifications to organizations exist (email, webhooks) | **Open:** an incident procedure (what counts as a serious incident for an exam system, who reports, within what time) |
| **Authorised representative** (Art. 22) | A provider established outside the EU appoints one in writing before placing on the EU market | — | **Open** if MindAttic is not established in the EU **[counsel]** |
| **Conformity assessment, declaration, CE marking** (Arts. 43, 47, 48) | For Annex III point 3: internal control (Annex VI); an EU declaration of conformity; CE marking (digital for a software service) | — | **Open** |
| **Registration** (Art. 49, Art. 71 database) | The provider registers the system in the EU database before placing it on the market; deployers that are public authorities register their use | — | **Open.** Public universities using Gadfly may have to register their use (Art. 49(3)) **[counsel]** |
| **Post-market monitoring** (Art. 72) | A plan to collect and analyse performance data over the system's life | Analytics, gaze-flag rates by group (GAD-T261), reconciliation rates, the speech-recognition audit, the research export (GAD-T451) | **Open:** the written plan, and who reviews which numbers how often |
| **Deployer duties** (Art. 26), fundamental rights impact assessment (Art. 27), right to explanation (Art. 86) | Deployers use it per the instructions, assign oversight, keep logs, inform affected persons; public bodies and private bodies providing public services do an FRIA; affected persons may ask for an explanation of a decision | Students are told before every attempt that an AI examines them and that people grade, reconcile and approve (the exam notice). Students see, per objective, which criteria fell short and why, and may appeal (GAD-T273) | Gadfly should hand deployers a template FRIA and the instructions for use. Whether the per-objective feedback meets Art. 86 is **[counsel]** |
| **AI literacy** (Art. 4, in force) | Provider and deployer staff have sufficient AI literacy | The guides explain the panel's limits and automation bias | **Open:** a short training note for graders and admins, and a record of who has read it |
| **Transparency to the student** (Art. 50(1), from 2 August 2026) | People are told they are interacting with an AI system unless obvious | The exam notice says "An AI examiner asks you questions" and the examiner is an avatar | Done in substance; counsel to confirm the wording **[counsel]**. Art. 50(2) marking of AI-generated audio (the examiner's synthetic voice) falls first on the speech provider; whether Gadfly must also mark it is **[counsel]** |

## 5. Prohibited practices (Art. 5), as handled

- **Emotion recognition in education (Art. 5(1)(f)).** Already handled in the build (GAD-T258, GAD-T259, GAD-T283) and
  described in PRIVACY.md: no state is ever named; the voice comparison no longer steers probing; voice measurements,
  and the timing cues that come from the voice, are off by default for an EU/EEA organization and for a student marked
  as in the EU/EEA; gaze and face checks look for unauthorized material, the Commission guidelines' own example of a
  use that is not prohibited (para. 255). The research export never carries voice measurements for an EU/EEA
  organization or student, whatever the setting (GAD-T451).
- **An admin turning voice measurements on for EU students.** "On for everyone" is still possible. The guidelines turn
  on what the system is used to infer, not on what its output is called (dissertation 5.8.2). Gadfly uses the numbers
  descriptively, for reviewers, never as a state or a score, which is an argument that nothing is inferred; but pitch,
  jitter and loudness are the features from which arousal or anxiety would be inferred, and a reviewer reading
  "voice measurements beyond the usual range" next to an answer is close to the guidelines' example. **Our reading:
  turning them on for EU students carries a real risk of falling within Art. 5(1)(f), with penalties at the top tier
  (Art. 99(3)). It should not be done without counsel's written view** **[counsel]**. The settings page already says
  so. **Open, owner decision:** whether to make "On" leave EU/EEA students off regardless, until counsel has answered.
  Not changed in this ticket because the setting is an owner decision (GAD-T259).
- **Biometric categorisation (5(1)(g)) and real-time remote biometric identification (5(1)(h)).** Not done.

### The identity-drift face comparison

In Strict exams the webcam library keeps a face signature in browser memory at the start and compares the face in
view with it during the exam; when they differ it records an "identity-drift" episode ("possibly a different
person"), which is sent to the server like the other camera notes, at Low severity and confidence at most 0.4.

- **AI Act.** A one-to-one comparison that confirms a person is the one who started is closest to *biometric
  verification* (Art. 3(36)), which the Act expressly leaves out of remote biometric identification (Annex III, point
  1(a); Recital 54) and does not prohibit. It adds no classification beyond Annex III 3(d) **[counsel]**. It infers no
  emotion, so Art. 5(1)(f) does not reach it.
- **GDPR.** Face geometry processed to tell whether it is the same person is biometric data processed for unique
  identification, a special category (GDPR Art. 9). That it never leaves the device probably does not take it out of
  the GDPR, since the organization decides that it runs **[counsel]**. The Strict exam notice asks for consent before
  the camera checks run; whether that consent is "explicit" in the Art. 9(2)(a) sense is **[counsel]**. US biometric
  laws (BIPA, CUBI, Washington) are covered in PRIVACY.md.
- **Build change made here.** The Strict exam notice said the same-face check "is never sent". The check's result
  (a possible change of person, as a note) is sent, though the face data is not. The wording now says so, and the
  notice version was bumped, so attempts consented under the old wording keep the Strict camera checks off as designed.

## 6. Licensing review of third-party assets and dependencies

Checked from the package metadata (.nuspec), the files in `Gadfly.Server/wwwroot` and the notices next to them. No
copyleft (GPL, LGPL, AGPL, MPL, SSPL) or non-commercial licence was found anywhere. All licence conclusions are
**[counsel]**.

| Component | Where | Licence | Status |
|---|---|---|---|
| Avatar model `assets/avatars/mpfb.glb` and `portrait.png` | wwwroot, MindAttic.Avatar | CC0 1.0 (the TalkingHead v1.8.2 sample made with MPFB; `assets/avatars/LICENSE.md` records the source and hash) | **Safe.** The other TalkingHead sample avatars are non-commercial and must never be added (the notice says so) |
| Page-turn sounds `sounds/page-turn-1.mp3`, `page-turn-2.mp3` | wwwroot | CC0 1.0 (Freesound 177867 by mmaruska and 511402 by KikeVilaplana; `sounds/NOTICE.md`) | **Safe** |
| Webcam WASM runtime `webcam/vision_wasm_internal.*` and the bundled `@mediapipe/tasks-vision` 1.2.3 | wwwroot, served to the browser | Apache-2.0 (Google) | **Done (GAD-T465).** Apache-2.0 §4 asks for a copy of the licence and any NOTICE with each redistribution; Gadfly serves these files to every browser, which is redistribution. `wwwroot/THIRD-PARTY-NOTICES.txt` carries the full licence text (the npm package ships no NOTICE file) |
| Face model `webcam/face_landmarker.bin` (MediaPipe `face_landmarker.task`, float16 v1) | wwwroot | Apache-2.0 per MediaPipe; no model card kept locally | Notice **done (GAD-T465)**; **the model card is still needed** (the card states intended uses and known limits, which Art. 13 instructions should reflect) |
| MediaPipe usage-logging call in the bundle (`odml.pa.googleapis.com`) | `webcam.browser.js` | — | **Check.** README.txt says the browser contacts no third party; whether this call ever fires was not verified |
| three.js r186, fflate 0.8.2, meshoptimizer decoder 1.1 | wwwroot/lib/voice | MIT (LICENSE and headers present) | Safe |
| @met4citizen/talkinghead 1.8.2 | wwwroot/lib/voice | MIT (LICENSE present) | Safe |
| @microsoft/signalr 11.0.0 browser client | wwwroot/lib/voice/signalr | MIT (.NET Foundation; LICENSE.txt in the npm package) | Safe; licence in the notices file (GAD-T465) |
| MindAttic.Avatar, MindAttic.SpeechRecognition, MindAttic.Legion, MindAttic.Vault, MindAttic.Log | NuGet | MIT (MindAttic) | Safe (MindAttic's own) |
| MindAttic.Webcam, MindAttic.Authentication, Tutor.Ingestion | NuGet / bundle | **No licence declared** (no LICENSE file; the last two have none in the package either) | MindAttic's own code, so no third-party risk, but each should declare its licence; MindAttic.Webcam must carry the MediaPipe notice it bundles |
| Microsoft ASP.NET Core, EF Core, Azure SDKs, System.*, IdentityModel, MailKit/MimeKit, BouncyCastle, OpenXml, Newtonsoft.Json, AngleSharp, Argon2, Humanizer and the rest | NuGet (server) | MIT | Safe |
| Serilog family, SQLitePCLRaw, PdfPig, SmartReader, Tesseract (with native Leptonica) | NuGet (server) | Apache-2.0 (Leptonica: its own BSD-style licence, not checked) | Safe for a hosted service, since server-side code is not distributed to users; the notices are still needed if Gadfly is ever shipped for on-premises use |
| `tessdata/eng.traineddata` (inside Tutor.Ingestion) | NuGet | Apache-2.0 upstream; not stated in the package | Confirm |
| VersOne.Epub | NuGet | Unlicense | Safe |
| BCrypt.Net-Next | NuGet | licence URL only (MIT upstream) | Safe; confirm |
| Microsoft.Data.SqlClient.SNI.runtime; Microsoft.Identity.Client.NativeInterop (Tour tool only) | NuGet | Microsoft Software License Terms (redistributable) | Safe for this use |
| Fonts and icons | — | System fonts only; no web fonts or icon sets; the favicon and site images are MindAttic's own | Safe |

**Gadfly's own licence.** The repository has no LICENSE file; README.md says MIT "as declared in the project files"
and three .csproj files declare MIT. **Open, owner decision:** whether the product is meant to be MIT-licensed open
source at all, since a sellable hosted product often is not. Either add a LICENSE that matches the intent or remove
the MIT declarations.

**Done (GAD-T465):** `wwwroot/THIRD-PARTY-NOTICES.txt` is served at `/THIRD-PARTY-NOTICES.txt` and linked from the
public site footer and the security page. It carries the full Apache-2.0 text, every MIT notice and the CC0 provenance
for everything sent to the browser. Whenever a file under `wwwroot/webcam`, `wwwroot/lib` or `wwwroot/assets` is
added or upgraded, update that file in the same change.

## 7. The quotations in the product copy and the public site

The original Greek and French are not the question: the English translations are separately protected works.
Terms differ by country: in the US a work published before 1931 is in the public domain from 1 January 2026; in the
EU and the UK a work is protected until 70 years after the end of the year its last author (here, translator) died.
Short, acknowledged quotation for criticism, review or illustration is allowed in all three (US fair use; EU
InfoSoc Directive Art. 5(3)(d); UK CDPA s. 30), but a marketing epigraph is the weakest case for that exception.
All of this is **[counsel]**.

| Where | Quotation | Translation | US | EU / UK | Verdict |
|---|---|---|---|---|---|
| Science page (public), get-ready page, README, docs/SCIENCE.md | Plato, *Apology* 21d, "thinks he knows something when he does not"; 30e (paraphrased on the site, quoted in SCIENCE.md) | H. N. Fowler, Loeb Classical Library, first published 1914 (Perseus serves the 1966 printing). Fowler died in 1955 | Public domain (published 1914) | Public domain since 1 January 2026 (1955 + 70) | **Safe** |
| Landing page (public), until this change | Piaget, "Vary the questions, make counter-suggestions, in short, give up all idea of a fixed questionnaire" | Joan and Andrew Tomlinson, *The Child's Conception of the World*, 1929, from *La représentation du monde chez l'enfant* (1926). Piaget died in 1980; the translators' dates are unknown | Translation probably public domain since 2025 (published 1929); confirm the US edition | **In copyright**: the French original until the end of 2050, the translation until 70 years after the later translator's death | **Replaced.** It was also a misquotation: the original reads "to vary the questions, to make counter-suggestions, in short, to give up all idea of a fixed questionnaire", and the landing page dropped words without marking it. The landing page now quotes Plato 21d (Fowler) |
| Science page (public), README, docs/SCIENCE.md | Piaget, the p. 4 passage (with ellipses) and "golden rule is to avoid suggestion" (p. 13) | Tomlinson 1929 | As above | In copyright; short scholarly quotations with the source named | **Keep, as quotation** (short, accurate, attributed, in an explanatory text). The reference now names the translators and the original. Counsel to confirm |
| get-ready page | Piaget paraphrase ("vary the questions, try a counter-suggestion...") | — | — | — | **Safe** (a paraphrase with a citation reproduces no protected expression) |
| docs/SCIENCE.md, research notes | Plato, *Meno* | W. R. M. Lamb, Loeb, 1924 (Lamb died, we believe, in 1961; confirm) | Public domain | In copyright until the end of 2031 | Short scholarly quotation in documentation only: **keep**; do not move it into product copy before 2032 |

## 8. Open items, in order

1. Counsel review of this document and of PRIVACY.md (every **[counsel]** item), before the first EU customer.
2. Owner decision: should "On for everyone" leave EU/EEA students' voice measurements off until counsel answers.
3. Counsel to confirm the six-month log floor (built, GAD-T466): that what survives redaction is enough for Art. 12, and the erasure trade-off (pseudonymous ids kept, content removed).
4. The Art. 9 risk-management file, the Annex IV technical file and the Art. 13 instructions for use, with accuracy
   figures from the pilot (GAD-T66) and the research export (GAD-T451).
5. QMS, incident procedure, post-market monitoring plan, conformity assessment, declaration, CE marking and EU
   database registration, all before 2 December 2027 if the Digital Omnibus dates are confirmed.
6. The MediaPipe model card; whether the bundled MediaPipe logging call fires. (`THIRD-PARTY-NOTICES.txt` is done,
   GAD-T465.)
7. A LICENSE decision for Gadfly itself, and LICENSE files for MindAttic.Webcam, MindAttic.Authentication and Tutor.
8. Recalibrate a panel when its provider changes the model behind a name (dissertation 6.6).
