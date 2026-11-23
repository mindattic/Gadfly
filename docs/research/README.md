# The science behind Gadfly: research notes

These notes check the research that Gadfly's design rests on. They cover the Socratic method and oral examination, the learning science behind follow-up questions, and the evidence on AI grading, integrity signals and fairness. Each note gives, for every source, its full reference, the link that was opened, the level at which it was verified, a verified quote or labelled paraphrase, what it establishes, how it bears on a Gadfly feature, and a confidence rating. Each note ends with corrections to commonly repeated claims, sources that could not be located, and a synthesis for Gadfly.

Verification levels, the corrections found and the open items are collected in the ledger, [references/VERIFICATION.md](references/VERIFICATION.md). Nothing is described as checked unless it was. Where only an abstract or a record was seen, the note says so and keeps its claims to what that page supports.

Prepared 2026-11-21 (GAD-T254).

| Note | Domain |
|---|---|
| [01-elenchus-clinical-interview.md](01-elenchus-clinical-interview.md) | The Socratic elenchus as examination (Plato, Vlastos, Benson, Seeskin), Piaget's clinical interview, think-aloud and verbal reports (Ericsson & Simon, Nisbett & Wilson) |
| [02-oral-assessment-anxiety-fairness.md](02-oral-assessment-anxiety-fairness.md) | Validity and reliability of oral exams and vivas; test and speaking anxiety; accent, dialect and speech-recognition fairness |
| [03-retrieval-self-explanation-transfer.md](03-retrieval-self-explanation-transfer.md) | The testing effect and retrieval practice; self-explanation and elaborative interrogation; Bloom, the revised taxonomy, SOLO and transfer |
| [04-dynamic-assessment.md](04-dynamic-assessment.md) | Vygotsky's zone of proximal development, dynamic assessment (Lidz, Feuerstein, Grigorenko & Sternberg, Caffrey et al.), graduated prompts, scaffolding |
| [05-ai-examiner-llm-grading.md](05-ai-examiner-llm-grading.md) | LLM-as-judge biases, panels of models, LLM scoring of student work against human raters, human marking reliability, prompt injection, the EU AI Act's high-risk education category |
| [06-integrity-proctoring-gaze-voice.md](06-integrity-proctoring-gaze-voice.md) | Cheating in online exams, remote proctoring ethics and bias, AI-text detectors, webcam eye tracking, voice stress analysis, the AI Act's ban on emotion recognition in education |

Feasibility note: [corneal-reflection-and-room-sweep.md](corneal-reflection-and-room-sweep.md) finds that reading or detecting notes from the screen's reflection in the student's eyes is not feasible at webcam resolution (and would favour flagging glasses-wearers), and that an automated room sweep at the start buys little in an unwatched, frame-free exam (GAD-T244).

Engineering note (not part of the literature review): [stt-alternatives.md](stt-alternatives.md) compares the speech-to-text integrations in the Prose repo (Groq and OpenAI Whisper) with Gadfly's ElevenLabs Scribe recognizer (GAD-T414).

## What the evidence supports

- **Writing each follow-up from the student's own words has a long lineage.** It follows the elenchus's rule of examining only what the answerer actually believes (01) and Piaget's clinical method of following the child's answer without suggesting one (01).
- **Explaining "why" and "how" is a reasonable probe of understanding, and a moderate aid to learning** (03: self-explanation and elaborative interrogation). Explaining to a listener changes performance, usually for the better, and always slows people down (01), so slower speech and longer pauses are normal in a Gadfly exam.
- **An exam can be a learning event, if released results give feedback.** Retrieval practice reliably beats restudying for retention, but the benefit largely disappears without feedback when the student mostly fails (03).
- **Oral exams can be reliable, mostly through sampling** many objectives and topics rather than through rigid structure (02).
- **A panel of models from different families, blind human grading, reconciliation and human approval are all supported** (05), and human oversight is a legal requirement for AI that evaluates learning outcomes in the EU (05: AI Act Annex III point 3).
- **Integrity signals belong as low-confidence reviewer context, never as verdicts** (06). Webcam gaze is too coarse to resolve reading, voice-stress analysis performs at chance, and AI-text detectors are unreliable and biased against non-native writers.

## What it does not support, and what to change

- **A contradiction probe shows inconsistency, not which claim is wrong** (01). The rubric and the source material must decide that.
- **A "what would change if" question inside the student's own scenario is not transfer** (03). To probe transfer, Gadfly needs a separate question set in a situation that is new to the student.
- **Voice-strain metrics have no demonstrated validity and carry legal risk** (06). The European Commission's 2025 guidelines give detecting emotional arousal and anxiety during exams as an example of prohibited emotion recognition in education. Note 06 recommends switching voice strain off for EU tenants, not calling it "strain" or "stress", and not letting it drive harder probing. It also flags `docs/PRIVACY.md`'s "Nothing infers emotion" as exposed.
- **Speech recognition is the largest documented fairness risk** in a voice exam: error rates differ by dialect and for people who stutter (02). Measure word error rate by group, make transcript correction easy, and flag low-confidence segments so they are not scored as wrong content. A typed-answer accommodation is supported.
- **Hints and scoring must stay separate** (04). Dynamic assessment supports an optional practice mode with a fixed, instructor-written prompt ladder, reported beside the unaided score. It does not support turning prompt counts into a "learning potential" score.
- **No source tests LLM grading of spoken Socratic answers** (05). Gadfly's real agreement figures can only come from its own double-marked pilot (`docs/PILOT.md`).
