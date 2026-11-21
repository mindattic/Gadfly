# Citation verification ledger

**Last updated:** 2026-11-21 (GAD-T254, first verification pass).

Gadfly's research notes follow one rule: no source is described as checked unless it was. Every source in `docs/research/` was looked up during this pass, on the publisher page, a DOI registry record (Crossref), an index record (PubMed, Europe PMC, ERIC, OpenAlex), a repository or author copy, or the full text. This ledger records the level at which each source was verified, so a reader can see how much weight each citation bears. The individual notes hold the full reference, the link that was opened, and what the source does and does not establish.

## Levels

- **Full text read.** The work itself (or, where noted, the relevant sections) was opened, and the cited content was checked against it.
- **Abstract/publisher page read.** The abstract, publisher landing page or index record was read; the body was not. Claims attributed to these works are confined to what that page establishes.
- **Record located only.** The bibliographic record was confirmed but the content was not read. Where a note still says something about such a work, it names the secondary source the content came from. These works are flagged for a library check before anyone quotes them externally.

**Confidence** is the note author's judgment of how far the claim Gadfly makes can rest on the source: High, Medium or Low, with the reason. A source can be read in full and still get Medium confidence for Gadfly, because its setting (chatbots, children, written essays) differs from a spoken exam for adults.

**Quotes.** A verbatim quote appears only where the exact text was seen in a page that was opened. Everything else is labelled a paraphrase in the notes.

## Summary

| Note | Entries | Full text | Abstract/publisher page | Record only |
|---|---|---|---|---|
| [01 Elenchus, clinical interview, think-aloud](../01-elenchus-clinical-interview.md) | 14 | 5 | 8 | 1 |
| [02 Oral assessment, anxiety, fairness](../02-oral-assessment-anxiety-fairness.md) | 14 | 2 | 10 | 2 |
| [03 Retrieval practice, self-explanation, transfer](../03-retrieval-self-explanation-transfer.md) | 16 | 11 | 4 | 1 |
| [04 Dynamic assessment and the ZPD](../04-dynamic-assessment.md) | 14 | 4 | 3 | 7 |
| [05 AI examiner and LLM grading](../05-ai-examiner-llm-grading.md) | 17 | 8 | 9 | 0 |
| [06 Integrity, proctoring, gaze, voice](../06-integrity-proctoring-gaze-voice.md) | 20 | 8 | 11 | 1 |
| **Total** | **95** | **38** | **45** | **12** |

The 95 entries cover 93 distinct works: Regulation (EU) 2024/1689 (the AI Act) appears in notes 05 and 06, and Sotiriadou et al. (2020) appears in notes 02 and 06. In note 04, three record-only entries (Feuerstein 1979, Lidz 1987/1991, Poehner 2008) are backed by a companion text that was read in full; the note names it. All sources were retrieved on 2026-11-21.

**Cross-note reconciliations.**

- **Sotiriadou et al. (2020)** is recorded at abstract level in note 02 and at record level in note 06. The abstract level (note 02) stands; note 06 uses the paper only as a lead.
- **AI Act Annex III, point 3** was read on EUR-Lex in note 05. Note 06 read Annex III only on an unofficial mirror, so note 05's reading closes that item for the wording of points 3(b) and 3(d). Neither note checked amendments after the 2024 text, so the application dates stay open.

## Corrections to commonly repeated claims

Verification found these claims, in popular retellings of the literature or in Gadfly's own docs, to be wrong or overstated. Each is explained in the note named in brackets.

