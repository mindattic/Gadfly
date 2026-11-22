# The science behind Gadfly

Gadfly takes its name and its method from Socrates. This page explains what the Socratic elenchus is, why
follow-up questions built from a student's own answers say more about understanding than a single answer
does, where the method stops working, and what Gadfly does about each limit. It also says plainly where
Gadfly does not yet do what the research recommends.

Every citation here comes from the verified research notes in [docs/research/](research/README.md) and their
ledger, [VERIFICATION.md](research/references/VERIFICATION.md) (GAD-T254, all sources retrieved 2026-11-21).
Each reference at the end gives the level at which it was checked: **full text read**, **abstract or
publisher page read**, or **record located only** (where the content is known from another source that the
note names). Nothing below says more than that level supports.

## The elenchus: asking until the answer has to hold together

In Plato's *Apology* Socrates describes going to people reputed to be wise and questioning them, and finding
that "this man thinks he knows something when he does not" (21d, full text read). He calls himself a gadfly
attached to the city "as a gadfly to a horse, which, though large and well bred, is sluggish ... and needs to
be aroused by stinging" (30e). The questioning is aimed at a claim to know, not only at one sentence.

The scholarly name for this questioning is the *elenchus*. In Vlastos's influential definition (as quoted,
with his page numbers, by Wolfsdorf 2013), a thesis "is debated only if asserted as the answerer's own belief
and is regarded as refuted only if the negation of his thesis is deduced from his own beliefs." Two features
carry over directly into Gadfly:

- **Only the student's own words count.** The examiner works from what the student actually said, not from
  premises it supplied and the student merely agreed to (Benson's "doxastic constraint", as summarized in
  Wolfsdorf 2013).
- **The test is coherence.** The answers are asked to fit together. When two of them conflict, something in
  the student's grasp of the idea is not yet secure.

Gadfly's examiner works the same way. It opens with "what is it?", then writes every follow-up from the
previous answer: what did you mean by that word, why is that true, what would change if, here is a case that
doesn't fit, and, when two of the student's claims conflict, "can both of these be true?".

## Why follow-up questions reveal understanding

**A single correct answer can be borrowed.** In the *Meno*, Socrates separates confident but wrong belief,
felt perplexity, and true opinion that is not yet knowledge, and suggests that being "repeatedly asked these
same questions in a variety of forms" is what turns the last into understanding (85c, full text read). One
right definition can be recalled, guessed or pasted. Explaining it, defending it against a counterexample and
using it somewhere new is much harder to do without understanding it. Benson's reading of the elenchus
(Benson 1995 and 2000, publisher pages read) is that the knowledge it tests is demanding and systematic, not
the ability to recall an answer.

**Following the answer is a recognized research method.** Piaget's clinical interview turned "follow the
child's answer" into a discipline: "vary the questions, ... make counter-suggestions, in short, ... give up
all idea of a fixed questionnaire" (Piaget 1929, p. 4, full text read). An answer that survives a
counter-suggestion is better evidence than one stated once. Gadfly's Counterexample move plays that role, and
its re-testing of a concept through several moves follows from it.

**Not suggesting the answer matters as much as asking.** Piaget's "golden rule is to avoid suggestion, that is
to say to avoid dictating a particular answer" (p. 13), and he warns that even the form of a question ("what
makes the sun move?") can plant its answer. Gadfly's clarifying question is written to be neutral for this
reason. The *Meno* is not the model here: many of Socrates' questions to the slave boy are leading yes/no
questions (84d-85b), so Gadfly follows Piaget instead.

**Asking "why" probes understanding, and also helps learning.** Prompting learners to explain improves
learning (Chi et al. 1994; Bisra et al. 2018, a meta-analysis that found question-form prompts at least as
good as instructions; both full text read), and Dunlosky et al. (2013, full text read) rate elaborative
interrogation and self-explanation as of moderate use. The fit is partly an analogy: answering an examiner is
explaining to someone, not self-explanation in the strict sense.

**Content is what gives copied answers away.** Across 158 cues, the deception cues with some support concern
content: liars "tell less compelling tales" (DePaulo et al. 2003, abstract read). A follow-up built from the
student's own previous answer asks them to explain, extend and defend it, which a scripted or AI-written
answer does not carry with it. This is the reasoning behind Gadfly's approach to integrity. Direct evidence
that conversational follow-up reduces cheating was **not** verified (see the limits below).

