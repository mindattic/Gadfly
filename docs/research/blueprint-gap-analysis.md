# Blueprint gap analysis: ElenchusAtScale against Gadfly

GAD-T421, 22 November 2026. The owner asked for the ElenchusAtScale dissertation
(`D:\Projects\MindAttic\Dissertation.ElenchusAtScale`, chapters 00-08, 80, 90) to be treated as the blueprint for how
Gadfly should work, with technology and transmission bottlenecks the only accepted limits. This document lists every
concrete recommendation, requirement, safeguard and open item in it, checks each against Gadfly's code on `main`
(at `fccba6d`; other agents' uncommitted work in the tree was not counted), and names the ticket that acts on each gap.

Also read: the draft corrections 44-50 and the new open items (A.10) in the GAD-T330 note, `docs/EVIDENCE_MAP.md`,
`docs/SCIENCE.md`, `docs/VOICE.md`, `docs/PILOT.md`, and the owner's recorded decisions.

**Status key.** **Built**: the code does it (file and commit cited). **Partly**: some of it, with what is missing.
**Not built**: nothing in the code does it. **Contradicted**: the code or the product's own documentation does what
the dissertation argues against. **Owner decision**: the build departs from the dissertation because of a recorded
decision of the owner; listed, no ticket. **Dissertation-side**: an open item of the dissertation itself (literature to
read), nothing for Gadfly to build.

Where two dissertation items ask for the same change they share one row.

## A. The fifteen requirements (Chapter 3.10; Tables 4.11 and 5.1)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| A1 | M1: keep the examiner apart from the grader; the grade responds only to evidence | 3.1, 3.6, 3.8 | Partly | Probing now comes only from answer content (`Gadfly/Engine/AnswerDelivery.cs`, `83739f7`, GAD-T258). But the offline grader still receives each answer's exam-time stance as a "Reviewer note" (`Gadfly.Sources/Grading/AiObjectiveGrader.cs:155`) | GAD-T430 |
| A2 | M2: common opening move and question, common rubric | 2.4.3, 3.6, 4.3.2 | Partly | Every objective opens with Elicit (`Gadfly/Engine/QuestionPlanner.cs:144`); rubric versioned per concept. The opening wording is generated afresh for each student (correction 31); no fixed instructor question exists | GAD-T436 |
| A3 | M3: no question contains or presupposes its answer; later answers contaminated | 2.3.1, 3.3, 4.3.4 | Built | Prompt rules, fail-closed leak guard and presupposition check (`ExamPrompts.cs`, `QuestionGenerator.cs`, `4574f9f`/`46827b1`, GAD-T266); contamination in `ObjectiveAssessor.IsContaminated`. How well the panel detects presupposition is unmeasured (pilot, GAD-T451) | - |
| A4 | M4: a clarification refines an answer, never a second answer | 3.2, 4.5 | Built | Clarified answer merged and judged once (`ObjectiveAssessor.cs`, at `e691015`) | - |
| A5 | M5: transfer credited only from situations new to the student | 2.7.1, 3.2, 5.2.2 | Partly | Only Apply credits transfer; ScenarioVariation recorded as extrapolation; transfer labels (`Gadfly/Engine/TransferLabels.cs`, `12c13e4`, GAD-T268). Whether an Apply situation is actually new is asserted by the prompt, never checked | GAD-T442 |
| A6 | M6: only asserted claims enter the ledger; retractions recorded | 3.4, 4.4 | Partly | Extraction excludes assent, withdrawn hedges and restatements, majority rule (`Gadfly/Engine/ClaimLedger.cs`). No retraction record: a withdrawn claim stays a premise | GAD-T441 |
| A7 | M7: contradiction is evidence about coherence; its resolution is the evidence; silence is "no contradiction detected" | 2.2.3, 3.4, 4.4 | Partly | Resolution scored on coherence, either revision legitimate (`ObjectiveAssessor.ResolutionScore`, GAD-T266); unjudged confrontation stays pending (`d78d879`). Still: only same-concept pairs with Jaccard >= 0.15 (`ClaimLedger.cs:83`), at most three; panel confidence unused; no detection reported as `Consistent = true` (`ObjectiveAssessor.cs:262`, `SessionReport.cs:55`) | GAD-T440 |
| A8 | M8: no level on one answer; insufficient evidence distinct from low understanding | 3.5, 4.5 | Built | Corroboration >= 2, ceiling tested, nothing pending; Level 0 defined as insufficient evidence (`ObjectiveAssessor.Climb`, `Gadfly/Assessment/AssessmentRules.cs`; `d78d879`) | - |
| A9 | M9: the same stopping rule for everyone; never stop on a running estimate; capacity reserved for unstarted objectives | 3.5, Prop. 3.3, 4.3.1 | Built | Deterministic sufficiency rules; planner reserves capacity (`QuestionPlanner.cs`); `MasteryModel` plays no part in settling. (Its use as a reported score is row A11) | - |
| A10 | M10: path reported beside each level (questions, moves, criteria reached, exclusions) | Prop. 3.4(c), 3.6, 4.5, 4.6.2 | Partly | Questions and follow-ups per objective shown (`Gadfly/Engine/SessionReport.cs:261`, `c6300aa`, GAD-T267), contaminated count shown. Moves used, criteria reached vs left at Level 0 (silently reweighted), and recited/assisted exclusions are not shown beside the level | GAD-T435 |
| A11 | M10 and Prop. 3.3: no mean-score or running-estimate aggregation reported as the score | 3.5, 3.6, 4.5 | Contradicted | `SessionReport.From` sets a "mastered / partial / not-demonstrated" recommendation from the running estimate and the mean (`SessionReport.cs:200-205`), shown on `ReviewList.razor:63` and `ReviewSession.razor:32`; the admin assignment and student consoles show the report's `OverallScore` as the student's score when there is no final grade (`AssignmentAdminService.cs:194-203`, `StudentAdminService.cs:255`) | GAD-T425 (fix) |
| A12 | M11: enough objectives, or the score is formative; the minimum estimated from a pilot | 3.7, 4.3.1, corr. 38 | Partly | `MinSummativeObjectives` (default 4) and the formative label (`Gadfly/Engine/Objectives.cs`, `c6300aa`). The 4 is a setting; nothing estimates variance components to set it | GAD-T443 |
| A13 | M12: judges from different families, single answers, no fixed evidence order, online judgment apart from the grade | 3.8, 4.6, 5.4.2 | Partly | Four providers (`Gadfly/Socrates/LegionTierResolver.cs`); single-answer judging (`PanelAnswerJudge.cs`); criteria and levels shuffled, evidence before level (`4c4c64c`, GAD-T269). Evidence still in exam order (`AiObjectiveGrader.cs:123`), stance notes still shown, self-preference untestable (question authorship not recorded) | GAD-T430, GAD-T431 |
| A14 | M13: blind human, weight fixed in advance, reconciliation by a reviewer independent of both judgments, aggregate thresholds on sets | 3.8, 4.9, 5.4.1 | Partly | Blind by default (`Gadfly.Server/Grading/BlindGrading.cs`), weights snapshotted and human weight never 0 (`Gadfly/Assessment/CombinedScoring.cs`, `GradingCore.cs`), scorer cannot reconcile or approve (`GradingService.cs:51-83`, `bed8d3d`/`31a028c`, GAD-T285), calibration thresholds (`Gadfly/Assessment/PanelCalibration.cs`, `bfdd7b4`, GAD-T270). Missing: calibration informs but never gates the panel's weight; the reconciler may also approve; double marking happens outside the app | GAD-T427, GAD-T444, GAD-T428 |
| A15 | M14: transcripts confirmed or corrected before grading; uncertain recognition flagged, never scored as wrong content | 3.1, 3.9, 4.7 | Partly / Owner decision | Unsure words named to the panel and marked for the human (`AiObjectiveGrader.cs:159-173`, `dbb4d41`, GAD-T265); recognizer advice recorded on the answer (`be6a6ff`). The student no longer confirms or corrects a spoken transcript (owner decision GAD-T405, see below); the WER reference this removed is ticketed | GAD-T426 |
| A16 | M15: path drivers that are not evidence never reach the score, and their effect on paths is measured by group | 3.6, 4.3.5, 5.3.3 | Partly | No voice or timing cue steers probing (`83739f7`); EU voice off (`5643444`, `9ac09ed`); gaze rates by group (`Gadfly.Server/Analytics/GazeFlagRates.cs`, `fc365e0`); read-out false-flag count (`Gadfly.Cli/ReadOutRateCommand.cs`, `b856604`). Nothing reports how often the content cues raised the pressure, by group or by first language (the cue lists are English, A.10) | GAD-T434 |

## B. Recommendations of the literature review (Chapter 2)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| B1 | Only the answerer's own assertions serve as premises of a contradiction | 2.2.3, Prop. 3.2 | Built | Ledger excludes assent; the Contradiction question quotes both of the student's claims and counts only when one question put both (`QuestionPlanner.FollowUp`, `d78d879`) | - |
| B2 | Score justifications, not introspection; avoid "how did you arrive at that?" | 2.3.2 | Not built | No rule in `ExamPrompts.Question` or the leak guard against questions about the student's own mental process | GAD-T445 |
| B3 | Improvement during the session is expected, not suspicious | 2.3.2, 3.3 | Built | Nothing in the engine or integrity signals treats late gains as suspicious | - |
| B4 | Rehearsal before a viva | 2.5.1 | Partly | Ungraded practice interview in progress (GAD-T294, GAD-T343 to GAD-T345 open); notes added that the read-back it describes no longer exists | GAD-T294, GAD-T344 (notes) |
| B5 | No clock running during pauses the student did not cause; no fixed end-of-answer silence | 2.5.1, 5.3.4 | Built | Clock stops while paused, capped (`8f0cdaa`, GAD-T263); adaptive silence (`voice-room.js`, `73b187f`, GAD-T264). Effect unmeasured (row E3) | - |
| B6 | Measure the recognizer's error rate by accent, dialect and fluency group | 2.5.2, 5.3.1, 7.3 | Partly | `Gadfly.Server/Analytics/SpeechRecognitionAudit.cs` (`d951f51`, GAD-T265) measures against student-checked transcripts, which no longer exist for spoken answers since GAD-T405 (A.9 open item). bottleneck: speech-recognition accuracy | GAD-T426 |
| B7 | Make transcript correction easy and visible to reviewers | 2.5.2 | Owner decision | Removed for spoken answers by GAD-T405 (`be6a6ff`); kept for typed answers. Listed under owner decisions | - |
| B8 | Flag segments the recognizer was unsure of, so they are not scored as wrong content | 2.5.2 | Built | `AiObjectiveGrader.cs:159-173`; recorded on each answer for reviewers (`be6a6ff`) | - |
| B9 | A typed-answer accommodation is a requirement, not a convenience | 2.5.2, 5.7.1, 6.4 | Built | Instructor-granted, audited, per student and assignment (`ExamService.SetTypedAnswerAccommodationAsync`, `19c7151`, `ea381b3`, GAD-T260). Residual reach to undeclared students is an owner-decision point (below) | - |
| B10 | Released results give feedback on what was missing, per objective | 2.6.1, 5.9 | Built | Per-objective criteria, reasons, next-level descriptor, score change since last attempt (`7d32b21`, GAD-T273) | - |
| B11 | Say in what respects a transfer question differs from the source; note sequestered transfer | 2.7.2 | Built | `Gadfly/Engine/TransferLabels.cs` (`12c13e4`) | - |
| B12 | Help belongs in a separate mode; any counted help is a fixed instructor ladder; no gain score | 2.8.3, 4.10 | Built | Learning Mode, `HintLadders`, assisted evidence excluded by default (`6381d43`, GAD-T271) | - |
| B13 | A panel of different model families | 2.9.1 | Built | `LegionTierResolver.cs`, `PanelAnswerJudge.cs` | - |
| B14 | Aggregate thresholds (.70 / .10 / .15) apply to sets of answers | 2.9.3, corr. 21 | Built | `PanelCalibration.cs` (`bfdd7b4`), weaker of two marker comparisons counts | - |
| B15 | Remove names from scripts before marking | 2.9.3 (Meadows & Billington 2005) | Not built | Student name shown in `GradingQueue.razor:51`, `GradingSession.razor:28`, `AssessmentReportView.razor:19` | GAD-T439 |
| B16 | Double marking | 2.9.3, 7.2 | Partly | Calibration accepts two human levels per item, but only as JSON posted to `POST /grading/calibration` (`PanelCalibrationService.cs:117`); no in-app second blind marker | GAD-T428 |
| B17 | Student text is untrusted input to the grader; no credit for words addressed to the grader | 2.9.4, 4.6.1 | Built | Fenced student text and flag for human review (`ExamPrompts.Judge`, `AiObjectiveGrader.cs:132`) | - |
| B18 | Detectors of machine-written text never used as integrity signals | 2.9.4, 5.6.1 | Built | The text-style estimate is switched off (`engine.AssessTextStyle = false`, `270ab3e`, GAD-T412); the code and `gadfly textstyle-rate` remain for any reinstatement | - |
| B19 | No inference of emotional states of students; voice never steers the examiner | 2.10.3, 2.11, 5.8.2 | Built | Voice and timing removed from probing everywhere (`83739f7`); voice measurements off by default in the EU (`5643444`, `9ac09ed`); reviewer line names no state | - |

## C. The formal model (Chapter 3), beyond the M-list

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| C1 | Prefer a different move over a repeated one | 3.2 | Built | Penalties for repeated and reused moves (`QuestionPlanner.FollowUp`, rule 6) | - |
| C2 | Choose the combination weight and the reconciliation threshold from pilot data; thresholds decide whether the panel may be used at all | 3.8, 5.4.1 | Partly | Defaults 0.4/0.6 and 10/20 points (`CombinedScoring.cs`); calibration recorded but an unaccepted panel keeps its weight (`GradingCore.cs:40`) | GAD-T427 |
| C3 | H1 (relayed answers are less coherent) stated as a hypothesis for a relay condition | 3.3, 5.5 | Not built | No way to run or tag a relay condition | GAD-T452 |
| C4 | Group-specific response parameters (s may fall for anxious speakers and those the recognizer serves badly) | 3.9 | Not built | No group comparison of scores or path drivers | GAD-T433, GAD-T434 |
| C5 | Coverage of criteria reported when Level-0 criteria are left out | 3.6 | Partly | See A10 | GAD-T435 |

## D. The method as built (Chapter 4), beyond the M-list

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| D1 | An instructor approves the concept map and rubric before a (summative) examination runs on them | 4.2, corr. 35, 6.8 | Partly | Publishing an assessment checks the instructor's objective criteria (`Gadfly.Server/Assessments/AssessmentService.cs:618-667`), but the panel-written concept map and per-concept rubric the exam-time judge uses need no approval | GAD-T437 |
| D2 | The judges' "never penalize fluency" instruction tested, not assumed | 4.6.1, 5.3.2 | Partly | `Gadfly.Sources/Grading/PanelBiasCheck.cs` (`4c4c64c`) exists with tests, but no command or page runs it | GAD-T432 |
| D3 | Recitation check acts on content and asks again; read-out false-flag rate measurable and suppressed under accommodation | 4.8.1, 5.5 | Built | `Gadfly/Engine/RecitationCheck.cs` (`617c5de`), `gadfly readout-rate` (`b856604`), off under extra time and in the EU (`9ac09ed`) | - |
| D4 | Gaze recorded at low confidence, never scored or a verdict; quality reported | 4.8.2, corr. 50 | Built | `Gadfly.Server/Integrity/WebcamIntegrityRecorder.cs`; calibration and quality score (`c514099`, `7746025`, GAD-T305) | - |
| D5 | Integrity context shown only after the content-based score | 4.8.2, 5.6.2 | Built | Hidden from the blind grader until their score is in (`bed8d3d`, `31a028c`, GAD-T285); analytics leak closed (`5fcdcb8`, GAD-T315) | - |
| D6 | Reviewer wording on gaze must not claim what no verified source supports | 4.8.2, corr. 34, 42, A.7 | Contradicted | `Gadfly.Server/Review/IntegrityExplanations.cs:15-18` ("People look up or away ... to think"), `:28` (off-screen reading "the most meaningful webcam signal", against 2.10.2), `:86`; `docs/SOCRATIC_PLATFORM.md:127` ("a well-supported sign of thinking") | GAD-T446 (fix) |
| D7 | Voice measurements name no state, stay off the grade, hidden from blind graders | 4.8.3 | Built | `Gadfly.Server/Voice/VoiceStrain.cs` (`5643444`, GAD-T259) | - |
| D8 | Grade status machine, audited actions, human approval and publication | 4.9 | Built | `GradeStatusMachine` (`AssessmentRules.cs`), `Permissions.cs`, append-only audit | - |
| D9 | Practice help kept apart from scored evidence | 4.10 | Built | See B12 | - |
| D10 | The examiner's wait-filler and face style must not signal how the last answer went | 2.2, 2.3, corr. 49 | Built | Fixed non-evaluative lines (`Gadfly.Server/Voice/ExaminerFiller.cs`, `b3c0012`); style per move (`ExaminerStyle.cs`, GAD-T303). Untested with students | - |

## E. Validity, fairness and integrity (Chapter 5)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| E1 | A paraphrase probe run on human examiners as well as the panel | 5.3.2 | Not built | Bias probe is panel-only and has no runner | GAD-T432 |
| E2 | Report, by group, how often the probing pressure was raised and on which cue | 5.3.3, A.10 | Not built | Pressure computed on the fly (`AnswerDelivery.Read`), never aggregated | GAD-T434 |
| E3 | Measure whether the adaptive end-of-answer silence cuts off fewer answers for anxious or second-language speakers | 5.3.4 | Not built | No cut-off measure | GAD-T434 |
| E4 | Calibration statistics broken down by group (SMD by group) | 5.4.1 | Not built | `PanelCalibration.Evaluate` is aggregate only | GAD-T427 |
| E5 | Boundary and split-panel reconciliation triggers tuned on calibration data | 5.4.1, corr. 41 | Partly | `Gadfly/Assessment/ReconciliationTriggers.cs` (`bfdd7b4`) with fixed defaults | GAD-T427 |
| E6 | Present the answers to the grader in shuffled order with stable labels | 5.4.2 | Not built | Evidence fixed in exam order (`AiObjectiveGrader.cs:123`) | GAD-T430 |
| E7 | Test self-preference by comparing grades from members whose family did and did not write the question | 5.4.2 | Not built | Chosen draft's provider not recorded (`Gadfly/Engine/QuestionGenerator.cs`) | GAD-T431 |
| E8 | Monitor panel and human score gaps by group | 5.4.3 | Not built | `AnalyticsService` reports agreement overall only | GAD-T433 |
| E9 | A student relaying answers through an earpiece | 5.5, 6.8 | Not built | No control besides the follow-up. bottleneck: webcam resolution and audio pickup cannot detect an in-ear device | GAD-T449 |
| E10 | Absent-face, second-face and changed-face episode rates by group; tracking quality by group | 5.6.1, corr. 50, A.10 | Partly | `GazeFlagRates.cs` covers look-aways, reading, fades only. bottleneck: webcam resolution | GAD-T447 |
| E11 | The gaze fade acts on the student during the answer | 5.6.1, 5.6.2, 5.7.3 | Owner decision, softened further | Decision (c), softened by grace, accommodation and rates (`fc365e0`), then superseded by the GAD-T513 research: gentle reminder ladder, 35% / 4 px, nothing while thinking (GAD-T557/T561). Listed below | - |
| E12 | Compare, within students, answers given while the screen faded with those it did not | 5.7.3 | Not built | No such analysis | GAD-T448 |
| E13 | Compare typed and spoken answers at the same level | 5.7.1, A.10 | Not built | Typed answers stored with mode `text` (GAD-T260), never compared | GAD-T438 |
| E14 | Decision (b): voice strain drives probing | 5.7.2 | Built (resolved) | Reversed by the owner (GAD-T258, `83739f7`), as the dissertation's evidence argued | - |
| E15 | Clearing scores keeps the oversight trail and tells the student | 5.7.4 | Built | Audit copy before removal, student told (`95f4904`, GAD-T262). The attempt still counting while feedback is withdrawn is an owner decision (below) | - |
| E16 | Territorial scope of the EU markers; an admin turning EU voice measurements on; facial comparison under the Act; high-risk duties; application dates | 5.8.2, 5.8.4, A.6, A.8 | Not built | Unread provisions; nothing in the code or `docs/PRIVACY.md` settles them | GAD-T453 |
| E17 | A "calibrated" label may increase deference; reconciliation is not protected by blindness | 5.8.3 | Not built | Label shown on the grading page as is | GAD-T427 |
| E18 | The record: audit log, versioned prompts, replayable planner, frozen policy | 5.9 | Built | `GradingAudit`, `PromptSetSha256` in `SessionReport`, deterministic `QuestionPlanner`, `PolicySnapshot` | - |
| E19 | The student can see, and answer, the integrity context recorded about them | 5.9 | Not built | Student copy of the transcript leaves integrity notes out (GAD-T295), no student view | GAD-T450 |
| E20 | One person should not judge, reconcile and approve | 5.9, 3.8, corr. 47 | Partly | Scorer barred (GAD-T285); reconciler may also approve; single-grader waiver audited | GAD-T444 |
| E21 | Blindness is a configurable default | 4.9, 5.8.3 | Built | Turning it off needs an approved, audited grading-policy change (`AssessmentService.cs`, `GradingPolicyChanges`) | - |

## F. Limitations (Chapter 6)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| F1 | Instruments exist but none has been run on real examinations | 6.1 | Partly | Calibration, bias probe, WER, gaze rates, read-out count all built; pilot not run (GAD-T66 open) | GAD-T66 (note), GAD-T451 |
| F2 | Neurodivergent students: remedies reach only declared accommodations | 6.4 | Partly | Eye-contact accommodation (`fc365e0`), read-out suppressed under extra time (`b856604`), typed answers (GAD-T260); no measure of undeclared students | GAD-T434 |
| F3 | Languages other than English: cue lists, recitation thresholds and prompts are English | 6.4 | Partly | `AnswerDelivery.cs` hedging/vague lists; per-exam language being added (GAD-T357, uncommitted) | GAD-T357 (note), GAD-T434 |
| F4 | Students without stable equipment or a private room | 6.4 | Partly | Clock stops during pauses (`8f0cdaa`); who is disadvantaged is not measured | GAD-T434 |
| F5 | Objective order and learning during the session change what is reported | 6.5, 3.9 | Partly | Turn order recorded per session; no analysis or export | GAD-T451 |
| F6 | Calibration expires when a provider changes the model behind a name; re-run probes for correlated change | 6.6, A.8 | Not built | Calibration keyed by a free-text panel name (`PanelCalibrationService.cs:75`) | GAD-T429 |
| F7 | Reproducibility: record prompt set, members' votes, models and dissent | 6.6 | Built | Per-vote provider and model shown (`ReviewSession.razor:203`), prompt-set hash in reports | - |
| F8 | An adversarial (prompt-injection) condition | 6.8 | Not built | No injection probe set | GAD-T432 |

## G. What the planned study needs Gadfly to record or expose (Chapter 7)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| G1 | Agreement between the panel and blind human examiners (QWK, SMD) on live data | 7.1, 7.3 | Partly | Statistics in `PanelCalibration.cs`; live analytics give exact/within-one agreement only (`AnalyticsService.cs:51-55`) | GAD-T428 |
| G2 | Agreement between human examiners; the double-marked calibration set | 7.1, 7.2 | Partly | Two human levels accepted only through the API | GAD-T428 |
| G3 | Reliability as a function of the number of objectives | 7.1, 3.7 | Not built | No variance-component estimate | GAD-T443 |
| G4 | Whether students who understand but express it poorly can still show it | 7.1 | Partly | Disfluent paraphrase probe exists, not runnable | GAD-T432 |
| G5 | A relay condition; whether relayed answers survive follow-up | 7.1, 7.2 | Not built | No study-arm tags | GAD-T452 |
| G6 | Word error rate by speaker group | 7.3 | Partly | Reference transcripts gone (B6) | GAD-T426 |
| G7 | Per-objective score change between attempts with and without feedback | 7.3 | Partly | Change since last attempt shown (`7d32b21`); no feedback-withheld arm | GAD-T452 |
| G8 | Rate and causes of reconciliation | 7.3 | Partly | Rate in `ReliabilitySummary`; causes recorded per reconciliation but not reported or exportable | GAD-T451 |
| G9 | Consent, data minimization, treatment of integrity signals in the study | 7.5 | Partly | Exam consent and privacy rules exist (`docs/PRIVACY.md`); no research consent | GAD-T452 |
| G10 | One de-identified dataset for analysis | 7.1-7.3 | Not built | No research export | GAD-T451 |

## H. Appendix A open items and the corrections drafted on GAD-T330 (44-50, A.10)

| # | Item | Section | Status | Evidence | Ticket |
|---|---|---|---|---|---|
| H1 | Read primary works cited through secondary sources; paywalled figures; full texts of Wass, Koenecke, Huxham, Fox; measurement theory | A.6, A.7 | Dissertation-side | Literature work for the dissertation, not the app | - |
| H2 | Plato translations' copyright | A.6 | Not built | Quoted in product copy (`Components/Pages/Site/Science.razor`, `Student/StartExam.razor`) | GAD-T453 |
| H3 | Gaze aversion during thinking: find the literature or soften the reviewer wording | A.7, A.8 | Contradicted | See D6 | GAD-T446 |
| H4 | Text-style estimate needs its own evidence before it is shown to graders | A.8, corr. 46 | Owner decision | Disabled (GAD-T412); applies only if reinstated | - |
| H5 | Facial comparison and the Act's provisions on biometric verification | A.8 | Not built | `identity-drift` episode in `IntegrityExplanations.cs:25` | GAD-T453 |
| H6 | High-risk duties; territorial scope | A.8 | Not built | See E16 | GAD-T453 |
| H7 | Calibration expiry | A.8 | Not built | See F6 | GAD-T429 |
| H8 | A reference transcript for the recognizer's error rate | A.9 | Not built | See B6 | GAD-T426 |
| H9 | Content cues by language | A.10 (corr. 44) | Not built | See E2 | GAD-T434 |
| H10 | The typed group compared before typed and spoken are compared | A.10 (corr. 45) | Not built | See E13 | GAD-T438 |
| H11 | Gaze tracking quality by group | A.10 (corr. 50) | Not built | See E10 | GAD-T447 |
| H12 | Project documentation matches the code | 6.7, corr. 42-50 | Contradicted | `docs/EVIDENCE_MAP.md` mechanic 6 (clock runs during pauses, fixed 4 s; changed by GAD-T263/T264), mechanic 9 and decision (a) (text-style running; disabled by GAD-T412), decision (d) (GAD-T262 "proposed"; done), mechanic 4 (Fox 2011 cited for gaze, corr. 42); `docs/SCIENCE.md` (WER by group and unsure-word flags "not yet built"; GAD-T265 built them); `docs/PILOT.md` (the removed read-back) | GAD-T454 (fix) |

## Counts

| Status | Rows |
|---|---|
| Built | 30 (one of them, E14, built by reversing an owner decision) |
| Partly | 33 (A15 also rests partly on an owner decision) |
| Not built | 30 |
| Contradicted | 4 |
| Owner decision | 3 |
| Dissertation-side | 1 |
| **Total** | **101** |

(Rows that point to another row, "See ...", are counted under the status they show.)

## Tickets filed (priority order)

Validity and fairness first, then integrity, then the rest. Each cites the section it acts on.

| Ticket | Name | Type | Acts on |
|---|---|---|---|
| GAD-T425 | Running estimate shown as score | fix | A11 |
| GAD-T426 | Human-reference transcripts for WER (bottleneck: speech-recognition accuracy) | feature | A15, B6, G6, H8 |
| GAD-T427 | Calibration gates panel weight | feature | A14, C2, E4, E5, E17 |
| GAD-T428 | In-app blind double marking | feature | A14, B16, G1, G2 |
| GAD-T430 | Grader isolated from exam-time judgments | feature | A1, A13, E6 |
| GAD-T435 | Report path per objective | feature | A10, C5 |
| GAD-T434 | Path-driver rates by group | feature | A16, E2, E3, F2-F4, H9 |
| GAD-T433 | Score gaps by group | feature | C4, E8 |
| GAD-T437 | Rubric approval before summative | feature | D1 |
| GAD-T436 | Fixed opening question | feature | A2 |
| GAD-T439 | Anonymous human marking | feature | B15 |
| GAD-T444 | Separate reconciler and approver | feature | A14, E20 |
| GAD-T429 | Calibration expires on model change | feature | F6, H7 |
| GAD-T431 | Self-preference check | feature | A13, E7 |
| GAD-T432 | Runnable panel and human bias probes | feature | D2, E1, F8, G4 |
| GAD-T443 | Reliability by objective count | feature | A12, G3 |
| GAD-T438 | Typed versus spoken comparison | feature | E13, H10 |
| GAD-T440 | Contradiction detector honesty (bottleneck: panel latency per pair) | feature | A7 |
| GAD-T441 | Record claim retractions | feature | A6 |
| GAD-T442 | Check Apply novelty | feature | A5 |
| GAD-T445 | No introspection questions | feature | B2 |
| GAD-T446 | Unsupported gaze reviewer wording | fix | D6, H3 |
| GAD-T447 | Camera episode rates by group (bottleneck: webcam resolution) | feature | E10, H11 |
| GAD-T448 | Fade cost measurement | feature | E12 |
| GAD-T449 | Earpiece relay countermeasure (bottleneck: webcam resolution and audio pickup) | feature | E9 |
| GAD-T450 | Student sees integrity record | feature | E19 |
| GAD-T451 | Pilot research data export | feature | F1, F5, G8, G10 |
| GAD-T452 | Study conditions and research consent | feature | C3, G5, G7, G9 |
| GAD-T453 | AI Act and licensing review | feature | E16, H2, H5, H6 |
| GAD-T454 | Evidence docs out of date | fix | H12 |

Notes added to existing tickets: GAD-T294 and GAD-T344 (rehearsal, 2.5.1; the read-back they describe was removed by
GAD-T405), GAD-T361 (its read-back correction conflicts with GAD-T405; its unsure-word rates feed GAD-T426), GAD-T357
(English-only cue lists and thresholds, 6.4), GAD-T358 (vocabulary hints as the recognizer-accuracy mitigation),
GAD-T66 (what the pilot must collect for Chapter 7), GAD-T244 (related earpiece gap).

## Owner decisions the research disagrees with

No ticket was filed for any of these. Each is the owner's to make; the dissertation's position is recorded beside it.

1. **Spoken answers are taken as heard, with no read-back (GAD-T405, `be6a6ff`).** Owner: a chance to rephrase after
   hearing the transcript gives the student a second, considered pass and changes the Socratic exchange. Dissertation:
   M14 asks that transcripts be "confirmed or corrected before they are graded" (3.10), and 2.5.2 asks that correction be
   "easy and visible to reviewers", because the recognizer is the largest documented fairness risk and its errors fall on
   identifiable groups (Koenecke et al. 2020; Mujtaba et al. 2024). With no student check, uncertain recognition is only
   flagged to graders, and the WER-by-group measure lost its reference (correction 43). GAD-T426 restores the measure
   without reinstating the read-back.
2. **Voice and camera mandatory, typing only as a granted accommodation (GAD-T260, decision (a) narrowed).** The
   dissertation's main objection (2.5.2, 5.7.1: a typed route is "a requirement rather than a convenience") is now met
   for declared needs. It still holds that the remedy "does not reach a student who has not declared a need" (6.4), and
   that the integrity case for voice is weaker than it looks, since the follow-up question, the best-supported control,
   works on typed answers too (5.7.1). GAD-T438 measures the typed group without changing the decision.
3. **Probing pressure from answer content, never voice (GAD-T258).** Not a disagreement: the dissertation argued for
   this (5.7.2). Its one caution is that the content cues are English word lists and may fire more for second-language
   speakers (correction 44, A.10); GAD-T434 measures that.
4. **The text-style check is disabled (GAD-T412).** The dissertation agrees with not using it (2.9.4: detectors "should
   never be used as integrity signals"), but the owner disabled it for latency and kept it to "come back later behind a
   setting" (correction 46). The dissertation's position is that it should not return unless its false-flag rate by
   group and first language has been measured first (A.8).
5. **Gaze fade and "Please look at me" (decision (c)).** Owner: a lawful deterrent, softened by grace, accommodation and
   rates by group (GAD-T261, GAD-T305). Dissertation: the fade acts on the student during the answer on a signal that
   "cannot tell thinking from reading" (5.6.1), adds pressure where anxiety does harm (Woldeab & Brothen 2019), may fire
   unequally across groups (Yoder-Himes et al. 2022), and so changes the conditions of the examination (5.7.3, 5.6.2).
   GAD-T447 and GAD-T448 measure it without changing it.
   **Update (GAD-T513 research, GAD-T557/T561):** the heavy fade and "Please look at me" no longer exist. Reminders never fire
   while the student is thinking; the ladder is the examiner's head tilt, then a light 35% / 4 px dim around her that clears at
   once with a cooldown, then at most one warm line; no white-out. This answers most of the objection above; the dim still acts
   during the answer, so GAD-T448 keeps measuring it (it now counts only that dim).
6. **Cleared scores hold the attempt out of grading, and the attempt still counts (decision (d)).** GAD-T262 answered
   two of the three objections. The dissertation's remaining objection is that the cleared attempt counts against the
   student's limit while the feedback that makes a further attempt worthwhile is withdrawn (5.7.4; Rowland 2014).
7. **One fast model words the live question and runs its leak guard (GAD-T494).** Owner: the spoken exam has to feel like a
   conversation. The bottleneck on production was the panel vote that drafted each question (about 7.5 s) followed by
   a second panel vote for the leak guard (about 4.1 s), while the examiner's voice starts in about 0.17 s. Dissertation:
   the multi-family panel drafts and guards the questions as well as judging the answers (3.8, 4.3.4), so that no single
   model family shapes the exam path. What still holds: the leak guard still runs before every question is asked and
   still fails closed (A3/M3), the panel still judges every answer (A13/M12), and question authorship is recorded
   (`live-model-draft`, GAD-T431), so self-preference stays testable. The single model's leak-detection rate against
   the panel's is unmeasured.
   **Extended by GAD-T508: the live steering uses one model, the grading still uses the full panel.** The panel's
   judgment of an answer no longer blocks the next question. On the turn, the same fast model scores the answer
   against the same rubric (a provisional judgment, metered as `live-judge`, timed as `live.judge`), and the planner
   chooses the next move (probe, mastery, aporia) from it. The full multi-model panel judgment (aporia check, judges,
   analysis, claims and contradiction checks) runs in the background right after the turn. It is the only judgment that
   is graded, reported, reviewed or sent as evidence, and once it arrives the planner plans from it, with the
   provisional judgment used only for turns whose full judgment is still pending. The exam never ends, and its report
   is never written, before every full judgment is in. What this departs from: the dissertation has the panel's verdict
   shape the exam path as well as the grade (3.8), so that no single model family steers the dialogue; now one family
   steers it, for at most one or two turns per answer. What still holds: no grade, mastery level or reported result
   rests on the single model, and every material disagreement between the provisional and the panel's judgment (an
   honest "I don't know" heard by one and not the other, a different evidence result, or 0.25 or more apart) is kept
   on the turn and in the audit trail (`judgment.provisional.disagreed`), so the single model's steering error is
   measurable. How often it steered differently from the panel, and whether that changed which objectives were
   probed, is unmeasured.

## Addendum, 26 November 2026: since `fccba6d`

**Closed since this analysis.** Every ticket in "Tickets filed" above (GAD-T425 to GAD-T454) has closed. The rows and
counts above still record the code at `fccba6d` and were not re-graded row by row; read them with this addendum.

**Pure elenchus cleanup (epic GAD-T808, planned at `0df308e`, landed by `9923ca8`).** Checked against the code, not
against this table; only gaps still present in the code were filed. The rows each task acted on:

| Ticket | What changed | Rows |
|---|---|---|
| GAD-T814 | The fixed English, Spanish and Korean fallback questions are open questions of at most 40 words: no yes/no, presupposing or process-report forms. A Contradiction fallback quotes both ledger claims; one that cannot ask its move is recorded as the move it asks (`QuestionGenerator.LastMove`), so it never credits transfer. | A3, A5, B2 |
| GAD-T815 | One question per turn: a draft is cut after its first question, and a first question past `QuestionGenerator.MaxQuestionWords` (50) is redrafted; on the streamed path the recorded question is what was said. The mock panel's questions follow the same rules. | A3, A4 |
| GAD-T816 | Consistency is scored only on a Contradiction move, as the quality of the resolution (prompt set v11, `PanelAnswerJudge.WeightsFor`). | A7, B1 |
| GAD-T817, GAD-T818 | Closing reflection: when the examiner ends the exam it asks, aloud, which claim the student is least sure they could defend; answered aloud or skipped, never a turn or grade input (`Gadfly.Server/Sessions/ClosingReflection.cs`). | B10, A7 (2.6.1, 3.4) |
| GAD-T819 | No delivery measures in the student's live view: no response delay, no voice comparison; stored metrics and reviewer views unchanged. | B19, D7 |
| GAD-T820 | The get-ready page no longer claims the voice comparison can add a follow-up question, and says the examiner will not confirm, hint or correct. | B4, B19, E14 |
| GAD-T821 | The student's published result lists the contradictions put to them in their own words, withdrawn ones marked, with no verdict, and their answered reflection; staff reports carry neither. | A7, B1, B10, E18 |
| GAD-T823 | `ConceptStatus` is NotStarted, Open, SettledConceptualOrAbove, SettledBelowConceptual, Inconclusive: "aporia" is kept for an honest "I don't know", and insufficient evidence is not called "exhausted". Older stored names are read as the new ones; analytics CSV headers renamed. | A8, A11 |
| GAD-T822 | The `ExamPromptsV3` alias is gone; code and docs name `ExamPrompts` (prompt set id, version and SHA-256 unchanged). | E18, H12 |
| GAD-T824 | `docs/DESIGN.md`, `docs/VOICE.md`, `docs/SOCRATIC_PLATFORM.md`, `docs/SCIENCE.md`, `docs/EVIDENCE_MAP.md`, `docs/API.md` and the instructor guide describe the above (stale read-back student flow, live-view voice line, concept status names). | H12 (6.7) |