1. **The elenchus shows inconsistency, not falsity** (01). Vlastos's "problem of the elenchus" is that refuting a claim only shows that it clashes with the student's other commitments. A "can both things be true?" probe tests whether the student's claims fit together. It cannot decide which claim is wrong; the rubric and the source material must.
2. **There is no single "Socratic method"** (01). Vlastos later conceded that his "standard elenchus" fits only part of the dialogues.
3. **The Meno slave-boy passage is not a model of non-leading questions** (01). Many of Socrates' questions at 84d-85b are leading yes/no questions. Piaget's rule against suggesting an answer is the better model for Gadfly's neutral clarifying question.
4. **"Thinking aloud changes reasoning" is overstated** (01). Across 94 studies (Fox, Ericsson & Best 2011), plain think-aloud did not change accuracy, but explaining to a listener did, and every form of verbalizing slowed people down. Gadfly's format is explaining to a listener.
5. **Nisbett & Wilson (1977) did not show verbal reports are worthless** (01). They found that reports on the causes of one's own behaviour can be accurate when the cause is salient and plausible.
6. **"Someone relaying another source's answers tends to contradict themselves"** (`docs/SOCRATIC_PLATFORM.md`) has no support in these sources (01). Treat it as a product hypothesis to test in the pilot.
7. **"Structure makes oral exams reliable" is overstated** (02). Van der Vleuten et al. put reliability down to sampling, not structuring. In Wass et al. (2003), the content sampled accounted for more score variance than the examiners did.
8. **The accent-credibility effect (Lev-Ari & Keysar 2010) is not settled** (02). Several replications failed and one succeeded.
9. **Huxham et al. (2012) does not show that anxiety has no effect on oral scores** (02). Students reported more nervousness about orals, and the sample was too small to detect small subgroup effects.
10. **"Test anxiety causes poor performance" (Hembree 1988)** is a causal reading of mostly correlational data (02). The stronger evidence is that scores rise when anxiety is treated.
11. **Roediger & Karpicke (2006) shows retention, not understanding** (03). Restudying beat testing at 5 minutes, and the final test was free recall of the same passage.
12. **The testing effect can vanish** (03). Rowland (2014) found essentially no benefit without feedback when practice-test success was 50% or less. Gadfly gives no feedback during an exam, so repeated attempts help learning only if released results give feedback for each objective.
13. **Testing benefits do not spread to untested material** (03). Pan & Rickard (2018) find transfer weakest for material that was studied but not tested.
14. **Chi et al. (1989) is correlational** (03), and answering an examiner is "explaining to another", not self-explanation in the strict sense. The self-explanation evidence supports Gadfly's why-prompts only by analogy.
15. **Bloom's taxonomy is not a strict ladder** (03), and a "what would change if" question inside the student's own scenario is extrapolation (Bloom's Comprehension), not transfer. Application needs a situation that is new to the student.
16. **The ZPD passage says "more capable peers"** (04). "More knowledgeable other" is not in it, and "scaffolding" comes from Wood, Bruner & Ross (1976), not Vygotsky. The 1978 *Mind in Society* text is an edited translation; quote it only as that edition's wording.
17. **"Dynamic assessment predicts better than static tests" is overstated** (04). Caffrey et al. (2008) found an advantage only under some conditions, and the same group later described the two as predicting similarly. DA predicted best when prompts were not contingent on the student's response, which favours a fixed prompt ladder over improvised hints.
18. **Prompt counts and gain scores are not "learning potential"** (04) when students start at different levels (De Beer 2006).
19. **Zheng et al. (2023) does not show self-enhancement bias** in LLM judges (05); the authors say they cannot determine whether it exists. Its headline agreement figure is for pairwise preferences with ties excluded, not for rubric scoring.
20. **"Machines grade essays as well as humans" (Shermis 2014)** needs its context (05): human-human agreement was only QWK 0.61-0.85, and construct validity and gaming were left open.
21. **The .70 / .10 / .15 scoring-agreement thresholds** (Williamson et al. 2012, as reported by ETS) apply to a whole set of answers, not to one answer (05).
22. **Liang et al. (2023) is about AI-text detectors, not graders** (05). Citing it as evidence that LLM graders mark down non-native speakers is a misattribution, though it is strong evidence never to use detectors.
23. **Gender Shades measured gender classification, not face detection** (06). The direct proctoring evidence is Yoder-Himes et al. (2022): one product, one university, a small darkest-skin subgroup.
24. **WebGazer's error of about 4 degrees** was measured against a Tobii tracker with four participants (06). It does not mean webcam tracking matches a commercial eye tracker, and it is far too coarse to resolve word-level reading.
25. **Jitter tends to fall, not rise, under stress** where any effect is found, and the findings are mixed (Giddens et al. 2013) (06). This bears on `strain.jitterRatio` in `docs/VOICE.md`.
26. **The NIJ voice-stress "15%"** is the average share of lies detected (LVA 21%, CVSA 8%), not overall accuracy (06).
27. **Dendir & Maxwell (2020) inferred cheating from a score drop**; they did not observe it (06).
28. **`docs/PRIVACY.md` says "Nothing infers emotion"** (06). The Commission's 2025 guidelines on the AI Act prohibitions give detecting "emotional arousal and anxiousness" during exams as an example of banned emotion recognition in education. Voice-strain metrics, especially ones that drive harder probing, are exposed to that reading. See note 06's synthesis.

**Citation details corrected.** Woldeab & Brothen (2019) is in the *International Journal of E-Learning & Distance Education*, not *Online Learning*. Bond & DePaulo (2006) is in *Personality and Social Psychology Review*, not *Psychological Bulletin*. Memon et al. is dated 2010 (online 2008). The Sotiriadou DOI resolves to "The role of authentic assessment to preserve academic integrity...", not to an "interactive oral" title. Cambridge issued Haywood & Lidz on 18 December 2006 (usually cited as 2007). The Kortemeyer preprint and journal article have different titles, and its RÂ² of 0.84 is on synthetic data.

## Open items

- **Read the record-only primary works before quoting them externally:** Vlastos (1983), Joughin (1998), Lev-Ari & Keysar (2010), Biggs & Collis (1982), Wood, Bruner & Ross (1976), Feuerstein et al. (1979), Lidz (1987, 1991), Campione & Brown (1987), Poehner (2008), Grigorenko & Sternberg (1998), Sternberg & Grigorenko (2002), and the full text of Sotiriadou et al. (2020).
- **Check figures blocked by paywalls:** the effect sizes in Adesope et al. (2017) and Caffrey et al. (2008); the nine dimensions in Barnett & Ceci (2002); the agreement statistics in Mizumoto & Eguchi (2023) and Pack et al. (2024); the marks and anxiety measures in Huxham et al. (2012).
- **Plato translations:** the Perseus translations (Fowler, Lamb) were used because Project Gutenberg was down. Their copyright status for verbatim reuse in product copy was not checked.
- **AI Act dates:** confirm the current application dates for the high-risk rules (Annex III) after any 2025-2026 amendments.
- **Gaps with no verified evidence:** neurodivergent students in oral and voice exams; direct evidence that conversational follow-up reduces cheating; any study of LLMs grading spoken Socratic answers. Gadfly's own double-marked pilot (`docs/PILOT.md`) is the only route to the last one.

## Source entries

Each entry gives the verification level, confidence and retrieval date, then the verified quote or claim exactly as recorded in the note. Full references, links and the rest of each entry are in the note.

### [Research note 01: The Socratic elenchus as examination, the clinical interview and think-aloud](../01-elenchus-clinical-interview.md)

- **PlatoApology** | Full text read | confidence High | retrieved 2026-11-21  
  - 21d: "this man thinks he knows something when he does not, whereas I, as I do not know anything, do not think I do either."
- **PlatoMeno** | Full text read | confidence High | retrieved 2026-11-21  
  - 82e: "Do you observe, Meno, that I am not teaching the boy anything, but merely asking him each time?"
- **Vlastos1983** | Record located only | confidence Medium | retrieved 2026-11-21  
  - Definition, as quoted in Wolfsdorf2013 (citing Vlastos p. 30): "a search for moral truth by adversary argument in which a thesis is debated only if asserted as the answerer's own belief and is regarded as refuted only if the negation of his thesis is deduced from his own beliefs."
- **Benson1995** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Paraphrase of the publisher abstract: the chapter starts from the common reading of the Socrates of the early dialogues as a purely destructive questioner who asks for definitions of virtues and reaches no conclusions. Paraphrase of Wolfsdorf2013's summary: after examining the elenchi in *Euthyphro*, *Charmides* and *Laches*, Benson concludes that Socrates shows only that the interlocutor's beliefs are inconsistent, so Vlastos's problem does not arise in those texts. Benson also calls it a "dogma" that the elenchus is Socrates' only method of argument (Wolfsdorf's quotation of Benson p. 109: "the elenchus is Socrates' only method of argument").
- **Benson2000** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Paraphrase of the publisher abstract: the hallmarks of the early dialogues (the question-and-answer elenchos, the interest in definition, professed ignorance, and the claim that virtue is knowledge) are epistemological. The model of knowledge behind them is said to be closer to modern scientific understanding than to ordinary knowledge. Part I contains chapters titled "The Aims of the Elenchos", "The Problem of the Elenchos" and "The Dissolution of the Problem of the Elenchos". The final chapter is "Conclusion: Meno, the Slave-Boy, and Plato".
- **Seeskin1987** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  From the publisher description: "Socrates seeks to define moral qualities-moral essences-with the goal of improving the soul of the respondent." Paraphrase of the rest of the description: ethics underlies epistemology, because discovering philosophical truth makes moral demands on the respondent. The *Apology*, *Gorgias*, *Protagoras* and *Meno* get the most emphasis.
- **Wolfsdorf2013** | Full text read | confidence High for what the chapter says (full text read); Medium as a stand-in for the primary works it summarizes | retrieved 2026-11-21  
  - "Mainstream Anglophone scholarship on Socrates' method over the past quarter century has been galvanized by Gregory Vlastos's 1983a article `The Socratic Elenchus'."
- **Piaget1929** | Full text read | confidence High | retrieved 2026-11-21  
  - p. 4: "The only way to avoid such difficulties is to vary the questions, to make counter-suggestions, in short, to give up all idea of a fixed questionnaire."
- **Ginsburg1997** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Opening line of the publisher description: "Entering the Child's Mind teaches a powerful technique for gaining insight into a child's way of thinking." Paraphrase of the rest: the book, written in the tradition of Piaget and Vygotsky, argues that standardized tests often miss complex aspects of thinking and presents the clinical interview as a flexible method for research, clinical practice and education. Chapter titles include "The Need to Move Beyond Standardized Methods" (pp. 1-29), "Not a Cookbook: Guidelines for Conducting a Clinical Interview" (pp. 115-158) and "Evaluating Clinical Interviews: How Good Are They?" (pp. 159-208).
- **EricssonSimon1980** | Abstract/publisher page read | confidence High for the abstract's claims; the detailed model was not read | retrieved 2026-11-21  
  Abstract: "Verbalizing information is shown to affect cognitive processes only if the instructions require verbalization of information that would not otherwise be attended to." Also: "The inaccurate reports found by other research are shown to result from requesting information that was never directly heeded, thus forcing subjects to infer rather than remember their mental processes."
- **EricssonSimon1993** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  From the description: "concurrent and retrospective verbal reports are now generally accepted as important sources of data", and the revised edition reviews "new evidence on how giving verbal reports affects subjects' cognitive processes" and gives "an updated version of their information-processing model, which explains verbalization and verbal reports".
- **EricssonSimon1998** | Full text read | confidence High | retrieved 2026-11-21  
  - p. 181: "we showed why verbal descriptions of one's thinking directed to another individual are likely to alter the course of thinking"
- **NisbettWilson1977** | Abstract/publisher page read | confidence High for the abstract's claims; the reviewed experiments were not read | retrieved 2026-11-21  
  Abstract: "Evidence is reviewed which suggests that there may be little or no direct introspective access to higher order cognitive processes." And: "their reports are based on a priori, implicit causal theories, or judgments about the extent to which a particular stimulus is a plausible cause of a given response." And: "Accurate reports will occur when influential stimuli are salient and are plausible causes of the responses they produce".
- **Fox2011** | Abstract/publisher page read | confidence High for the abstract's summary claims; individual task-type effects were not checked | retrieved 2026-11-21  
  Abstract: "Findings based on nearly 3,500 participants show that the 'think-aloud' effect size is indistinguishable from zero (r = -.03) and that this procedure remains nonreactive even after statistically controlling additional factors such as task type". And: "procedures that entail describing or explaining thoughts and actions are significantly reactive, leading to higher performance than silent control conditions. All verbal reporting procedures tend to increase times to complete tasks." The meta-analysis covers 94 studies. (Inner quotation marks are shown as single quotes.)

### [Research note 02: Oral assessment validity and reliability, speaking anxiety and fairness confounds](../02-oral-assessment-anxiety-fairness.md)

- **Joughin1998** | Record located only | confidence Medium | retrieved 2026-11-21  
  Paraphrase taken from Joughin2010, pp. 10-12, which presents the same framework: oral assessments vary along six dimensions. They are what is assessed, interaction, authenticity, structure, who assesses, and whether the assessment is purely oral or combined with other modes.
- **Joughin2010** | Full text read | confidence High | retrieved 2026-11-21  
  - p. 7: "anxiety that interferes with a student's performance will not give a true indication of his or her ability."
- **Memon2010** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Paraphrase of the abstract:
- **Wass2003** | Abstract/publisher page read | confidence High for the numbers as stated in the abstract | retrieved 2026-11-21  
  - "Reliability coefficients were: (A) intercase 0.65; (B) pass/fail 0.85."
- **VanderVleuten2010** | Full text read | confidence High | retrieved 2026-11-21  
  - "one measure is no measure, and single-point assessments are not to be trusted."
- **Huxham2012** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - "There was no evidence of particular groups of students being disadvantaged in the oral tests."
- **Akimov2020** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Paraphrase of the abstract: online and distance delivery bring challenges of accessibility, identity security and academic dishonesty. Online oral exams can address some of these, but validity, reliability and fairness depend on careful planning.
- **Sotiriadou2020** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  Paraphrase of the abstract:
- **Horwitz1986** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  The ERIC abstract treats "foreign language anxiety as a conceptually distinct variable in foreign language learning" and discusses "the effects of this anxiety on language learning and its pedagogical implications".
- **Hembree1988** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - "Test anxiety (TA) causes poor performance."
- **vonderEmbse2018** | Abstract/publisher page read | confidence High for the direction and the predictors as stated, from a large recent meta-analysis | retrieved 2026-11-21  
  - "test anxiety was significantly and negatively related to a wide range of educational performance outcomes"
- **LevAri2010** | Record located only | confidence Low | retrieved 2026-11-21  
  From the press release, quoting the authors: "They misattribute the difficulty of understanding the speech to the truthfulness of the statements." Paraphrase of the press release:
- **Koenecke2020** | Abstract/publisher page read | confidence High | retrieved 2026-11-21  
  - "all five ASR systems exhibited substantial racial disparities"
- **Mujtaba2024** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  "consistent and statistically significant accuracy bias across all ASRs against disfluent speech". Paraphrase: six leading ASR systems were tested on real speech from people who stutter and on synthetic stuttered LibriSpeech. Accuracy was worse on disfluent speech in word error rate, character error rate and semantic accuracy.

### [Research note 03: Retrieval practice, self-explanation, and transfer versus recall](../03-retrieval-self-explanation-transfer.md)

- **Roediger2006** | Full text read | confidence High | retrieved 2026-11-21  
  - Abstract: "Testing is a powerful means of improving learning, not just assessing it."
- **KarpickeBlunt2011** | Full text read | confidence High for what the study found; Medium for how far it generalizes (see Corrections and cautions: a published comment and reply exist) | retrieved 2026-11-21  
  - Abstract: "The advantage of retrieval practice occurred even when the criterial test involved creating concept maps."
- **Rowland2014** | Full text read | confidence High | retrieved 2026-11-21  
  - Abstract: "Engaging in a test over previously studied information can serve as a potent learning event"
- **Adesope2017** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - ERIC abstract: "Results reveal that practice tests are more beneficial for learning than restudying and all other comparison conditions."
- **Dunlosky2013** | Full text read | confidence High | retrieved 2026-11-21  
  - Abstract: "Elaborative interrogation, self-explanation, and interleaved practice received moderate utility assessments"
- **PanRickard2018** | Full text read | confidence High | retrieved 2026-11-21  
  - Abstract: "testing can yield transferrable learning as measured relative to a nontesting reexposure control condition" (d = 0.40, 95% CI [0.31, 0.50]).
- **Chi1989** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - Abstract, p. 145: "We find that "Good" students learn with understanding: They generate many explanations which refine and expand the conditions for the action parts of the example solutions, and relate these actions to principles in the text."
- **Chi1994** | Full text read | confidence Medium | retrieved 2026-11-21  
  - Abstract, p. 439: "14 eighth-grade students were merely asked to self-explain after reading each line of a passage on the human circulatory system. Ten students in the control group read the same text twice, but were not prompted to self-explain."
- **Bisra2018** | Full text read | confidence High | retrieved 2026-11-21  
  - Abstract: "Self-explanation is a process by which learners generate inferences about causal connections or conceptual relationships."
- **Bloom1956** | Full text read | confidence High | retrieved 2026-11-21  
  - Introduction: the six classes are Knowledge, Comprehension, Application, Analysis, Synthesis and Evaluation, and "the present one appears to us to represent something of the hierarchical order of the different classes of objectives."
- **Krathwohl2002** | Full text read | confidence High for the article (full text, written by a co-editor of the revision); the 2001 book itself was not read | retrieved 2026-11-21  
  - "Synthesis changed places with Evaluation and was renamed Create."
- **BiggsCollis1982** | Record located only | confidence Low for any specific wording attributed to the book (not read); High that the work exists as cited | retrieved 2026-11-21  
  Paraphrase, from the CUHK guide and the Wikipedia article (not from the book): SOLO has five levels: prestructural, unistructural, multistructural, relational and extended abstract. At the relational level the aspects are "integrated into a coherent whole" (Wikipedia). At the extended abstract level the whole is "conceptualised at a higher level of abstraction" (Wikipedia) and generalized to a new topic or area. The CUHK guide lists verbs such as explain causes, relate and apply for relational outcomes, and theorise, generalise and hypothesise for extended abstract.
- **BarnettCeci2002** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - Abstract: the debate on far transfer has "made little progress toward resolution", which the authors attribute to "a failure to specify various dimensions along which transfer can occur."
- **BransfordSchwartz1999** | Full text read | confidence High for what the chapter argues (full text of the framing sections); it is a conceptual review, not an effect-size study | retrieved 2026-11-21  
  - pp. 8-9: transfer studies use "sequestered problem solving" (SPS), in which there are "no opportunities for them to demonstrate their abilities to learn to solve new problems by seeking help from other resources such as texts or colleagues or by trying things out, receiving ffeedback [sic] and getting opportunities to revise."
- **vanGogSweller2015** | Full text read | confidence Medium | retrieved 2026-11-21  
  - Abstract: research "indicated that the testing effect decreases as the complexity of learning materials increases" and studies in the special issue "suggest that the effect may even disappear when the complexity of learning material is very high."
- **KarpickeAue2015** | Abstract/publisher page read | confidence Medium | retrieved 2026-11-21  
  - Abstract: "Van Gog and Sweller's idea of "element interactivity" is not defined in a quantitative, measurable way."

### [Research note 04: Dynamic assessment and the zone of proximal development](../04-dynamic-assessment.md)

- **Vygotsky1978** | Full text read | confidence High; exact wording and page checked in the 1978 edition text; Medium only on OCR fidelity of single characters | retrieved 2026-11-21  
  "This difference between twelve and eight, or between nine and eight, is what we call the zone of proximal development. It is the distance between the actual developmental level as determined by independent problem solving and the level of potential development as determined through problem solving under adult guidance or in collaboration with more capable peers." (p. 86). On how the help is given: "Different experimenters might employ different modes of demonstration in different cases: some might run through an entire demonstration and ask the children to repeat it, others might initiate the solution and ask the child to finish it, or offer leading questions." (p. 86). And: "The actual developmental level characterizes mental development retrospectively, while the zone of proximal development characterizes mental development prospectively." (pp. 86-87).
- **Wood1976** | Record located only | confidence Low for content (not read); High for the bibliographic record | retrieved 2026-11-21  
  None from the paper. Paraphrase of secondary descriptions seen in search results: the paper is widely credited with introducing "scaffolding" for adult help that lets a child complete a task beyond unaided reach, in a block-building study with 3- to 5-year-olds.
- **Feuerstein1979** | Record located only (primary work; a companion text was read, see note) | confidence High for what the chapter says (read); Medium for attributing those positions to the 1979 book itself (book not read) | retrieved 2026-11-21  
  From the chapter: "In 1979, the senior author, in a book entitled The Dynamic Assessment of Retarded Performers, presented the rationale for needed alternatives to conventional psychometric assessment, as well as a new approach to the assessment of learning potential, the Learning Potential Assessment Device (LPAD)." On conventional tests, instrument construction choices "all of which lead to the search for reliability, and ultimately for predictability". On graduated prompts: "Interactions that are limited in their mediational flexibility, such as graduated prompting (Bransford et al., 1987; Campione & Brown, 1987) or the testing the limits approach of Carlson and Wiedl (1978, 1979) will not make possible the detection of the microchanges which can be produced in the individual on a variety of levels." The chapter also proposes renaming the device the Learning Propensity Assessment Device.
- **Lidz1987** | Record located only (primary work; a companion text was read, see note) | confidence Medium; review read in full, but it is a short secondary account; books not read | retrieved 2026-11-21  
  Review (OCR errors normalised in this quote: "bas" read as "has", "MOst" as "most", "ttaditional" as "traditional"): "The issue that has received the most criticism from traditional assessment devotees is the problem of transfer or generalizability". Paraphrase of the review: the volume collects chapters by the main DA authors (Feuerstein's LPAD, Budoff's test-train-test work, Campione and Brown linking DA with school achievement, Embretson); the Campione and Brown and Embretson/Budoff chapters conclude that how much transfer occurs is partly determined by intellectual factors rather than by the quality of mediation; the reviewer says the book gives no definitive answer on this.
- **HaywoodLidz2007** | Abstract/publisher page read | confidence Medium; claims limited to the publisher pages | retrieved 2026-11-21  
  The book description says DA "embeds interaction within the framework of a test-intervene-retest approach". Paraphrase of the chapter 1 preview: the common element across DA definitions is active intervention by the examiner and assessment of the examinee's response to it; DA had been in use for more than 50 years but was still not widely practised.
- **CampioneBrown1987** | Record located only | confidence Low for content; High that the chapter exists as cited | retrieved 2026-11-21  
  No quote. Paraphrase of how the chapter is used in sources that were read: Stad et al. (2019) and Feuerstein, Falik and Feuerstein (chapter above) both cite Campione and Brown (1987) as the origin of graduated prompting; Sarabi Asl et al. (2024) describe the Campione/Brown approach as giving pre-planned prompts and then testing near, far and very far transfer.
- **LantolfPoehner2004** | Abstract/publisher page read | confidence Medium; abstract read; the split itself is attributed to this paper only through secondary citations | retrieved 2026-11-21  
  Paraphrase of the abstract: DA is grounded in the ZPD; its defining feature is that the examiner mediates performance through prompts, hints and leading questions, both to reveal abilities and to promote development during the assessment; the paper reviews DA approaches, notes how few studies addressed language learning, and discusses criticisms. The abstract page does not itself use the words "interventionist" or "interactionist".
- **Poehner2008** | Record located only (primary work; a companion text was read, see note) | confidence Medium; the distinction is read in a 2024 peer-reviewed article; the Poehner quote is secondary | retrieved 2026-11-21  
  Sarabi Asl et al. quote Poehner (2008, pp. 44-45) as saying that in the interventionist model "mediators are not free to respond to learners' needs as these become apparent during the procedure but must instead follow a highly scripted approach to mediation in which all prompts, hints, and leading questions have been arranged hierarchically" - seen in Sarabi Asl et al., not checked in Poehner. Sarabi Asl et al. themselves write that the pre-planned interpretation, "while bringing about the comfort of better generalizability and large-scale administration, deprives learners and instructors of co-creating a ZPD" (p. 5). Paraphrase of Sarabi Asl et al.: interventionist DA uses pre-planned mediation, implicit-to-explicit, to produce quantifiable results for comparison; interactionist DA lets mediation emerge from dialogue and does not define a fixed endpoint.
- **Stad2019** | Full text read | confidence High for what the protocol was; Low for transfer to Gadfly's population and task type | retrieved 2026-11-21  
  If a child could not solve an item after a general instruction, "they received help according to a standardised graduated prompting protocol that consisted of four prompts"; prompts went from metacognitive to step-by-step and "were only provided after an incorrect answer". Scoring used pre- and post-test accuracy "In addition, the number of hints children were provided with, and the type of hints they utilised". The introduction lists measures of potential for learning as "the amount and the type of feedback received during training, performance after training, the change in performance from the pre-training to post-training stage, and the transfer of learned skills".
- **GrigorenkoSternberg1998** | Record located only | confidence Medium; consistent across two secondary sources, but the review itself was not read | retrieved 2026-11-21  
  No quote from the article. De Beer (2006) reports that, "according to Grigorenko and Sternberg (1998), multiple attempts to quantify learning potential and transform such testing into robust psychological diagnostic tools have not produced consistent results." Fuchs et al. (2011) cite it for the point that DA differs from traditional testing in the examiner-student relationship, the content of feedback, and an emphasis on process over product.
- **SternbergGrigorenko2002** | Record located only | confidence Low for content; High for the record | retrieved 2026-11-21  
  No quote. Stad et al. (2019, read) cite the book for dynamic tests "aimed at both measuring children's progression in task solving" and for the list of potential-for-learning measures quoted under Stad2019. The often repeated "sandwich" (pretest-train-posttest) versus "cake" (item-by-item prompting) distinction attributed to this book was searched for and not found in any page opened; it is not relied on here.
- **Caffrey2008** | Abstract/publisher page read | confidence Medium; abstract only; effect sizes and study list not checked | retrieved 2026-11-21  
  Mixed-methods review of 24 studies; 15 analysed quantitatively with Pearson correlations. Analyses compared traditional tests and DA, compared two forms of DA, examined student populations and outcome types, and assessed DA's value added over traditional testing. "Results indicated superior predictive validity for DA when feedback is not contingent on student response, when applied to students with disabilities rather than at-risk or typically achieving students, and when independent DA and criterion-referenced tests were used as outcomes instead of norm-referenced tests and teacher judgment."
- **Fuchs2011** | Full text read | confidence High for what the paper says; Low for generalisation from first-grade decoding to adult conceptual understanding | retrieved 2026-11-21  
  On Caffrey et al.: they "found that traditional tests and DA similarly predicted future academic performance, irrespective of whether the students were normally achieving or at risk." On their own measure: "The tester uses a scripted standardized administration" with "five levels of more and more explicit scaffolded instruction". Abstract: DA "was a significant predictor of responsiveness to instruction, contributing unique variance to end-of-first-grade word identification and reading comprehension beyond that explained by other well-established predictors". On cost: making prompts more helpful earlier would mean "increasing its length and administration time - already at 20 to 30 min" (dash normalised).
- **DeBeer2006** | Full text read | confidence High for what the paper says; Medium as a field-wide verdict (single author, one instrument) | retrieved 2026-11-21  
  "There is a paucity of published empirical data on the reliability and validity of dynamic testing." On gain scores: "the difference score or ZPD has often (incorrectly) been referred to as that which indicates 'potential'"; the author argues the improvement score "should not be used alone, but that present level of performance as well as the projected future level of performance and the resulting difference score should also be taken into account." The abstract lists the remedies: standardised procedures, Item Response Theory for measurement accuracy, and computerised adaptive testing for time efficiency.

### [Research note 05: AI as examiner and LLM grading reliability](../05-ai-examiner-llm-grading.md)

- **Zheng2023** | Full text read | confidence High; full text read; but these are chat-assistant answers judged on general helpfulness, not student answers judged against rubrics | retrieved 2026-11-21  
  "Only GPT-4 outputs consistent results in more than 60% of cases." (p. 5, on position bias when the two answers are swapped). "Due to limited data and small differences, our study cannot determine whether the models exhibit a self-enhancement bias." (p. 5). GPT-4 agreement with humans "reaches 85%, which is even higher than the agreement among humans (81%)" (p. 7, setup S2, which excludes ties). Humans "deemed GPT-4's judgments reasonable in 75% of cases and are even willing to change their choices in 34% of cases" (p. 7).
- **Wang2024** | Full text read | confidence High; full text read; pairwise chatbot setting, not education | retrieved 2026-11-21  
  "Vicuna-13B could beat ChatGPT on 66 over 80 tested queries with ChatGPT as an evaluator." (abstract, ACL Anthology page). The paper reports GPT-4 conflict rates (verdict changes when order is swapped) of 46.3% and 5.0% on two comparisons, and ChatGPT 82.5% and 52.5% (Section 2). It also reports that the conflict rate rises as the quality gap between the two responses shrinks (Figure 2 caption).
- **Verga2024** | Full text read | confidence Medium; full text read, but a preprint, three-model panel, non-education tasks | retrieved 2026-11-21  
  The panel (PoLL) is built from "three models being drawn from three disparate model families" (Section 3.1): Command R, Claude 3 Haiku and GPT-3.5, combined by max voting for binary QA judgements and average pooling for 1-5 scores. Cohen's kappa against human judgements on KILT QA sets: PoLL 0.763 / 0.906 / 0.867 versus GPT-4 0.627 / 0.841 / 0.830 (Table 1). On Chatbot Arena Hard, the GPT-4 judge ranked another GPT-4 variant higher than its actual position (Section 4.4, "intra-model bias").
- **ChiangLee2023** | Abstract/publisher page read | confidence Medium; abstract only; story-quality ratings rather than student assessment | retrieved 2026-11-21  
  "Human evaluation is indispensable and inevitable for assessing the quality of texts." (abstract, opening). Paraphrase: giving an LLM the same instructions, samples and questions used in human evaluation produced ratings consistent with expert human ratings on story generation and adversarial-attack tasks, and stable across instruction formatting and sampling; the paper discusses limitations and ethical considerations.
- **Panickssery2024** | Abstract/publisher page read | confidence Medium; abstract and figure read; preprint; summarisation domain | retrieved 2026-11-21  
  Self-preference is defined as the case "where an LLM evaluator scores its own outputs higher than others' while human annotators consider them of equal quality" (abstract). Paraphrase: GPT-4 and Llama 2 distinguish their own outputs from other LLMs' and humans' above chance without fine-tuning, and after fine-tuning the strength of self-preference is linearly correlated with self-recognition ability (Figure 1, summarisation datasets).
- **HenkelBoxer2024 (K-12 short answers)** | Abstract/publisher page read | confidence Medium; abstract only; one dataset; formative context | retrieved 2026-11-21  
  "GPT-4, with basic few-shot prompting performed well (Kappa, 0.70)" and was "very close to human-level performance (0.75)" (abstract).
- **HenkelHills2023 (reading comprehension, Ghana)** | Abstract/publisher page read | confidence Medium; abstract only; preprint; small sample | retrieved 2026-11-21  
  GPT-4 "performed extremely well on grading the novel dataset" with "(QWK 0.92, F1 0.89)" against expert human raters (abstract).
- **Mizumoto2023** | Abstract/publisher page read | confidence Low-Medium; abstract only; older model; statistics not checked | retrieved 2026-11-21  
  "AES using GPT has a certain level of accuracy and reliability and could provide valuable support for human evaluations" (abstract). Paraphrase: GPT-3 text-davinci-003 scored all 12,100 TOEFL11 essays, scores were compared to benchmark proficiency levels, and adding linguistic features improved accuracy.
- **Kortemeyer2023** | Abstract/publisher page read | confidence Medium; abstract only; synthetic data for the headline statistic | retrieved 2026-11-21  
  For summative, high-stakes use the tool "should only be used for an initial round of grading that sorts and flags solution approaches" (arXiv abstract). Paraphrase of the APS abstract: a MathPix + GPT-4 workflow graded handwritten physics derivations; agreement with human graders was reported as R-squared 0.84 on a synthetic dataset; promising for formative feedback, best used to assist human graders for final evaluations.
- **Pack2024** | Abstract/publisher page read | confidence Medium; abstract only; small sample; coefficients not checked | retrieved 2026-11-21  
  "GPT-4 performed the best, demonstrating excellent intrarater reliability and good validity." "The interrater reliability of GPT-3.5 and GPT-4, however, decreased slightly over time." The authors conclude "all models are subject to fluctuations in their performance" (abstract).
- **Shermis2014** | Full text read | confidence High; full text read; but these are trained, prompt-specific engines on essays, not zero-shot LLMs on spoken answers | retrieved 2026-11-21  
  "Quadratic-weighted kappas ranged from 0.62 to 0.85, a typical range for human rater performance in statewide high-stakes testing programs." "Human quadratic-weighted kappas ranged from 0.61 to 0.85 and were closely followed by the machine ranges which went from 0.60 to 0.84." Limitation: "A predictive model may do a good job of matching human scoring behavior, but for reasons unrelated (or unsatisfactorily related) to the construct of interest." It also flags that scoring measures may be "subject to manipulation by test-takers and coaches".
- **Casabianca2023 (reporting Williamson, Xi & Breyer 2012)** | Full text read | confidence Medium; the memo is read in full; the original Williamson paper was not | retrieved 2026-11-21  
  "based on Williamson et al. (2012), a QWK or correlation of .70 or higher is commonly used as an acceptable level of concordance between the human and automated scores." "Williamson et al. used degradation less than .10 and SMD less than .15, and these criteria also have had wide use in practice." (Introduction). The memo also notes that applying the same .70 threshold to other metrics is "unclear" (Discussion).
- **MeadowsBillington2005** | Full text read | confidence High; full text read; but the evidence base is UK and older (pre-2005) | retrieved 2026-11-21  
  "Irrespective of the subject area, however, essay questions presented the greatest reliability problem." (p. 18). Hartog and Rhodes (1936) found agreement between pairs of markers of history honours scripts "ranged from -0.41 to 0.85 with an average of just 0.44" (p. 18). In an NUS (1969) study with five markers, "The average difference in marks for individual scripts was 19" (out of 100; p. 18). Practices recommended for essay reliability include "removing the names from the essays", training markers and "the double marking of essays" (p. 41, citing Diederich via Meckel 1963).
- **Liang2023** | Abstract/publisher page read | confidence High for the detector finding; Low for any extension to graders (not tested in this paper) | retrieved 2026-11-21  
  The authors "evaluated the performance of seven widely used GPT detectors" and "they incorrectly labeled more than half of the TOEFL essays as "AI-generated" (average false-positive rate: 61.3%)" (PMC). arXiv abstract: "Our findings reveal that these detectors consistently misclassify non-native English writing samples as AI-generated".
- **Milani2025** | Full text read | confidence Medium; full text read; small study, consumer chat interfaces rather than API graders | retrieved 2026-11-21  
  "all the experiments show that the considered LLMs are vulnerable to every injection strategy that could be tested." (Section 4.1, p. 11). Paraphrase: when assignments were graded as a batch rather than singly, all models tended to grade more leniently, regardless of injection.
- **ChiangEtAl2024 (classroom deployment)** | Abstract/publisher page read | confidence Medium; abstract only, but peer-reviewed and a large real deployment | retrieved 2026-11-21  
  "students can easily manipulate the LLM-based evaluator to output specific strings" (abstract). Paraphrase: GPT-4 graded assignments for 1,028 students; students generally accepted it but reported it sometimes ignored grading instructions; students could obtain high scores without meeting the rubric.
- **EUAIAct2024** | Full text read | confidence High for the text as published; Medium for the current application dates (see Corrections) | retrieved 2026-11-21  
  Annex III point 3(b): "AI systems intended to be used to evaluate learning outcomes, including when those outcomes are used to steer the learning process of natural persons in educational and vocational training institutions at all levels". Point 3(d): "AI systems intended to be used for monitoring and detecting prohibited behaviour of students during tests in the context of or within educational and vocational training institutions at all levels". Article 14(1): high-risk systems "shall be designed and developed in such a way, including with appropriate human-machine interface tools, that they can be effectively overseen by natural persons". Article 14(4)(b) requires overseers to remain aware of "the possible tendency of automatically relying or over-relying on the output produced by a high-risk AI system (automation bias)". Article 113: the Regulation "shall apply from 2 August 2026", with Article 6(1) obligations from 2 August 2027.

### [Research note 06: Academic integrity, proctoring evidence, and the limits of gaze and voice-stress signals](../06-integrity-proctoring-gaze-voice.md)

- **Alessio2017** | Abstract/publisher page read | confidence Medium; clear effect, but single-course observational design and only the abstract was read | retrieved 2026-11-21  
  "Students scored, on average, 17 points lower [95% CI: 14, 20]" and "used significantly
- **DendirMaxwell2020** | Abstract/publisher page read | confidence Medium; consistent with Alessio2017; quasi-experimental and abstract-only | retrieved 2026-11-21  
  "Analysis of exam scores shows that online proctoring was associated with a decrease in
- **Coghlan2021** | Full text read | confidence High; full text read; the claims are normative and the article is well cited | retrieved 2026-11-21  
  "facial recognition functions in OP systems sometimes have more difficulty recognizing
- **WoldeabBrothen2019** | Abstract/publisher page read | confidence Medium; abstract only, correlational | retrieved 2026-11-21  
  Paraphrase of abstract: in 631 students, higher trait test anxiety was associated with
- **BuolamwiniGebru2018** | Abstract/publisher page read | confidence High for what it measured; Low as direct evidence about proctoring | retrieved 2026-11-21  
  "darker-skinned females are the most misclassified group (with error rates of up to
- **YoderHimes2022** | Full text read | confidence Medium; full text read, but single product and modest subgroup size | retrieved 2026-11-21  
  Paraphrase: in ~357 students across four STEM courses (Fall 2020) using Respondus
- **WeberWulff2023** | Abstract/publisher page read | confidence High; clear published conclusion; detectors change, but no later evidence here overturns it | retrieved 2026-11-21  
  "The researchers conclude that the available detection tools are neither accurate nor
- **Papoutsaki2016** | Full text read | confidence High; figures read directly in the paper | retrieved 2026-11-21  
  Online study (76 participants analysed): "Sampling cursor activity (RR+C) has the
- **SemmelmannWeigelt2018** | Abstract/publisher page read | confidence High; specific numbers in the abstract, consistent with Papoutsaki2016 | retrieved 2026-11-21  
  "in-lab data exhibit an offset of about 172 px (15% of screen size, 3.94° visual angle)
- **Rayner1998** | Abstract/publisher page read | confidence Medium; abstract only; numbers taken from the later open paper by the same author | retrieved 2026-11-21  
  "The basic theme of the review is that eye movement data reflect moment-to-moment
- **Rayner2009** | Full text read | confidence High; numbers read directly | retrieved 2026-11-21  
  "the average fixation duration in reading is about 200-250 ms" and "the average saccade
- **NRC2003** | Full text read | confidence High; authoritative review, passages read | retrieved 2026-11-21  
  "Although proponents of voice stress analysis claim high levels of accuracy, empirical
- **Harnsberger2009** | Abstract/publisher page read | confidence High; clear numeric abstract in a peer-reviewed forensic journal | retrieved 2026-11-21  
  "the 'true positive' (or hit) rates for all examiners averaged near chance (42-56%)";
- **Damphousse2007** | Full text read | confidence High; primary report read | retrieved 2026-11-21  
  "The programs were not able to detect deception at a rate any better than chance."
- **Giddens2013** | Abstract/publisher page read | confidence Medium; abstract only, but a review whose conclusion is explicitly cautious | retrieved 2026-11-21  
  "Increase in fundamental frequency is the most commonly reported effect of stress in
- **DePaulo2003** | Abstract/publisher page read | confidence High for the abstract-level conclusion | retrieved 2026-11-21  
  1,338 estimates of 158 cues; liars "are more tense" and "tell less compelling tales";
- **BondDePaulo2006** | Abstract/publisher page read | confidence High; large meta-analysis, abstract figures clear | retrieved 2026-11-21  
  "people achieve an average of 54% correct lie-truth judgments, correctly classifying 47%
- **EUAIAct2024** | Full text read | confidence High for the quoted articles and recitals; Medium for Annex III wording (mirror only) | retrieved 2026-11-21  
  Art. 5(1)(f) prohibits "the placing on the market, the putting into service for this
- **EC_Guidelines2025** | Full text read | confidence High for what the guidelines say; they are non-binding and only the CJEU interprets the Act | retrieved 2026-11-21  
  Para 247: emotions "should be understood in a wide sense and not interpreted
- **Sotiriadou2020** | Record located only | confidence Low; content not read | retrieved 2026-11-21  
  None from the work. The title states the claim under study (authentic assessment,