**An exam can teach as well as measure.** Retrieval practice reliably beats restudying for later retention
(Roediger & Karpicke 2006; Rowland 2014; both full text read), and open, recall-type questions like Gadfly's
are the format that helps most (Rowland 2014).

## The limits, and how Gadfly covers them

| Limit the research found | What Gadfly does |
|---|---|
| **A contradiction shows inconsistency, not which claim is wrong.** Vlastos's "problem of the elenchus" is that a refutation shows only that a thesis clashes with other things the answerer agreed to (Vlastos 1983, record located only, quoted via Wolfsdorf 2013). Benson argues that is all the elenchus ever claimed (Benson 1995). | The "can both be true?" question quotes the student's two claims back and lets them revise either. Which claim is right is decided by the instructor's rubric and the cited course material, not by the contradiction. |
| **There is no single "Socratic method."** Vlastos later conceded his account fits only the *Gorgias* (Wolfsdorf 2013). | Gadfly's question planner chooses among a dozen moves (Elicit, Clarify, Probe, CausalReasoning, Counterexample, ScenarioVariation, Apply, Contradiction, AssumptionTesting, IndependentExplanation, Synthesize), not one procedure, and does not claim to reproduce "the" Socratic method. |
| **Explaining to a listener changes the explainer.** Plain think-aloud did not change accuracy across 94 studies, but describing and explaining raised performance, and every form of verbal report slowed people down (Fox, Ericsson & Best 2011, abstract read; Ericsson & Simon 1998, full text read). | Gadfly is an explaining procedure, so it is honest to describe it as a learning event as well as a measure. Judges are told to ignore filler words and false starts; pauses and speech rate never enter the grade. Reading late-session gains as knowledge built during the exam, rather than brought to it, is still a recommendation (note 01), not a report feature. |
| **People's accounts of why they answered may be reconstructions** (Nisbett & Wilson 1977, abstract read). | Each judge grades only against the cited source passages and the rubric criteria, crediting correct understanding in the student's own words. A justification is scored on whether it is right and connected, not as a report of the student's mental process. |
| **Adaptive follow-ups trade comparability for depth, and examiners can mistake articulateness for knowledge** (Joughin 2010, full text read). | Each objective opens with an Elicit question ("what is it?") and is scored against the instructor's rubric, not against how far the thread went. Judges are told never to penalize grammar, accent or fluency. |
| **Oral exams are reliable through sampling, not structure.** "One measure is no measure" (van der Vleuten et al. 2010, full text read); content sampled accounted for more score variance than examiners did (Wass et al. 2003, abstract read). | Exams cover several objectives, each scored separately, by a panel of models and an independent human. |
| **LLM judges have biases** (position, verbosity), and humans shown an LLM verdict changed their own choice about a third of the time (Zheng et al. 2023, full text read). Panels from different model families reduce single-model bias (Verga et al. 2024, preprint, full text read). Human marking of open answers is itself unreliable (Shermis 2014, full text read). | A panel of models from different families scores each objective; a human examiner grades blind by default; large disagreements go to reconciliation; nothing reaches a student until an authorized person approves it. The EU AI Act treats AI that evaluates learning outcomes as high-risk and requires human oversight (Regulation (EU) 2024/1689, Annex III point 3 and Art. 14, full text read). |
| **Speech recognition is the largest documented fairness risk.** All five systems tested showed "substantial racial disparities" (Koenecke et al. 2020, abstract read), and accuracy is worse on stuttered speech (Mujtaba et al. 2024, abstract read). | The original answer is kept as given; judges are told to ignore transcription errors that do not change meaning and never to penalize accent. Measuring error rates by speaker group and flagging low-confidence segments to graders are open recommendations from note 02, not yet built. |
| **Some students cannot be examined fairly by voice.** Oral assessment guidance recommends alternative arrangements for undue anxiety and adjustments for speech or hearing impairment (Joughin 2010, full text read), and recognition is worst for some dialects and for stuttered speech (Koenecke et al. 2020; Mujtaba et al. 2024, abstracts read). | Exams stay spoken by default (owner decision (a)). An instructor or admin can grant one named student on one exam a typed-answer accommodation, audited (GAD-T260): the examiner still speaks the questions, the camera and gaze check still run, and only the answer is typed, stored and shown to reviewers as typed. Pastes into the answer box are noted for review rather than blocked, because assistive tools insert text that way; the follow-up questions built from the student's own words remain the main integrity control, and they work on typed answers too (note 06). Whether typed and spoken answers to the same questions score alike is not yet measured. |
| **Webcam gaze is too coarse to tell reading from thinking.** Webcam gaze error is roughly 100-250 pixels, about 4 degrees (Papoutsaki et al. 2016, full text read), while reading moves in jumps of 7-9 letters with fixations of about a quarter-second (Rayner 2009, full text read). | Camera and gaze events are reviewer-only annotations with confidence capped at 0.4 and severity never above Low. They are never a verdict and never part of the grade. |
| **Voice-stress analysis has no demonstrated validity.** Examiners using a commercial analyser "averaged near chance (42-56%)" even for stress (Harnsberger et al. 2009, abstract read); a field test did no better than chance (Damphousse 2007, full text read); the National Research Council found little scientific basis (NRC 2003, full text read). The European Commission's 2025 guidelines give detecting "emotional arousal and anxiousness" during online exams as an example of prohibited emotion recognition in education (full text read). | The voice-strain line is descriptive only, shown to reviewers with the caveat that effort, nerves, a hard question or the room change the voice, hidden from blinded graders, and never part of the grade or an integrity event. Note 06's three recommendations are now in place: the line names no state (GAD-T259), it is off by default for EU tenants and EU-resident students (GAD-T259), and since GAD-T258 (the owner reversed decision (b) on 2026-11-21) `strain.vsBaseline` no longer steers probing anywhere. How hard the examiner presses comes from what the answer says (hedging, vague wording, a missing reason or example), the judges' score and evidence, and recitation; the wait before answering and the hesitation count are recorded for reviewers but no longer steer it either, since they are weak evidence and move with nerves, accent and accommodations ([EVIDENCE_MAP.md](EVIDENCE_MAP.md) mechanic 2, [PRIVACY.md](PRIVACY.md)). |
| **AI-text detectors are unreliable** (Weber-Wulff et al. 2023, abstract read) and flagged most essays by non-native writers as AI (Liang et al. 2023, abstract read). | Gadfly uses no AI-text detector. Answer-text style is reviewer-only context. |
| **Testing helps learning only with feedback when students mostly fail** (Rowland 2014), and benefits do not spread to untested material (Pan & Rickard 2018, full text read). | No feedback or hints during the exam (the never-hint rule). Repeat attempts pay off only if released results say, objective by objective, what was missing. |
| **A "what would change if" question inside the student's own scenario is extrapolation, not transfer** (Bloom et al. 1956, full text read). | Only Apply, a situation new to the student, earns transfer credit: Level 5 and "transfer shown" need a good Apply answer, and the response analysis records a ScenarioVariation what-if as application (extrapolation). Each Apply question is labelled by how its situation differs from the source (purpose, setting, people, time scale, form, or subject area, after Barnett and Ceci 2002), and its reason notes that the transfer measured is sequestered, without resources or hints, so it may understate the student (GAD-T268). |

## What the research does not show

- That a Socratic oral exam is a more valid measure of understanding than other formats. Gadfly makes no
  validity claims until its own pilot ([PILOT.md](PILOT.md)) measures them.
- That someone relaying another source's answers tends to contradict themselves. That is a product hypothesis,
  not a finding.
- That conversational follow-up reduces cheating. The one candidate source was located but not read.
- How well LLMs grade spoken, transcribed Socratic answers. No source tests it; Gadfly's double-marked pilot is
  the only route to that number.
- Anything about neurodivergent students in oral or voice exams; no source was verified.
- That gaze or voice signals can detect cheating, lying or stress. Gadfly does not claim to detect any of these.

## References

Levels are as recorded in the [verification ledger](research/references/VERIFICATION.md). Full entries, links
and what each source does and does not establish are in the note named in brackets.

- Benson, H. H. (1995). The dissolution of the problem of the elenchus. In C. C. W. Taylor (Ed.), *Oxford Studies in Ancient Philosophy* (Vol. 13, pp. 45-112). Oxford University Press. https://doi.org/10.1093/oso/9780198250005.003.0004 — abstract/publisher page read [01]
- Benson, H. H. (2000). *Socratic wisdom: The model of knowledge in Plato's early dialogues*. Oxford University Press. https://doi.org/10.1093/oso/9780195129182.001.0001 — abstract/publisher page read [01]
- Bisra, K., Liu, Q., Nesbit, J. C., Salimi, F., & Winne, P. H. (2018). Inducing self-explanation: A meta-analysis. *Educational Psychology Review, 30*(3), 703-725. https://doi.org/10.1007/s10648-018-9434-x — full text read [03]
- Bloom, B. S. (Ed.), Engelhart, M. D., Furst, E. J., Hill, W. H., & Krathwohl, D. R. (1956). *Taxonomy of educational objectives: The classification of educational goals. Handbook I: Cognitive domain*. David McKay. — full text read [03]
- Chi, M. T. H., de Leeuw, N., Chiu, M.-H., & LaVancher, C. (1994). Eliciting self-explanations improves understanding. *Cognitive Science, 18*(3), 439-477. https://doi.org/10.1207/s15516709cog1803_3 — full text read [03]
- Damphousse, K. R., Pointon, L., Upchurch, D., & Moore, R. K. (2007). *Assessing the validity of voice stress analysis tools in a jail setting* (NCJ 219031). National Institute of Justice. https://www.ojp.gov/pdffiles1/nij/grants/219031.pdf — full text read [06]
- DePaulo, B. M., Lindsay, J. J., Malone, B. E., Muhlenbruck, L., Charlton, K., & Cooper, H. (2003). Cues to deception. *Psychological Bulletin, 129*(1), 74-118. https://doi.org/10.1037/0033-2909.129.1.74 — abstract/publisher page read [06]
- Dunlosky, J., Rawson, K. A., Marsh, E. J., Nathan, M. J., & Willingham, D. T. (2013). Improving students' learning with effective learning techniques. *Psychological Science in the Public Interest, 14*(1), 4-58. https://doi.org/10.1177/1529100612453266 — full text read [03]
- Ericsson, K. A., & Simon, H. A. (1998). How to study thinking in everyday life: Contrasting think-aloud protocols with descriptions and explanations of thinking. *Mind, Culture, and Activity, 5*(3), 178-186. https://doi.org/10.1207/s15327884mca0503_3 — full text read [01]
- European Commission. (2025). *Commission Guidelines on prohibited artificial intelligence practices established by Regulation (EU) 2024/1689 (AI Act)* (C(2025) 5052 final, 29 July 2025). — full text read; non-binding [06]
- Fox, M. C., Ericsson, K. A., & Best, R. (2011). Do procedures for verbal reporting of thinking have to be reactive? *Psychological Bulletin, 137*(2), 316-344. https://doi.org/10.1037/a0021663 — abstract/publisher page read [01]
- Harnsberger, J. D., Hollien, H., Martin, C. A., & Hollien, K. A. (2009). Stress and deception in speech: Evaluating layered voice analysis. *Journal of Forensic Sciences, 54*(3), 642-650. https://doi.org/10.1111/j.1556-4029.2009.01026.x — abstract/publisher page read [06]
- Joughin, G. (2010). *A short guide to oral assessment*. Leeds Met Press in association with University of Wollongong. https://eprints.leedsbeckett.ac.uk/id/eprint/2804 — full text read [02]
- Koenecke, A., Nam, A., Lake, E., Nudell, J., Quartey, M., Mengesha, Z., Toups, C., Rickford, J. R., Jurafsky, D., & Goel, S. (2020). Racial disparities in automated speech recognition. *PNAS, 117*(14), 7684-7689. https://doi.org/10.1073/pnas.1915768117 — abstract/publisher page read [02]
- Liang, W., Yuksekgonul, M., Mao, Y., Wu, E., & Zou, J. (2023). GPT detectors are biased against non-native English writers. *Patterns, 4*(7), 100779. https://doi.org/10.1016/j.patter.2023.100779 — abstract/publisher page read [05]
- Mujtaba, D., Mahapatra, N., Arney, M., Yaruss, J. S., Gerlach-Houck, H., Herring, C., & Bin, J. (2024). Lost in transcription: Identifying and quantifying the accuracy biases of automatic speech recognition systems against disfluent speech. *NAACL 2024* (pp. 4795-4809). https://doi.org/10.18653/v1/2024.naacl-long.269 — abstract/publisher page read [02]
- National Research Council. (2003). *The polygraph and lie detection*. The National Academies Press. https://doi.org/10.17226/10420 — full text read [06]
- Nisbett, R. E., & Wilson, T. D. (1977). Telling more than we can know: Verbal reports on mental processes. *Psychological Review, 84*(3), 231-259. https://doi.org/10.1037/0033-295X.84.3.231 — abstract/publisher page read [01]
- Pan, S. C., & Rickard, T. C. (2018). Transfer of test-enhanced learning: Meta-analytic review and synthesis. *Psychological Bulletin, 144*(7), 710-756. https://doi.org/10.1037/bul0000151 — full text read [03]
- Papoutsaki, A., Sangkloy, P., Laskey, J., Daskalova, N., Huang, J., & Hays, J. (2016). WebGazer: Scalable webcam eye tracking using user interactions. In *Proceedings of IJCAI-16* (pp. 3839-3845). AAAI Press. — full text read [06]
- Piaget, J. (1929). *The child's conception of the world* (J. Tomlinson & A. Tomlinson, Trans.). Routledge & Kegan Paul. — full text read (Introduction, sections 1-2) [01]
- Plato. (1966). *Apology* (H. N. Fowler, Trans.). In *Plato in twelve volumes* (Vol. 1). Harvard University Press. Perseus Digital Library. — full text read (21b-23a, 30e-31a) [01]
- Plato. (1967). *Meno* (W. R. M. Lamb, Trans.). In *Plato in twelve volumes* (Vol. 3). Harvard University Press. Perseus Digital Library. — full text read (82b, 82e, 84a-85d) [01]
- Rayner, K. (2009). Eye movements in reading: Models and data. *Journal of Eye Movement Research, 2*(5):2, 1-10. https://doi.org/10.16910/jemr.2.5.2 — full text read [06]
- Regulation (EU) 2024/1689 (Artificial Intelligence Act). *Official Journal of the European Union*, L, 2024/1689, 12.7.2024. http://data.europa.eu/eli/reg/2024/1689/oj — full text read [05]
- Roediger, H. L., III, & Karpicke, J. D. (2006). Test-enhanced learning: Taking memory tests improves long-term retention. *Psychological Science, 17*(3), 249-255. https://doi.org/10.1111/j.1467-9280.2006.01693.x — full text read [03]
- Rowland, C. A. (2014). The effect of testing versus restudy on retention: A meta-analytic review of the testing effect. *Psychological Bulletin, 140*(6), 1432-1463. https://doi.org/10.1037/a0037559 — full text read [03]
- Shermis, M. D. (2014). State-of-the-art automated essay scoring: Competition, results, and future directions from a United States demonstration. *Assessing Writing, 20*, 53-76. https://doi.org/10.1016/j.asw.2013.04.001 — full text read [05]
- van der Vleuten, C. P. M., Schuwirth, L. W. T., Scheele, F., Driessen, E. W., & Hodges, B. (2010). The assessment of professional competence: Building blocks for theory development. *Best Practice & Research Clinical Obstetrics and Gynaecology, 24*(6), 703-719. https://doi.org/10.1016/j.bpobgyn.2010.04.001 — full text read [02]
- Verga, P., et al. (2024). *Replacing judges with juries: Evaluating LLM generations with a panel of diverse models* (arXiv:2404.18796) [Preprint]. https://doi.org/10.48550/arXiv.2404.18796 — full text read [05]
- Vlastos, G. (1983). The Socratic elenchus. *Oxford Studies in Ancient Philosophy, 1*, 27-58. — record located only; wording quoted via Wolfsdorf (2013) and Potter (1993), not checked against the original [01]
- Wass, V., Wakeford, R., Neighbour, R., & Van der Vleuten, C. (2003). Achieving acceptable reliability in oral examinations. *Medical Education, 37*(2), 126-131. https://doi.org/10.1046/j.1365-2923.2003.01417.x — abstract/publisher page read [02]
- Weber-Wulff, D., et al. (2023). Testing of detection tools for AI-generated text. *International Journal for Educational Integrity, 19*, 26. https://doi.org/10.1007/s40979-023-00146-z — abstract/publisher page read [06]
- Wolfsdorf, D. (2013). Socratic philosophizing. In J. Bussanich & N. D. Smith (Eds.), *The Bloomsbury companion to Socrates* (pp. 34ff.). Bloomsbury. — full text read (author-hosted proof) [01]
- Zheng, L., et al. (2023). Judging LLM-as-a-judge with MT-Bench and Chatbot Arena. *NeurIPS 36, Datasets and Benchmarks Track*. https://doi.org/10.48550/arXiv.2306.05685 — full text read [05]

The Plato quotations use the Perseus translations (Fowler, Lamb); their copyright status for reuse in product
copy has not been checked (ledger, open items).
