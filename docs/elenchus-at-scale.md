# Elenchus at Scale
### Socratic Oral Assessment by Synthetic Examiners

_by MindAttic_

## Abstract

*Draft, 21 November 2026. To be revised when Chapters 3 to 8 are drafted.*

Capable language models have broken the assumption on which the written examination rested: that the words submitted were produced by the person whose name is on them. Watching students more closely is a weak remedy, because proctoring, gaze tracking, voice analysis and detectors of machine-written text either cannot identify an individual who cheated or misfire on identifiable groups of students. The older remedy is to change the assessment: to question the student aloud and follow up on what they say. Oral examination resists outsourced answers but does not scale. This dissertation asks whether a synthetic examiner, a system of language models that questions a student aloud in the Socratic manner, writes each follow-up from the student's own previous answer and judges the answers against the instructor's material, can conduct such examinations at scale, and under what conditions the evidence it gathers is fit to inform a grade. Using Gadfly, a working system, as its case, it argues that a Socratic examination is a procedure for gathering evidence about understanding, and that the soundness of that evidence depends less on the capability of the examiner's models than on conditions that the literature makes explicit: wide sampling of objectives; scoring anchored to fixed rubrics rather than to the path the conversation took; controls for anxiety, accent, disfluency and transcription error; a panel of judges from different model families combined with blind human grading and reconciliation; and integrity signals kept as context, never as verdicts. The dissertation reviews six literatures against a recorded verification ledger, states a formal model of examination as sequential evidence-gathering by questioning, states the method as requirements an implementation can be checked against, and designs the empirical test that would show whether a system meets them. It reports no empirical evaluation, and it records that no study yet tests language models grading spoken Socratic answers.

**Keywords:** Socratic method; elenchus; oral assessment; viva; large language models; LLM-as-a-judge; automated scoring; dynamic assessment; retrieval practice; academic integrity; remote proctoring; speech recognition fairness; AI Act

## Statement on Authorship and Method

This dissertation is being written in collaboration between its author and a large language model, and its research notes were prepared by language-model agents working under the project's verification rule. The thesis, the decision to pursue it, and the final judgment on every claim belong to the author. Section 1.7 describes the method in detail.

The verification rule is that no source is cited as checked unless it was. Every work in the reference list was located in a research pass on 21 November 2026 and is recorded in Appendix A at the level at which it was read: full text read, abstract or publisher page read, or record located. Claims attributed to works read only at the abstract level are confined to what the abstract establishes. Primary works that were located but not opened are cited only through a secondary source that was read, in the form "as cited in", and Appendix A lists them. Quotations appear only where the exact text was seen. The verification pass also found 28 claims, in popular retellings of the literature or in the project's own documentation, that were wrong or overstated; Appendix A, Section A.4, lists them.

## Abbreviations

| Abbreviation | Meaning |
|---|---|
| AES | Automated essay scoring |
| AI | Artificial intelligence |
| ASR | Automatic speech recognition |
| DA | Dynamic assessment |
| LLM | Large language model |
| LPAD | Learning Potential Assessment Device |
| QWK | Quadratic-weighted kappa |
| SMD | Standardized mean difference |
| SOLO | Structure of the Observed Learning Outcome |
| ZPD | Zone of proximal development |

## Chapter 1 — Introduction

### 1.1 The problem

For most of its history, the written examination rested on an assumption so basic that it was rarely stated: the words on the page were produced by the person whose name was at the top. Remote examination weakened that assumption, and capable language models have broken it. A student who can type a question into a chat window can now return, within seconds, a fluent and usually correct answer to almost any question a course examination is likely to ask. The difficulty is not only that such answers can be copied. It is that a finished answer, however good, no longer carries much information about the understanding of the person who submitted it.

The responses on offer fall into two families. The first tries to restore the old assumption by watching the student: remote proctoring, webcam and screen monitoring, and software that claims to detect machine-written text. The literature reviewed in Chapter 2 gives reasons to doubt each of these. Proctored online examinations do produce lower scores than unproctored ones (Alessio et al., 2017; Dendir & Maxwell, 2020), which suggests that unproctored scores are inflated, but no surveillance signal has been shown to identify an individual who cheated. Detectors of machine-written text are neither accurate nor reliable (Weber-Wulff et al., 2023), and seven widely used detectors labelled more than half of a set of essays by non-native English writers as machine-generated (Liang et al., 2023). Face detection in one widely used proctoring product flagged students with darker skin more often (Yoder-Himes et al., 2022). The second family changes the assessment instead of the surveillance: it asks for work whose production is hard to delegate. The oldest such form is the oral examination, in which the examiner hears the answer being made and can ask about it.

Oral examination has a long record in doctoral defences, professional certification and medical licensing, and Chapter 2 shows that it can be made reliable (Wass et al., 2003; van der Vleuten et al., 2010). It has one decisive weakness: it does not scale. Each examination costs the full attention of at least one qualified examiner for its whole length, and reliable scores in assessments of competence require wide sampling across content and examiners, with estimates of at least three to four hours of testing time for minimally reliable scores (van der Vleuten et al., 2010). In postgraduate medical certification, some countries have moved away from oral examinations altogether because of continuing concerns about whether they support certification decisions reliably and fairly (Memon et al., 2010).

This dissertation asks whether that weakness can now be removed without giving up what makes oral examination worth having. Language models can do more than write answers. They can ask questions, listen to answers transcribed from speech, follow up on what was said, and judge answers against a rubric. A synthetic examiner could, in principle, conduct a Socratic oral examination for every student in a course, at any hour, in parallel. Whether it should, and under what conditions the evidence it gathers can bear the weight of a grade, are the questions this work sets out to answer.

### 1.2 The case: Gadfly

The dissertation is built around a working system. Gadfly is a platform, developed by MindAttic, in which a student demonstrates understanding of a subject by being questioned out loud, in the manner of Socrates, by a synthetic examiner that follows up on what the student has just said. Its design, as recorded in the project's own plan, rests on five commitments.

1. **Grounding in the instructor's material.** The instructor supplies the source material. It is parsed and divided into passages, a set of language models extracts a concept map from it, and for each concept a rubric is built listing the points that a real understanding covers, the acceptable variants and the common misconceptions, each point tied to the passages it came from. The instructor reviews and edits both.
2. **Questions written from the student's own words.** The examiner chooses, for each concept, one of a small set of moves: Elicit ("what is it, in your own words?"), Clarify (pin down a vague term), Probe ("why does that follow?"), Counterexample (offer a case that breaks the student's definition), Contradiction (confront the student with two of their own claims that conflict), Apply or Transfer (a new situation that needs the concept rather than its definition), Synthesize (connect two concepts) and Close (end the concept because understanding has been shown, the student has reached an acknowledged impasse, or time has run out). Each follow-up is generated from the student's previous answer, so it cannot be prepared in advance.
3. **A ledger of claims.** The examiner extracts the claims the student makes and keeps them, so that later claims can be checked against earlier ones. This is the device that gives the elenchus, the Socratic examination of a person's beliefs, its name in the system.
4. **Judgment by a panel, with a human in charge.** Each answer is judged against the source material by a panel of language models from different families, each citing the passages it relied on and free to dissent. A human examiner grades blind, without seeing the panel's scores until their own are submitted. Combined scores are computed under a policy fixed before grading begins, large disagreements block finalization until a second reviewer reconciles them, and only a human can approve and publish a grade.
5. **Integrity signals as evidence, never as verdicts.** Webcam and audio signals (another face, a second voice, sustained gaze at a region off the screen) are recorded for a human reviewer. They never fail a student automatically and never enter the grade.

Gadfly is used here as a case in the methodological sense: a concrete, inspectable instance of the general design the dissertation examines. The argument does not depend on Gadfly's particular code, and Chapter 4 states the method in terms that any implementation could meet or fail. But a working system disciplines theory. Every claim made about synthetic Socratic examination must be one that a system like Gadfly could be built to satisfy, and every requirement the literature imposes must be one that can be checked against a specification.

### 1.3 The thesis

The thesis of this dissertation is that **a Socratic oral examination is a procedure for gathering evidence about understanding, and that a synthetic examiner can conduct one at scale whose evidence is fit to inform a grade, but only under conditions that the literature makes explicit and that are largely independent of how capable the examiner's language models are.**

The thesis has three parts.

The first is a reframing. An examination is not a container of questions but a sequence of observations, each chosen in the light of the ones before, from which an inference about a student's understanding is drawn. Socratic questioning is distinctive because each question is chosen to be maximally informative given what the student has just said, and because the answers it elicits are hard to produce without the understanding they are meant to reveal. Chapter 3 states this as a formal model of evidence-gathering by questioning.

The second is a claim of possibility. The elements of a Socratic examination that once required a skilled human examiner (following an answer, choosing a probing next question, noticing that two claims conflict, judging an explanation against a body of material) are elements that current language models can perform, imperfectly but measurably. Scale removes the main practical objection to oral examination.

The third is a claim about conditions, and it carries most of the weight. The literature reviewed in Chapter 2 shows that the validity of an oral examination depends on things that have little to do with the examiner's intelligence: how widely it samples the content, how far the scoring is anchored to fixed criteria rather than to the path the conversation took, whether anxiety, accent and disfluency are kept from contaminating the score, how human judgment is combined with machine judgment, and whether integrity signals are kept in their place. A more capable examiner makes the conversation better. It does not, by itself, make the evidence sound. The conditions under which it is sound are institutional and procedural, and the dissertation's main contribution is to state them, ground each in evidence, and design the test that would show whether a system meets them.

### 1.4 Research questions

The dissertation addresses five questions.

- **RQ1. What does a Socratic examination measure, and what can it not measure?** In particular, what does a contradiction between two of a student's claims show, and what does a correct answer to a probing follow-up show? (Chapters 2 and 3.)
- **RQ2. Under what conditions can adaptive questioning by a synthetic examiner produce evidence that is comparable across students?** Adaptive follow-up is the method's strength and the main threat to its reliability, since no two students receive the same questions. (Chapters 3, 4 and 5.)
- **RQ3. How should the judgments of a panel of language models and of a human examiner be combined into a grade?** What does the evidence on machine judgment, human marking reliability and automation bias require of that combination? (Chapters 2, 4 and 5.)
- **RQ4. What is the proper role of integrity signals in a spoken examination?** What do webcam gaze and acoustic features of speech actually measure, and what does the law of the jurisdictions in which such a system would operate permit? (Chapters 2 and 5.)
- **RQ5. What empirical study would test whether a synthetic Socratic examination meets these conditions?** (Chapter 7.)

### 1.5 Contributions

The dissertation makes four contributions.

1. **A verified synthesis** of six literatures that bear on synthetic oral examination and have not previously been brought together: the philosophical literature on the Socratic elenchus, the clinical interview and the psychology of verbal reports; the validity, reliability and fairness of oral assessment; retrieval practice, self-explanation and transfer; dynamic assessment; the reliability and biases of language models as judges; and the evidence on proctoring, gaze tracking and voice-stress analysis. Every source was checked at a recorded level, and Appendix A records that level for each.
2. **A formal model** of the examination as sequential evidence-gathering, which separates what a question is for (reducing uncertainty about a student's understanding of an objective) from what the score is (an inference about that understanding), and which makes precise why adaptive questioning and comparable scoring are compatible only if the score is anchored to the objective and not to the conversational path.
3. **A statement of method,** the Gadfly method, as a set of requirements an implementation can be checked against, each traced to the evidence that motivates it.
4. **A design for an empirical test,** stating the measures, comparisons and thresholds that would show whether a system that implements the method produces evidence fit to inform a grade, and the results that would show it does not.

### 1.6 Scope and limits

The work is a theoretical and design dissertation. It does not report an empirical evaluation of Gadfly, because none has yet been run, and it does not claim that synthetic Socratic examination works. It states the conditions under which it would, and the study that would find out. Several limits follow and are stated here so that no later chapter appears to claim more.

- **No source tests the central practice.** The verification pass behind Chapter 2 found no study of language models grading spoken, transcribed Socratic answers, multi-turn follow-ups or per-objective scoring in an oral examination. Agreement figures from essays and short written answers are evidence about neighbouring tasks, not about this one, and the dissertation never quotes them as Gadfly's expected accuracy.
- **The integrity claim is a hypothesis.** It is often said that a person relaying another source's answers will contradict themselves under questioning. No verified source supports this, and Chapter 2 records it as a hypothesis for the empirical test.
- **Some populations are not covered by the evidence.** No source on neurodivergent students in oral or voice examinations was verified, and the dissertation says nothing about them beyond the need to find out.
- **The method serves examination, not tutoring.** A Socratic tutor that hints is a different instrument with a different purpose. Chapter 2 reviews dynamic assessment, in which help given during the test is part of the measurement, and Chapter 4 argues that any such mode must be kept apart from the scored examination.
- **The legal analysis is descriptive.** The dissertation reports what the European Union's Artificial Intelligence Act and the European Commission's guidelines on it say about systems that evaluate learning outcomes or infer emotions in education. It is not legal advice, and the application dates of the high-risk rules are recorded in Appendix A as an item to confirm.

### 1.7 Method, authorship and verification

This dissertation was written in collaboration between its author and a large language model, and its research notes were prepared by language-model agents working under the project's verification rule. That fact bears on the subject. A dissertation that argues synthetic systems can examine students, but only under human oversight and with every claim checked, should be held to the same standard, and the method of its own production should be open to inspection.

Three rules governed the work.

1. **No source is described as checked unless it was.** Every work cited was located during a research pass on 21 November 2026, through its publisher page, a registry or index record, a repository or author copy, or its full text. Each is recorded at one of three levels: full text read (in whole or in the relevant parts, stated), abstract or publisher page read, or record located only. A claim attributed to a work read only at the abstract level is confined to what the abstract establishes.
2. **Record-only works are not cited for their content.** Where a primary work could not be opened but its content is known from a secondary source that was read, the dissertation cites it in the secondary form ("as cited in"), and the reference list names only the source that was read. Appendix A lists the primary works that appear in the text in this way, as a list for a library.
3. **Corrections are kept, not hidden.** The verification pass found 28 claims, in popular retellings of the literature or in the project's own documentation, that were wrong or overstated. They are listed in Appendix A, Section A.4, and several of them shape the argument of Chapter 2.

The research notes on which Chapter 2 draws, with the full reference, the page opened, the verified quotation or paraphrase, and a confidence rating for each source, are kept with the project (Gadfly repository, `docs/research/`). Quotations from them appear in this dissertation only where the exact text was seen.

### 1.8 Structure of the dissertation

Chapter 2 reviews the six literatures and closes with the gaps that the rest of the dissertation addresses. Chapter 3 states the formal model of an examination as sequential evidence-gathering by questioning. Chapter 4 states the Gadfly method as a set of requirements, each traced to its evidence. Chapter 5 examines validity, fairness and integrity: what the scores mean, for whom they might mean something different, and what integrity signals can and cannot do. Chapter 6 sets out the limitations of the argument. Chapter 7 designs the empirical test. Chapter 8 concludes. Appendix A is the source verification ledger.

## Chapter 2 — Literature Review

### 2.1 Scope and method of the review

A synthetic Socratic examination draws on several literatures that rarely cite one another. Philosophers have argued for half a century about what the Socratic elenchus can establish. Developmental psychologists built the clinical interview to follow a child's reasoning without leading it. Researchers in medical education measured the reliability of oral examinations. Cognitive psychologists showed that being tested changes what is learned. The dynamic assessment tradition made help given during a test part of the measurement. Computer scientists have begun to measure how language models behave as judges, and a separate literature has measured what remote proctoring, gaze tracking and voice analysis can detect. This chapter brings these literatures together around the questions set out in Chapter 1.

The review rests on a single research pass carried out on 21 November 2026, which located 93 distinct works and recorded each at one of three levels: full text read (in whole or in the relevant parts), abstract or publisher page read, or record located only (Section 1.7). The chapter cites a subset of them. Where a work is cited, the claim attributed to it is confined to what was read at its recorded level: a work read only at the abstract level is cited only for what its abstract states, and a primary work that was located but not opened is cited only in the form "as cited in" a secondary source that was read. Appendix A records the level for every work in the reference list.

The chapter proceeds from the oldest literature to the newest. Section 2.2 asks what the elenchus examines. Section 2.3 turns to the clinical interview and the psychology of verbal reports. Section 2.4 reviews the validity, reliability and fairness of oral assessment, and Section 2.5 the construct-irrelevant sources of variance that a spoken examination adds. Section 2.6 reviews the evidence that an examination is also a learning event, and Section 2.7 what it means to measure understanding rather than recall. Section 2.8 reviews dynamic assessment. Section 2.9 turns to language models as judges and Section 2.10 to integrity, surveillance and the signals a spoken examination can collect. Section 2.11 summarizes the regulatory position in the European Union. Section 2.12 draws the findings together and states the gaps the rest of the dissertation addresses.

### 2.2 The elenchus: what Socratic questioning examines

#### 2.2.1 The examination of a claim to knowledge

The image from which Gadfly takes its name is Socrates' own. In Plato's *Apology*, Socrates describes himself as attached to the city "as a gadfly to a horse, which, though large and well bred, is sluggish on account of his size and needs to be aroused by stinging" (Plato, 1966, 30e). The same text describes what his questioning was for. Examining a man reputed to be wise, Socrates concluded that "this man thinks he knows something when he does not, whereas I, as I do not know anything, do not think I do either" (Plato, 1966, 21d). The target of the examination is a person's claim to know, not only a single proposition: Socrates "tried to show him that he thought he was wise, but was not" (Plato, 1966, 21c–d).

The *Apology* also records a cost. In Plato's account the examination made those examined hostile to the examiner (Plato, 1966, 21d). An examination designed to expose the gap between thinking one knows and knowing is, for the person examined, an exposure. That cost reappears in the modern literature on oral assessment as anxiety (Section 2.5), and it bears on how a synthetic examiner should word its most confrontational move.

#### 2.2.2 The slave-boy and the problem of leading questions

The *Meno* contains the classic picture of questioning that draws a correct answer out of a learner. Socrates leads an untaught boy to the solution of a geometrical problem and insists that he is not teaching: "Do you observe, Meno, that I am not teaching the boy anything, but merely asking him each time?" (Plato, 1967, 82e). The passage separates three states that a Socratic examination must distinguish: confident but mistaken belief, the felt perplexity that follows its refutation, and true opinion that is not yet knowledge. Before the questioning the boy "thought he knew then, and confidently answered as though he knew"; afterwards he "besides not knowing does not think he knows" (Plato, 1967, 84a–b). Socrates offers a route from true opinion to understanding: "if he were repeatedly asked these same questions in a variety of forms, you know he will have in the end as exact an understanding of them as anyone" (Plato, 1967, 85c).

The passage is often cited as a model of questioning that does not lead. It is a poor one. Many of Socrates' questions between 84d and 85b are leading questions that invite a yes or no: "Has not each of the inside lines cut off half of each of these four spaces? Boy Yes." (Plato, 1967, 85a). The text that names the ideal of not teaching also shows how easily questioning slides into supplying the answer. For an examination this matters twice over. A question that contains its answer measures nothing, and a synthetic examiner generating questions from a student's previous answer is under constant pressure to make the next question easier to answer than it should be. Section 2.3 shows that a better model of the non-leading question comes from developmental psychology, not from the *Meno*.

#### 2.2.3 Consistency, not truth

The modern scholarly debate over the elenchus was set by Gregory Vlastos's 1983 article. Vlastos defined the elenchus as "a search for moral truth by adversary argument in which a thesis is debated only if asserted as the answerer's own belief and is regarded as refuted only if the negation of his thesis is deduced from his own beliefs" (Vlastos, 1983, p. 30, as cited in Wolfsdorf, 2013). From this definition he drew what became known as the problem of the elenchus: "how is it that Socrates claims to have proved a thesis false when, in point of logic, all he has proved in any given argument is that the thesis is inconsistent with the conjunction of agreed-upon premises for which no reason has been given in that argument?" (Vlastos, 1983, p. 49, as cited in Wolfsdorf, 2013). Wolfsdorf (2013) describes this article as the work that "galvanized" Anglophone scholarship on Socrates' method for the following quarter century, and reports that Vlastos himself later conceded that his solution to the problem fits only part of the dialogues.

Benson's response dissolves the problem rather than solving it. Read through Wolfsdorf's (2013) summary, Benson (1995) concludes, after examining the elenchi of several early dialogues, that Socrates shows only that the interlocutor's beliefs are inconsistent, so that the problem Vlastos posed does not arise in those texts. Benson (2000), in a book-length treatment of the epistemology of the early dialogues, places the question-and-answer elenchus among a set of epistemological concerns, with a model of knowledge that the publisher's description calls closer to modern scientific understanding than to ordinary knowledge. Seeskin (1987) reads the method as moral as well as epistemic: its goal, in the publisher's description, is "improving the soul of the respondent."

For an examination the consequence of this debate is direct. On the best-supported reading, an elenctic exchange establishes that a person's stated claims do not fit together. It does not establish which of the conflicting claims is wrong, and it does not, by itself, establish that the person lacks understanding of the subject. A student confronted with two of their own claims that conflict has shown an incoherence, and may resolve it well or badly; a resolution in either direction can be correct. The source material and the rubric, not the contradiction, must decide which claim was mistaken. The literature also yields a rule that a synthetic examiner can follow: only the answerer's own beliefs count. Vlastos's definition admits as premises only what the answerer asserts as their own, so an examiner that confronts a student with a claim the examiner supplied and the student merely accepted is not conducting an elenchus. Finally, the scholarship gives no warrant for speaking of a single "Socratic method": the elenctic pattern Vlastos described fits only part of the dialogues (Wolfsdorf, 2013).

### 2.3 The clinical interview and the psychology of verbal reports

#### 2.3.1 Following the answer without suggesting it

The method that comes closest to what a Socratic examiner must do when it follows a student's answer was developed by Piaget to study children's reasoning. Piaget rejected both the fixed questionnaire, which imposes the examiner's framing, and pure observation, which cannot direct attention to what matters. His alternative was a clinical examination that follows the child's own answers: "The only way to avoid such difficulties is to vary the questions, to make counter-suggestions, in short, to give up all idea of a fixed questionnaire" (Piaget, 1929, p. 4). The clinical method makes two demands that matter here. The examiner must follow the answer the child actually gave, and the examiner must not suggest the answer the examiner expects. A conviction that survives a counter-suggestion is better evidence than one stated once, which echoes the *Meno*'s "variety of forms" (Plato, 1967, 85c) without its leading questions.

Ginsburg (1997) restates the clinical interview as a method for research, clinical practice and education, in the tradition of Piaget and Vygotsky. In the publisher's description, the book argues that standardized tests often miss complex aspects of thinking, and its chapters include guidance for conducting the interview and an evaluation of how good clinical interviews are. The clinical interview thus offers a precedent for a flexible, answer-following examination that is also disciplined. Its discipline also yields a sharper test of whether a question leads than the *Meno* does: a question can suggest an answer by presupposing it, not only by stating it. "What makes X happen?" presupposes that something outside X makes it happen.

#### 2.3.2 What spoken reasoning can and cannot show

An oral examination asks people to put their reasoning into words. Two questions follow: does putting it into words change the reasoning, and can people's accounts of their reasoning be trusted?

On the first question, Ericsson and Simon (1980) argued, in the words of their abstract, that "verbalizing information is shown to affect cognitive processes only if the instructions require verbalization of information that would not otherwise be attended to." Their later work drew a distinction that is decisive for an examination. Thinking aloud, in which a person voices the thoughts they are already having, need not change the course of thought, but "verbal descriptions of one's thinking directed to another individual are likely to alter the course of thinking" (Ericsson & Simon, 1998, p. 181). A meta-analysis of 94 studies with nearly 3,500 participants found that plain thinking aloud had an effect on performance "indistinguishable from zero (r = -.03)," whereas "procedures that entail describing or explaining thoughts and actions are significantly reactive, leading to higher performance than silent control conditions," and that "all verbal reporting procedures tend to increase times to complete tasks" (Fox et al., 2011, abstract).

A Socratic examination is not thinking aloud. It is explaining to a listener, which is the reactive kind of verbal report. Two consequences follow. First, the examination is likely to change the student, usually for the better: the student who explains may understand more at the end of the examination than at its start, so that improvement late in a session is not proof of knowledge brought to it. Second, spoken answers will be slower than silent reasoning. Long pauses and slow speech are normal properties of the format, which matters for any integrity signal that treats latency or hesitation as suspicious (Section 2.10).

On the second question, Nisbett and Wilson (1977) reviewed evidence that "there may be little or no direct introspective access to higher order cognitive processes," and that people's reports of why they responded as they did "are based on a priori, implicit causal theories" (abstract). Their review is often cited as showing that verbal reports are worthless. It does not show that: the same abstract states that "accurate reports will occur when influential stimuli are salient and are plausible causes of the responses they produce." Ericsson and Simon (1980) located the source of inaccurate reports in requests for "information that was never directly heeded, thus forcing subjects to infer rather than remember their mental processes" (abstract). For an examination the lesson is to score justifications rather than introspection. A rubric criterion such as "depth of reasoning" should judge whether the student's explanation is correct and connected to the principles of the subject, not whether it is a faithful report of the mental process that produced the answer. Questions of the form "how did you arrive at that?" ask for exactly the kind of information that may never have been attended to.

### 2.4 Oral assessment: validity, reliability and fairness

#### 2.4.1 Describing an oral assessment

Joughin (2010) describes oral assessments as varying along six dimensions: what is assessed, interaction, authenticity, structure, who assesses, and whether the assessment is purely oral or combined with other modes. The framework, which Joughin restates in the 2010 guide from his earlier work, is reused in later medical-education work (Memon et al., 2010). It locates a synthetic Socratic examination precisely. Interaction is high, since every follow-up is written from the previous answer. Structure is low to medium: the objectives are fixed, but the path through them is not. The assessors are a panel of language models and a human examiner. The mode is purely oral.

#### 2.4.2 Reliability comes from sampling

The most widely repeated claim about oral examinations is that structure makes them reliable. The evidence supports a different and more useful claim: reliability comes mostly from sampling. Van der Vleuten et al. (2010) state the principle bluntly: "one measure is no measure, and single-point assessments are not to be trusted." They argue that competence is specific rather than generic, that objectivity does not equal reliability, and that "even the notoriously subjective, old-fashioned, oral examination can be made reliable by wide sampling across content and examiners," while estimating that "at least 3–4 h of testing time are required to obtain minimally reliable scores."

The clearest quantitative evidence comes from the oral component of the membership examination of the Royal College of General Practitioners. In 896 candidates who each sat two 20-minute orals of five topics, the reported reliability coefficients were "intercase 0.65; (B) pass/fail 0.85" (Wass et al., 2003, abstract). The variance in scores was 41% attributable to candidates, 32% to the content of the orals and 27% to examiners and general error, and the authors projected that four orals with two examiners each, or five with one, would raise reliability to 0.78 for the intercase coefficient and 0.94 for pass or fail decisions. The content sampled contributed more to score variance than the examiners did. The authors conclude that "structured oral examinations can achieve reliabilities appropriate to high stakes examinations" (Wass et al., 2003, abstract), but their own figures attribute the gain to more topics and more examiners.

The implication for a synthetic examiner is easy to miss. The temptation is to improve reliability by improving the judge: a stronger model, a larger panel. The evidence suggests that reliability will usually improve more by examining more objectives than by adding judges, because the content sampled is the larger source of error. An examination on two or three concepts, however carefully judged, is a small sample.

#### 2.4.3 The cost of adaptive questioning

Joughin (2010) names variation in follow-up questions between candidates as a threat to reliability, and warns that "examiners can mistake a student's articulateness for knowledge" (p. 7). Both warnings apply with full force to a synthetic examiner. Adaptive follow-up, the source of the method's resistance to rehearsed or relayed answers, is also the source of its incomparability: no two students receive the same questions. The tension cannot be removed, but it can be managed. If every objective opens with the same question, and every answer is scored against the same rubric for that objective rather than against the depth the conversation happened to reach, then the path can vary while the standard does not. Chapter 3 states this condition formally.

#### 2.4.4 Oral and written formats compared

The direct comparisons are small and read here only at the abstract level, but they are consistent. In a study in which 99 biology students were randomly assigned to oral or written versions of the same questions, and a second cohort of 29 took both, both cohorts scored significantly higher in the oral format; students reported more nervousness about orals but often judged them more useful and more authentic, and "there was no evidence of particular groups of students being disadvantaged in the oral tests" (Huxham et al., 2012, abstract). The last finding is easily overstated. It does not show that anxiety has no effect on oral scores, and the sample was too small to detect small effects in subgroups. A single-course case study of online oral examination found the format workable and useful against impersonation and dishonesty, with validity, reliability and fairness depending on careful planning (Akimov & Malin, 2020). A survey of students who took scaffolded authentic assessments ending in an interactive oral reported that students saw such assessments as deterring misconduct (Sotiriadou et al., 2020). None of these studies involved automated scoring.

### 2.5 Construct-irrelevant variance: anxiety, accent and speech recognition

#### 2.5.1 Anxiety

Joughin (2010) puts the concern in one sentence: "anxiety that interferes with a student's performance will not give a true indication of his or her ability" (p. 7). The evidence that test anxiety is associated with poorer performance is extensive. A meta-analysis of 562 studies concluded, in the words of its abstract, that "test anxiety (TA) causes poor performance," and reported that "improved test performance and grade point average (GPA) consistently accompany TA reduction" (Hembree, 1988). The first statement is a causal reading of largely correlational data; the second, that scores rise when anxiety is treated, is the stronger evidence. A later meta-analysis of 238 studies found test anxiety "significantly and negatively related to a wide range of educational performance outcomes," with relationships in the small-to-moderate range, and higher anxiety where tests are perceived as difficult or carry high stakes (von der Embse et al., 2018, abstract). Anxiety about speaking a second language is, moreover, a construct distinct from general test anxiety (Horwitz et al., 1986).

A spoken examination adds speaking anxiety to test anxiety, and an examination conducted in a second language adds language anxiety to both. The effect is modest on average but real, and it bears most heavily on the students least able to absorb it. Joughin (2010) recommends rehearsal ("Practising presentations in class and providing rehearsals for vivas may help," p. 7) and alternative arrangements for students for whom anxiety is disabling. For a synthetic examiner, design choices that add time pressure, such as a fixed silence that ends an answer or a clock that keeps running during a pause, fall hardest on anxious, second-language and disfluent speakers.

#### 2.5.2 Speech recognition

The largest documented fairness risk in a voice examination is not the prejudice of a human listener but the speech recognizer. In a test of five commercial automatic speech recognition systems on speech from black and white American speakers, "all five ASR systems exhibited substantial racial disparities," with an average word error rate of 0.35 for black speakers against 0.19 for white speakers, and "the race gap was equally large on a subset of identical phrases spoken by black and white individuals" (Koenecke et al., 2020, abstract). Six leading recognizers tested on the speech of people who stutter showed "consistent and statistically significant accuracy bias across all ASRs against disfluent speech," in word error rate, character error rate and semantic accuracy (Mujtaba et al., 2024, abstract).

In a synthetic oral examination the transcript is what the judges score. A transcription error is therefore not noise around the measurement but a change in the evidence, and it is a change that falls systematically on identifiable groups. The literature implies three obligations: measure the recognizer's error rate by accent, dialect and fluency group; make transcript correction easy and visible to reviewers; and flag segments the recognizer was unsure of, so that they are not scored as wrong content. It also implies that a typed-answer accommodation, which Joughin's (2010) recommendation of adjustments for speech or hearing impairment supports, is a requirement rather than a convenience.

### 2.6 The examination as a learning event

#### 2.6.1 Retrieval practice

A large literature shows that being tested on material improves later retention of it more than studying it again. Roediger and Karpicke (2006) put the conclusion in their title and abstract: "testing is a powerful means of improving learning, not just assessing it." Their result needs its context. One week after study, students who had been tested "recalled 56% of the material, whereas the restudy group recalled only 42%"; but five minutes after study, restudying had been ahead (81% against 75%), and the final test was free recall of the same passage. The study shows durable retention, not understanding. Karpicke and Blunt (2011) went further, showing that retrieval practice "enhanced long-term learning above and beyond elaborative study with concept mapping by one and a half standard deviations (d = 1.50)" (Fig. 1 caption), on a final test that included inference questions requiring several concepts to be connected, and that "the advantage of retrieval practice occurred even when the criterial test involved creating concept maps" (abstract).

The meta-analyses agree on the direction and qualify the size. Rowland (2014) analysed 159 effect sizes from 61 studies and found an overall random-effects effect of g = 0.50 (95% CI [0.42, 0.58]), with high heterogeneity and with "initial recall tests yielding larger testing benefits than recognition tests" (abstract). Rowland's moderator analysis also found essentially no benefit from testing when there was no feedback and success on the practice test was 50% or lower. Adesope et al. (2017) found that "practice tests are more beneficial for learning than restudying and all other comparison conditions," with effects "moderated by the features of practice tests" (abstract). Dunlosky et al. (2013) "rate practice testing as having high utility," noting that "the advantage of practice testing with feedback over restudy is extremely robust" (Section 8.5).

Two boundary conditions qualify the evidence. First, benefits transfer, but selectively. Pan and Rickard (2018) found that "testing can yield transferrable learning as measured relative to a nontesting reexposure control condition" (d = 0.40, 95% CI [0.31, 0.50]), with transfer "greatest across test formats, to application and inference questions," but "weakest to rearranged stimulus-response items, to untested materials seen during initial study, and to problems involving worked examples" (abstract). Second, the effect may shrink with the complexity of the material. Van Gog and Sweller (2015) argue that research "indicated that the testing effect decreases as the complexity of learning materials increases" and may "even disappear when the complexity of learning material is very high" (abstract). Karpicke and Aue (2015) reply that "van Gog and Sweller's idea of 'element interactivity' is not defined in a quantitative, measurable way" (abstract). The debate is unresolved.

For a synthetic Socratic examination, this literature supports a claim of formative value with three conditions attached. Open, recall-type questions of the kind the method asks are the format that helps most (Rowland, 2014). Repeated attempts help only if the student learns, between attempts, what was missing; an examination that gives no feedback during the session can deliver its learning benefit only through the feedback in its released results. And the benefit will be specific to what was asked. A student who retakes an examination improves on what the examination probed, not on what it did not reach (Pan & Rickard, 2018).

#### 2.6.2 Self-explanation and the "why" question

The follow-up that asks "why does that follow?" draws on a second literature. Chi et al. (1989) found, in protocols of physics students studying worked examples, that "'Good' students learn with understanding: They generate many explanations which refine and expand the conditions for the action parts of the example solutions, and relate these actions to principles in the text" (abstract, p. 145). The finding is correlational. Chi et al. (1994) supplied the experiment: fourteen eighth-grade students prompted to explain each line of a text on the circulatory system gained more from pretest to posttest than ten who read the text twice, and on the harder questions, which required inference, the prompted group improved by 22.6% and the unprompted group by 12.5% (abstract, p. 439; pp. 452–454). A meta-analysis of 69 effect sizes found an overall effect of g = .55 for inducing self-explanation, and prompts phrased as questions (g = .56) did at least as well as prompts phrased as instructions (g = .40) (Bisra et al., 2018). Dunlosky et al. (2013) gave elaborative interrogation and self-explanation "moderate utility assessments," with concerns about longer and more complex material and about durability over educationally relevant delays.

The bearing on an examination is real but indirect. The self-explanation literature studies learners explaining material to themselves while studying, and Chi, as Bisra et al. (2018) report, distinguishes self-explanation from "explaining to another." Answering an examiner's "why" question is explaining to another. The evidence therefore supports the "why" question in two ways by analogy: as a probe, because an answer that ties a step to the principle behind it marks a learner who understands (Chi et al., 1989), and as a moderate aid to learning (Bisra et al., 2018; Chi et al., 1994). It also supports keeping hints out of the scored examination, since self-explanation outperformed explanations supplied by an instructor in the meta-analysis (Bisra et al., 2018).

### 2.7 Measuring understanding rather than recall

#### 2.7.1 Taxonomies of objectives and of answers

The original taxonomy of educational objectives ordered six classes, Knowledge, Comprehension, Application, Analysis, Synthesis and Evaluation, which its authors said "appears to us to represent something of the hierarchical order of the different classes of objectives" (Bloom et al., 1956, Introduction). The revision renamed the classes as verbs, moved Synthesis above Evaluation and renamed it Create, added a separate knowledge dimension (factual, conceptual, procedural and metacognitive), and relaxed "the requirement of a strict hierarchy ... to allow the categories to overlap one another" (Krathwohl, 2002). The revision's own overview notes that some processes under Understand, such as explaining, are more complex than at least one under Apply (Krathwohl, 2002). The rank of a category is therefore not a measure of difficulty, and an examination should not award points by category.

The original taxonomy draws a line that matters for the Socratic move that asks "what would change if...?" Predicting consequences from conditions already given is extrapolation, which Bloom et al. (1956) place under Comprehension: it "differs from application, however, in that the thinking is based on what is given rather than on some abstraction brought from the other experiences to the situation." Application requires something else: "the emphasis in writing application items is on getting situations new to the student" (Bloom et al., 1956). A what-if inside the scenario the student has described tests extrapolation; a question that requires the principle in a situation new to the student tests application. An examination that claims to probe transfer must ask the second.

Taxonomies of objectives describe what an examiner intends to test. A different tradition classifies the structure of the answer a student actually gives. The SOLO taxonomy of Biggs and Collis (1982) is commonly described as distinguishing answers that make one relevant point, several unconnected points, points integrated into a coherent whole, and a whole generalized to a new domain. The book was located but not read in this research pass, so the dissertation uses the distinction only as a commonly described one and does not attribute specific wording to it. It is nonetheless the kind of distinction a claim ledger makes visible: a ledger records how many relevant claims a student made and whether they were connected.

#### 2.7.2 Transfer

What an examination of understanding most wants to know is whether the student can use what they know in a situation they have not seen. The research on transfer gives that question less support than it is usually given. Barnett and Ceci (2002) observe that the debate over far transfer has "made little progress toward resolution," attribute this to "a failure to specify various dimensions along which transfer can occur," and propose nine such dimensions, concluding that "estimation of a single effect size for far transfer is misguided" and that, while "evidence for transfer under some conditions is substantial," "critical conditions for many key questions are untested" (abstract). An examination that labels its transfer questions "near" or "far" says too little; it should say in what respects each question's situation differs from the source material.

Bransford and Schwartz (1999) add a sharper point. Most transfer studies use what they call "sequestered problem solving," in which learners have "no opportunities ... to demonstrate their abilities to learn to solve new problems by seeking help from other resources such as texts or colleagues or by trying things out" (pp. 8–9), receive feedback and revise. Their thesis is that this methodology "is responsible for much of the pessimism about evidence for transfer" (p. 9), and they propose measuring "preparation for future learning" instead. A Socratic examination with no resources and no hints is a sequestered measure. It measures unaided direct application and will understate transfer. Improvement across attempts after feedback is a candidate measure of preparation for future learning, but one that would need its own validation.

### 2.8 Dynamic assessment

#### 2.8.1 The zone of proximal development

Every examination makes a choice about help. The Socratic examination described in Chapter 1 gives none: its clarifying questions must not hint. Dynamic assessment makes the opposite choice, and its literature is the strongest argument that the choice has a cost.

The tradition begins with Vygotsky's zone of proximal development, "the distance between the actual developmental level as determined by independent problem solving and the level of potential development as determined through problem solving under adult guidance or in collaboration with more capable peers" (Vygotsky, 1978, p. 86). Vygotsky's argument is that unaided performance is incomplete information: "The actual developmental level characterizes mental development retrospectively, while the zone of proximal development characterizes mental development prospectively" (pp. 86–87). Two corrections to the usual retelling should be recorded. The passage says "more capable peers"; the phrase "more knowledgeable other" is not in it. And the 1978 text is an edited translation, so its wording should be quoted as that edition's.

#### 2.8.2 Interventionist and interactionist assessment

Dynamic assessment, in the description of Haywood and Lidz (2007), "embeds interaction within the framework of a test-intervene-retest approach." Its traditions divide on how the help is given. Lantolf and Poehner (2004) ground dynamic assessment in the zone of proximal development and define it by the examiner's mediation of performance through prompts, hints and leading questions, both to reveal abilities and to promote development during the assessment. Later work distinguishes an interventionist model, in which, as Poehner puts it, mediators "must instead follow a highly scripted approach to mediation in which all prompts, hints, and leading questions have been arranged hierarchically," from an interactionist model in which mediation emerges from the dialogue (Poehner, 2008, pp. 44–45, as cited in Sarabi Asl et al., 2024). Sarabi Asl et al. (2024) observe that the scripted model, "while bringing about the comfort of better generalizability and large-scale administration, deprives learners and instructors of co-creating a ZPD" (p. 5).

A current interventionist protocol shows what a fixed ladder of prompts looks like. Children who could not solve an item after a general instruction "received help according to a standardised graduated prompting protocol that consisted of four prompts," running from metacognitive to step-by-step, which "were only provided after an incorrect answer," and scoring used pre- and post-test accuracy together with "the number of hints children were provided with, and the type of hints they utilised" (Stad et al., 2019). The interactionist tradition rejects such ladders. The authors of the Learning Potential Assessment Device write that "interactions that are limited in their mediational flexibility, such as graduated prompting ... will not make possible the detection of the microchanges which can be produced in the individual on a variety of levels" (Feuerstein et al., n.d.). The field does not agree that the number of prompts a student needs is a good measure.

#### 2.8.3 Validity and reliability of dynamic assessment

The claim that dynamic assessment predicts later achievement better than conventional tests is overstated. A review of 24 studies found "superior predictive validity for DA when feedback is not contingent on student response, when applied to students with disabilities rather than at-risk or typically achieving students, and when independent DA and criterion-referenced tests were used as outcomes" (Caffrey et al., 2008, abstract). The same research group later summarized that review as finding "that traditional tests and DA similarly predicted future academic performance" (Fuchs et al., 2011), while showing that their own scripted dynamic assessment of early reading, with "five levels of more and more explicit scaffolded instruction," contributed unique variance to later reading outcomes beyond established predictors, at a cost of 20 to 30 minutes per administration. The finding that prediction was best when feedback was not contingent on the student's response favours a fixed ladder over improvised help.

The measurement problems are acknowledged within the field. De Beer (2006) writes that "there is a paucity of published empirical data on the reliability and validity of dynamic testing," reports that, "according to Grigorenko and Sternberg (1998), multiple attempts to quantify learning potential and transform such testing into robust psychological diagnostic tools have not produced consistent results," and warns that "the difference score or ZPD has often (incorrectly) been referred to as that which indicates 'potential'." A gain score, or a count of prompts needed, is confounded with the level at which the student started.

For a Socratic examination the conclusion is a separation, not a rejection. Nothing in this literature undermines an unhinted examination: unaided performance is incomplete information, not wrong information. But the literature supports, as a separate activity, a practice mode in which help is given and the student's response to it is observed. If such help is ever counted, comparability requires the interventionist form: the same instructor-written ladder of prompts for every student, given only after an inadequate answer. Help improvised by a language model is interactionist and may be valuable for learning, but its prompt counts are not comparable between students. None of this evidence, which comes from young children, students with disabilities, and decoding and reasoning tasks, has been tested on adults answering conceptual questions aloud.

### 2.9 Machine judgment

#### 2.9.1 Language models as judges

The use of language models to judge other models' outputs has produced the first systematic evidence on their reliability as raters, and it shows both promise and specific, measurable biases. In the study that established the practice, the strongest model's agreement with human preferences "reaches 85%, which is even higher than the agreement among humans (81%)," but this figure is for pairwise preferences between chat responses with ties excluded, not for rubric scoring (Zheng et al., 2023, p. 7). The same study found a strong position bias: when two answers were presented in swapped order, "only GPT-4 outputs consistent results in more than 60% of cases" (p. 5). It is often cited as showing that models favour their own outputs; it does not, and its authors state that their study "cannot determine whether the models exhibit a self-enhancement bias" (p. 5). It also found that human evaluators shown the model's judgment "deemed GPT-4's judgments reasonable in 75% of cases and are even willing to change their choices in 34% of cases" (p. 7), a finding about human deference that bears directly on how human and machine judgment should be combined.

Position bias can be severe. Wang et al. (2024) report that one model "could beat ChatGPT on 66 over 80 tested queries with ChatGPT as an evaluator" (abstract), with verdicts changing on swapped order in up to 82.5% of comparisons for one judge, and with the rate of such conflicts rising as the quality gap between the two answers narrows: judgments are least stable exactly where they are hardest. Self-preference, separately measured, exists: two models distinguished their own outputs from others' above chance, and after fine-tuning the strength of self-preference correlated linearly with that ability to recognize their own outputs (Panickssery et al., 2024, abstract and Fig. 1).

A panel of different models is one response. Verga et al. (2024) built a panel from "three models being drawn from three disparate model families" and found that it agreed with human judgments better than a single large model did on question-answering tasks (Cohen's kappa 0.763, 0.906 and 0.867 for the panel against 0.627, 0.841 and 0.830 for the single judge), and that the single large judge ranked a variant of itself higher than its actual position. The evidence is a preprint on tasks that are not educational. Chiang and Lee (2023) found that a model given the same instructions and samples used in a human evaluation produced ratings consistent with expert human ratings on story-generation tasks, while opening their abstract with the caution that "human evaluation is indispensable and inevitable for assessing the quality of texts."

#### 2.9.2 Language models scoring student work

The studies of language models scoring student work are small, recent and read here mostly at the abstract level. On short-answer questions in K–12 education, one model "with basic few-shot prompting performed well (Kappa, 0.70)," "very close to human-level performance (0.75)" (Henkel et al., 2024, abstract). On a new dataset of reading-comprehension answers, agreement with expert raters reached a quadratic-weighted kappa of 0.92 (Henkel et al., 2023, preprint abstract), the highest figure in this literature and on the most constrained items. Essay scoring with an earlier model showed "a certain level of accuracy and reliability" that "could provide valuable support for human evaluations" (Mizumoto & Eguchi, 2023, abstract). A study of handwritten physics solutions concluded that for summative, high-stakes use the tool "should only be used for an initial round of grading that sorts and flags solution approaches" (Kortemeyer, 2023, preprint abstract). A study of essays by English-language learners found that the best model showed "excellent intrarater reliability and good validity," but that interrater reliability "decreased slightly over time" and that "all models are subject to fluctuations in their performance" (Pack et al., 2024, abstract). Every one of these studies frames machine scoring as support for human judgment, triage, or formative feedback, not as a replacement for it in high-stakes decisions.

#### 2.9.3 The human baseline

Machine agreement must be read against human agreement, which is lower than commonly assumed. In the large public demonstration of automated essay scoring, "human quadratic-weighted kappas ranged from 0.61 to 0.85 and were closely followed by the machine ranges which went from 0.60 to 0.84" (Shermis, 2014). The same study warns that "a predictive model may do a good job of matching human scoring behavior, but for reasons unrelated (or unsatisfactorily related) to the construct of interest," and that scoring measures may be "subject to manipulation by test-takers and coaches." The widely used acceptance thresholds for automated scoring are aggregate ones: "a QWK or correlation of .70 or higher is commonly used as an acceptable level of concordance between the human and automated scores," together with a degradation from human–human agreement of less than .10 and a standardized mean difference of less than .15 (Williamson et al., 2012, as cited in Casabianca et al., 2023). They apply to a set of answers, not to any single answer.

Human marking of open answers has a long record of unreliability. A review of the literature on marking reliability found that "irrespective of the subject area, however, essay questions presented the greatest reliability problem," reporting a historical study in which agreement between pairs of markers of history scripts "ranged from -0.41 to 0.85 with an average of just 0.44," and recommended "removing the names from the essays," training markers and "the double marking of essays" (Meadows & Billington, 2005, pp. 18, 41). Neither human nor machine judgment is a gold standard, which argues for comparing them blind and reconciling disagreements on the evidence, rather than letting either override the other.

#### 2.9.4 Security and fairness of machine judgment

Language-model graders can be manipulated. In a study of injection strategies hidden in student work, "all the experiments show that the considered LLMs are vulnerable to every injection strategy that could be tested," and grading assignments in batches made all models more lenient regardless of injection (Milani et al., 2025, p. 11). In a deployment with more than a thousand students, "students can easily manipulate the LLM-based evaluator to output specific strings" and could obtain high scores without meeting the rubric (Chiang et al., 2024, abstract). A transcript of a student's speech is untrusted input to a grader, and a spoken answer can carry instructions addressed to it as easily as a written one.

One widely cited fairness result is often misattributed to graders. Liang et al. (2023) showed that seven widely used detectors of machine-written text "incorrectly labeled more than half of the TOEFL essays as 'AI-generated' (average false-positive rate: 61.3%)." The finding concerns detectors, not graders, and should not be cited as evidence that language-model graders penalize non-native writers. It is strong evidence that detectors of machine-written text should never be used as integrity signals.

### 2.10 Integrity and the signals a spoken examination can collect

#### 2.10.1 Proctoring

The evidence that unproctored online examinations are inflated is consistent. In one online course, students in proctored tests "scored, on average, 17 points lower [95% CI: 14, 20]" and "used significantly less time" than in unproctored tests (Alessio et al., 2017, abstract). In two courses, proctoring "was associated with a decrease in average performance," which the authors "interpret ... as evidence that cheating took place in the online courses prior to proctoring" (Dendir & Maxwell, 2020, abstract). Both inferences are from score changes; neither study observed cheating, and neither shows that any particular signal identifies a person who cheated.

Proctoring has its own costs. Coghlan et al. (2021) argue that "facial recognition functions in OP systems sometimes have more difficulty recognizing darker skin tones," that "some test-takers may simply have idiosyncratic exam-taking styles, or disabilities and impairments" that trigger flags, and that institutions should ask whether alternative types of assessment could be used instead. In one product at one university, students with darker skin and Black students were significantly more likely to be flagged for instructor review, and women with the darkest skin tones most of all, though the darkest-skin subgroup was small (Yoder-Himes et al., 2022). The study often cited for such disparities, Buolamwini and Gebru (2018), measured commercial gender classification, in which "darker-skinned females are the most misclassified group (with error rates of up to 34.7%)" while "the maximum error rate for lighter-skinned males is 0.8%"; it is evidence about facial analysis in general, not about proctoring. Among 631 students, higher trait test anxiety was associated with lower scores, especially for highly anxious students taking proctored online examinations (Woldeab & Brothen, 2019, abstract).

#### 2.10.2 Gaze

A spoken examination with a webcam can estimate where the student is looking. Webcam gaze estimation is coarse. The open-source tracker most often used reported an average error of 174.9 pixels in its best online configuration and an average visual angle of 4.17 degrees against a commercial eye tracker, measured with four participants (Papoutsaki et al., 2016). A second study found "an offset of about 172 px (15% of screen size, 3.94° visual angle)" in the laboratory, with online data "slightly less accurate (18% of screen size, 207 px)" and more variable (Semmelmann & Weigelt, 2018, abstract). Reading, by contrast, is a matter of fine detail: "the average fixation duration in reading is about 200-250 ms" and "the average saccade length (for alphabetic writing systems) is about 7-9 letter spaces" (Rayner, 2009). A tracker with an error of several degrees cannot resolve word-level reading. It cannot reliably tell reading notes from reading the question, looking away to think, or looking at nothing.

#### 2.10.3 Voice

Acoustic features of a spoken answer are sometimes offered as signs of stress or deception. The evidence gives them no support. The National Research Council found that "although proponents of voice stress analysis claim high levels of accuracy, empirical research on the validity of the technique has been far from encouraging," and that the research offered "little or no scientific basis for the use of the computer voice stress analyzer" (National Research Council, 2003, pp. 167–168). In a laboratory test of one commercial system, "the 'true positive' (or hit) rates for all examiners averaged near chance (42-56%)," with false positive rates of 40% to 65%, and the system "operated at about chance levels in the detection of truth, deception, and the presence of high and low vocal stress states" (Harnsberger et al., 2009, abstract). In a field test with 319 arrestees, "the programs were not able to detect deception at a rate any better than chance"; sensitivity averaged 15%, and the systems produced on average about nine false flags for every true one (Damphousse et al., 2007). Even the acoustic correlates of stress are inconsistent: "increase in fundamental frequency is the most commonly reported effect of stress in well-controlled trials. The trend, however, is not universal" (Giddens et al., 2013, abstract).

The wider literature on deception points in the same direction. A meta-analysis of 158 cues found that liars "are more tense" and "tell less compelling tales," but "many behaviors showed no discernible links, or only weak links, to deceit" (DePaulo et al., 2003, abstract). People judging truth and lies "achieve an average of 54% correct lie-truth judgments," though they are "more accurate in judging audible than visible lies" (Bond & DePaulo, 2006, abstract). The only cues with some support concern the content of what is said. That observation matters for the design of an examination: if anything distinguishes an answer the student cannot stand behind, it is more likely to be found in the content of the student's replies to follow-up questions than in the sound of the student's voice.

### 2.11 The regulatory position in the European Union

The European Union's Artificial Intelligence Act classifies as high-risk "AI systems intended to be used to evaluate learning outcomes, including when those outcomes are used to steer the learning process of natural persons in educational and vocational training institutions at all levels," and "AI systems intended to be used for monitoring and detecting prohibited behaviour of students during tests" (Regulation (EU) 2024/1689, Annex III, points 3(b) and 3(d)). High-risk systems "shall be designed and developed in such a way, including with appropriate human-machine interface tools, that they can be effectively overseen by natural persons" (Art. 14(1)), and those overseeing them must remain aware of "the possible tendency of automatically relying or over-relying on the output produced by a high-risk AI system (automation bias)" (Art. 14(4)(b)). A synthetic Socratic examiner used for grades in an EU institution falls under both points.

The Act also prohibits "the use of AI systems to infer emotions of a natural person in the areas of workplace and education institutions, except where the use of the AI system is intended to be put in place or into the market for medical or safety reasons" (Art. 5(1)(f)). An emotion recognition system is one for "identifying or inferring emotions or intentions of natural persons on the basis of their biometric data" (Art. 3(39)), and biometric data includes personal data relating to "behavioural characteristics" (Art. 3(34)). The Commission's guidelines, which are non-binding, read "emotions" "in a wide sense" (para. 247), list voice and eye tracking among behavioural biometrics (para. 251), and give an educational example directly on point: eye tracking used "to detect if unauthorized material is used ... is not prohibited, because the system does not identify or infer emotions. By contrast, if the system is also used to detect emotions, such as emotional arousal and anxiousness, this would fall within the scope of the prohibition" (European Commission, 2025, para. 255). They add that safety means "protection of life and health and not to protect other interests, for example property against theft or fraud" (para. 258), so integrity is not a safety reason. A system that labels acoustic features of a student's voice as "strain," and especially one that uses them to decide how hard to question the student, is exposed to that reading. The Act states that it applies from 2 August 2026 (Art. 113); whether later amendments have changed the dates for the high-risk rules is recorded in Appendix A as an open item.

### 2.12 Synthesis and gaps

The six literatures converge on a small number of findings that frame the rest of the dissertation.

1. **The elenchus tests coherence; the rubric must test correctness.** A contradiction between two of a student's own claims is evidence that the student's account does not hold together. It does not identify which claim is wrong, and only the student's own assertions may serve as premises (Section 2.2).
2. **Following the answer is an old and disciplined method, and the discipline is the point.** The clinical interview shows how to follow an answer without suggesting one, including by presupposition (Section 2.3).
3. **Explaining to a listener changes the explainer.** A Socratic examination is reactive. It slows people down and usually improves their performance, so late gains are not proof of prior knowledge (Section 2.3).
4. **Reliability comes from sampling.** More objectives and more independent judgments improve reliability more than a better examiner does; adaptive follow-up is compatible with comparable scores only if scoring is anchored to fixed objectives and rubrics (Section 2.4).
5. **The largest fairness risks are construct-irrelevant: anxiety and, above all, speech recognition** (Section 2.5).
6. **An examination can be a learning event,** but only with feedback between attempts and only for what was probed (Section 2.6).
7. **Understanding is shown by application to situations new to the student,** and an unaided examination measures sequestered transfer (Section 2.7).
8. **Help belongs in a separate mode.** If it is ever counted, it must be scripted and the same for everyone (Section 2.8).
9. **Machine judges have measurable biases, and so do humans.** Panels of different model families, rubric anchoring, blind human grading, and reconciliation rather than override are each supported; humans shown a machine verdict defer to it (Section 2.9).
10. **Integrity signals are weak.** Gaze is too coarse to resolve reading; voice-stress analysis performs at chance; detectors of machine text are unreliable and biased. Under EU law, inferring emotional states of students is prohibited (Sections 2.10 and 2.11).

Three gaps remain, and each shapes a later chapter.

- **No study of the central practice.** No source found in this review tests language models grading spoken, transcribed Socratic answers, multi-turn follow-ups, or per-objective scoring in an oral examination. The agreement figures of Section 2.9 describe neighbouring tasks. Chapter 7 designs the study that would supply the missing evidence.
- **No direct evidence that follow-up questioning defeats relayed answers.** The claim that a person relaying answers from another source will contradict themselves under questioning, which motivated Gadfly's design, has no support in the sources reviewed. The evidence on surveillance supports Socratic follow-up as an integrity control only indirectly, by showing that the alternatives are weak (Section 2.10). The claim is treated as a hypothesis in Chapters 5 and 7.
- **No evidence on some students.** No source on neurodivergent students in oral or voice examinations was verified. Chapter 6 records the gap.

The next chapter turns the first four findings into a formal model of the examination as evidence-gathering by questioning.

## Chapter 3 — A Formal Model of Evidence-Gathering by Questioning

*Status: outline. This chapter is planned and not yet drafted. Each section below states what it will argue. Any source it needs beyond those reviewed in Chapter 2 must be located and verified, and entered in Appendix A, before it is cited.*

### 3.1 The examination as sequential observation

An examination is modelled as a sequence of observations about a student's understanding of a fixed set of objectives. For each objective the student has an unobserved state of understanding. Each question is an experiment chosen in the light of the answers so far, and each answer is an observation whose probability depends on that state and on the question asked. The examiner's task is to choose questions; the grader's task is to infer the state. The chapter keeps these two tasks apart throughout.

### 3.2 What a question is for

A question is valuable to the extent that its answer is expected to reduce uncertainty about the student's state on the objective it serves. The section will state this as expected information gain and show that the Socratic moves of Chapter 1 (Elicit, Clarify, Probe, Counterexample, Contradiction, Apply or Transfer, Synthesize, Close) differ in the kind of uncertainty they reduce: whether the student holds a claim, whether the claim is understood, whether it coheres with the student's other claims, and whether it extends to a new situation. It will relate the model to item response theory and computerized adaptive testing, which De Beer (2006) proposes as remedies for the measurement problems of dynamic testing; the measurement-theory sources themselves are still to be located and verified.

### 3.3 Why questions written from the student's own words are informative

A follow-up generated from the student's previous answer is conditioned on information that no prepared answer contains. The section will state the condition under which such a question discriminates between a student who understands and one who is reproducing an answer obtained elsewhere: the follow-up must require the principle behind the earlier answer, not its wording. It will state, as a hypothesis for Chapter 7, the claim that relayed answers lose coherence under follow-up, which Chapter 2 found unsupported.

### 3.4 The ledger and the logic of contradiction

The claim ledger is modelled as the set of propositions the student has asserted as their own. A contradiction is a subset of the ledger that is jointly inconsistent given the source material. Following Section 2.2, the model shows that a contradiction is evidence about coherence, that it does not by itself identify which claim is false, and that only asserted claims may enter the premise set.

### 3.5 Adaptive paths and comparable scores

The central result of the chapter. Because the question path differs between students, scores are comparable only if (a) every objective is scored against the same rubric whatever path was taken, (b) the score depends on the evidence about the objective and not on the length or depth of the path, and (c) the number of follow-ups each student received is reported rather than silently absorbed into the score. The section will show how violating each condition lets the path, rather than the student, determine the grade.

### 3.6 Sampling and reliability

The model is extended across objectives to show why, consistent with Wass et al. (2003) and van der Vleuten et al. (2010), reliability is governed more by the number of objectives sampled than by the precision of the judge on any one of them, and to give a rule for the minimum number of objectives below which per-objective scores should be reported only as formative.

### 3.7 Combining judges

Panel members and the human examiner are modelled as noisy raters of the same objective-level evidence. The section will state the conditions under which averaging across model families reduces bias, the cost of a human seeing the panel's verdict before judging (Zheng et al., 2023), and the reconciliation rule: disagreements beyond a set band are resolved on the evidence by a second reviewer, not by either source overriding the other.

### 3.8 Summary of the model's requirements

A list of the formal conditions that Chapter 4 turns into requirements of the method.

## Chapter 4 — The Gadfly Method

*Status: outline. Planned and not yet drafted.*

### 4.1 From model to method

How the conditions of Chapter 3 become requirements that an implementation can be checked against, and why the method is stated independently of Gadfly's code.

### 4.2 Grounding

Source material, the concept map and the per-concept rubric, each rubric point tied to the passages it came from, reviewed and edited by the instructor before any examination. Requirement: no judgment without a cited passage.

### 4.3 The moves and the planner

The eight moves, what each is for in the terms of Section 3.2, and the rules that govern them: a fixed opening question per objective; follow-ups written from the student's own words; the non-hinting rule, including hints by presupposition (Section 2.3); the Apply or Transfer move restricted to situations new to the student (Section 2.7); and the Contradiction move, which quotes the student's own claims back and accepts a resolution in either direction (Section 2.2).

### 4.4 The claim ledger

Extraction of claims from transcripts, what counts as an asserted claim, and how contradictions are detected and presented.

### 4.5 Judgment

The panel of language models from different families, rubric anchoring with per-level descriptors, evidence before score, single-answer scoring rather than pairwise comparison, one answer per call, transcripts treated as untrusted data, and the controls for position, length and self-preference bias reviewed in Section 2.9.

### 4.6 Human oversight

Blind human grading, the frozen grading policy, disagreement bands, mandatory reconciliation, the separation of the permission to score from the permission to approve, publication and appeal.

### 4.7 Speech

Transcription, transcript confirmation and correction, flagging of low-confidence segments, and the typed-answer accommodation (Section 2.5).

### 4.8 Integrity context

The signals collected, how they are shown to reviewers (after the content-based score, never in the grade), and the signals the method excludes (Sections 2.10 and 2.11).

### 4.9 Practice mode

The separate, optional practice mode with an instructor-written ladder of prompts, kept apart from scored examinations in data and display (Section 2.8).

### 4.10 Requirements table

Every requirement, the evidence that motivates it, and how an implementation is checked against it.

## Chapter 5 — Validity, Fairness and Integrity

*Status: outline. Planned and not yet drafted.*

### 5.1 What the score means

The validity argument for a synthetic Socratic examination: the inferences a grade is meant to support, and the evidence each inference needs.

### 5.2 Construct-irrelevant variance

Articulateness and fluency, anxiety, second-language speaking, accent, dialect and disfluency, and transcription error (Section 2.5): how each enters the score and how the method keeps it out.

### 5.3 Fairness of machine judgment

Bias by speaker group, monitoring of score gaps, a test with paraphrased answers of equal content and unequal fluency, and the human baseline (Section 2.9).

### 5.4 Integrity by design

The argument that the main integrity control is the examination itself: questions written from the student's own words, which a relayed answer does not anticipate. What the evidence supports (the weakness of the alternatives) and what it does not yet support (direct evidence that follow-up defeats relayed answers).

### 5.5 The limits of surveillance

Gaze, voice and detectors of machine text: what each can show, the disparities in flag rates, and the legal limits on inferring emotional states in education (Sections 2.10 and 2.11).

### 5.6 Accountability

Audit, appeal, the student's view of the evidence, and the separation between the system that recommends and the person who decides.

## Chapter 6 — Limitations

*Status: outline. Planned and not yet drafted.*

### 6.1 No empirical evaluation yet

The dissertation states conditions and designs a test; it does not report one.

### 6.2 Evidence from neighbouring tasks

The agreement evidence on machine judgment comes from essays, short written answers and chat responses, not from spoken Socratic answers (Section 2.12).

### 6.3 Sources read at the abstract level

Which findings rest on abstracts or on secondary sources, and what would change if the primary works were read (Appendix A).

### 6.4 Populations the evidence does not cover

Neurodivergent students; adult learners in dynamic assessment; languages other than English.

### 6.5 The examiner changes the examined

The reactivity of explaining to a listener (Section 2.3) and what it means for treating a session as a measurement.

### 6.6 Dependence on model providers

Drift in model behaviour over time (Pack et al., 2024), the reproducibility of judgments, and the audit of prompt sets.

### 6.7 Authorship

The limits of a dissertation on synthetic examiners written with a synthetic collaborator, and how the verification ledger answers them.

## Chapter 7 — The Design of an Empirical Test

*Status: outline. Planned and not yet drafted. The pilot described in the project's own plan (Gadfly repository, `docs/PILOT.md`) is the first step; this chapter designs the study that would follow it.*

### 7.1 Questions the test must answer

Agreement between the panel and blind human examiners; agreement between human examiners; reliability as a function of the number of objectives; whether students who understand the material but express it poorly can still show it; whether relayed or machine-supplied answers survive follow-up; fairness across speaker groups; and the formative effect of released results across attempts.

### 7.2 Design

Participants, materials, conditions (including one in which participants relay answers from another source), double-blind human marking, and the calibration set.

### 7.3 Measures

Quadratic-weighted kappa and standardized mean difference against the aggregate thresholds of Williamson et al. (2012, as cited in Casabianca et al., 2023); word error rate by speaker group; per-objective score change between attempts with and without feedback; the rate and causes of reconciliation.

### 7.4 Thresholds and decision rules

The results that would show the method is fit to inform a grade, and the results that would show it is not.

### 7.5 Ethics

Consent, data minimization, the treatment of integrity signals, and the regulatory position (Section 2.11).

### 7.6 Threats to validity

Sample, setting, the novelty of the format, and the reactivity of the examination itself.

## Chapter 8 — Conclusion

*Status: outline. Planned and not yet drafted.*

### 8.1 The answer to each research question

One section per research question of Section 1.4.

### 8.2 What a synthetic examiner can and cannot do

### 8.3 Conditions, not capability

The thesis restated: the soundness of the evidence depends on sampling, anchoring, oversight, fairness controls and the place given to integrity signals, more than on the capability of the models.

### 8.4 Future work

## References

*Format: APA 7th edition. The verification level of each work (full text read, abstract or publisher page read, or record located) is recorded in Appendix A. Preprints and gray literature are identified as such. Primary works known only through a secondary source are cited in the text in the form "as cited in" and are not listed here; Appendix A, Section A.3, lists them.*

Adesope, O. O., Trevisan, D. A., & Sundararajan, N. (2017). Rethinking the use of tests: A meta-analysis of practice testing. *Review of Educational Research, 87*(3), 659–701. https://doi.org/10.3102/0034654316689306

Akimov, A., & Malin, M. (2020). When old becomes new: A case study of oral examination as an online assessment tool. *Assessment & Evaluation in Higher Education, 45*(8), 1205–1221. https://doi.org/10.1080/02602938.2020.1730301

Alessio, H. M., Malay, N. J., Maurer, K., Bailer, A. J., & Rubin, B. (2017). Examining the effect of proctoring on online test scores. *Online Learning, 21*(1). https://doi.org/10.24059/olj.v21i1.885

Barnett, S. M., & Ceci, S. J. (2002). When and where do we apply what we learn? A taxonomy for far transfer. *Psychological Bulletin, 128*(4), 612–637. https://doi.org/10.1037/0033-2909.128.4.612

Benson, H. H. (1995). The dissolution of the problem of the elenchus. In C. C. W. Taylor (Ed.), *Oxford studies in ancient philosophy* (Vol. 13, pp. 45–112). Oxford University Press. https://doi.org/10.1093/oso/9780198250005.003.0004

Benson, H. H. (2000). *Socratic wisdom: The model of knowledge in Plato's early dialogues*. Oxford University Press. https://doi.org/10.1093/oso/9780195129182.001.0001

Biggs, J. B., & Collis, K. F. (1982). *Evaluating the quality of learning: The SOLO taxonomy*. Academic Press.

Bisra, K., Liu, Q., Nesbit, J. C., Salimi, F., & Winne, P. H. (2018). Inducing self-explanation: A meta-analysis. *Educational Psychology Review, 30*(3), 703–725. https://doi.org/10.1007/s10648-018-9434-x

Bloom, B. S. (Ed.), Engelhart, M. D., Furst, E. J., Hill, W. H., & Krathwohl, D. R. (1956). *Taxonomy of educational objectives: The classification of educational goals. Handbook I: Cognitive domain*. David McKay.

Bond, C. F., Jr., & DePaulo, B. M. (2006). Accuracy of deception judgments. *Personality and Social Psychology Review, 10*(3), 214–234. https://doi.org/10.1207/s15327957pspr1003_2

Bransford, J. D., & Schwartz, D. L. (1999). Rethinking transfer: A simple proposal with multiple implications. *Review of Research in Education, 24*, 61–100. https://doi.org/10.3102/0091732X024001061

Buolamwini, J., & Gebru, T. (2018). Gender shades: Intersectional accuracy disparities in commercial gender classification. *Proceedings of the 1st Conference on Fairness, Accountability and Transparency, PMLR 81*, 77–91. https://proceedings.mlr.press/v81/buolamwini18a.html

Caffrey, E., Fuchs, D., & Fuchs, L. S. (2008). The predictive validity of dynamic assessment: A review. *The Journal of Special Education, 41*(4), 254–270. https://doi.org/10.1177/0022466907310366

Casabianca, J. M., McCaffrey, D. F., Johnson, M. S., Ricker-Pedley, K. L., Rotou, O., & Martineau, J. (2023). *Exploration of the proportional reduction in mean-squared error for evaluating automated scores* (ETS Research Memorandum RM-23-01). ETS. https://www.ets.org/Media/Research/pdf/RM-23-01.pdf

Chi, M. T. H., Bassok, M., Lewis, M. W., Reimann, P., & Glaser, R. (1989). Self-explanations: How students study and use examples in learning to solve problems. *Cognitive Science, 13*(2), 145–182. https://doi.org/10.1207/s15516709cog1302_1

Chi, M. T. H., de Leeuw, N., Chiu, M.-H., & LaVancher, C. (1994). Eliciting self-explanations improves understanding. *Cognitive Science, 18*(3), 439–477. https://doi.org/10.1207/s15516709cog1803_3

Chiang, C.-H., Chen, W.-C., Kuan, C.-Y., Yang, C., & Lee, H.-y. (2024). Large language model as an assignment evaluator: Insights, feedback, and challenges in a 1000+ student course. In *Proceedings of the 2024 Conference on Empirical Methods in Natural Language Processing* (pp. 2489–2513). Association for Computational Linguistics. https://doi.org/10.18653/v1/2024.emnlp-main.146

Chiang, C.-H., & Lee, H.-y. (2023). Can large language models be an alternative to human evaluations? In *Proceedings of the 61st Annual Meeting of the Association for Computational Linguistics (Volume 1: Long Papers)* (pp. 15607–15631). Association for Computational Linguistics. https://doi.org/10.18653/v1/2023.acl-long.870

Coghlan, S., Miller, T., & Paterson, J. (2021). Good proctor or "Big Brother"? Ethics of online exam supervision technologies. *Philosophy & Technology, 34*(4), 1581–1606. https://doi.org/10.1007/s13347-021-00476-1

Damphousse, K. R., Pointon, L., Upchurch, D., & Moore, R. K. (2007). *Assessing the validity of voice stress analysis tools in a jail setting* (NCJ 219031) [Report]. U.S. Department of Justice, National Institute of Justice. https://www.ojp.gov/pdffiles1/nij/grants/219031.pdf

De Beer, M. (2006). Dynamic testing: Practical solutions to some concerns. *SA Journal of Industrial Psychology, 32*(4), 8–14. https://doi.org/10.4102/sajip.v32i4.245

Dendir, S., & Maxwell, R. S. (2020). Cheating in online courses: Evidence from online proctoring. *Computers in Human Behavior Reports, 2*, 100033. https://doi.org/10.1016/j.chbr.2020.100033

DePaulo, B. M., Lindsay, J. J., Malone, B. E., Muhlenbruck, L., Charlton, K., & Cooper, H. (2003). Cues to deception. *Psychological Bulletin, 129*(1), 74–118. https://doi.org/10.1037/0033-2909.129.1.74

Dunlosky, J., Rawson, K. A., Marsh, E. J., Nathan, M. J., & Willingham, D. T. (2013). Improving students' learning with effective learning techniques: Promising directions from cognitive and educational psychology. *Psychological Science in the Public Interest, 14*(1), 4–58. https://doi.org/10.1177/1529100612453266

Ericsson, K. A., & Simon, H. A. (1980). Verbal reports as data. *Psychological Review, 87*(3), 215–251. https://doi.org/10.1037/0033-295X.87.3.215

Ericsson, K. A., & Simon, H. A. (1998). How to study thinking in everyday life: Contrasting think-aloud protocols with descriptions and explanations of thinking. *Mind, Culture, and Activity, 5*(3), 178–186. https://doi.org/10.1207/s15327884mca0503_3

European Commission. (2025). *Commission guidelines on prohibited artificial intelligence practices established by Regulation (EU) 2024/1689 (AI Act)* (C(2025) 5052 final, 29 July 2025).

Feuerstein, R., Falik, L. H., & Feuerstein, Ra. (n.d.). *The Learning Potential Assessment Device* [Book chapter manuscript; an earlier version appeared in E. Samuda (Ed.), *Advances in cross-cultural assessment*, Sage, 1998]. Feuerstein Institute. https://feuerstein-institute.org/wp-content/uploads/2023/05/Ch.-3.-LPAD.pdf

Fox, M. C., Ericsson, K. A., & Best, R. (2011). Do procedures for verbal reporting of thinking have to be reactive? A meta-analysis and recommendations for best reporting methods. *Psychological Bulletin, 137*(2), 316–344. https://doi.org/10.1037/a0021663

Fuchs, D., Compton, D. L., Fuchs, L. S., Bouton, B., & Caffrey, E. (2011). The construct and predictive validity of a dynamic assessment of young children learning to read: Implications for RTI frameworks. *Journal of Learning Disabilities, 44*(4), 339–347. https://doi.org/10.1177/0022219411407864

Giddens, C. L., Barron, K. W., Byrd-Craven, J., Clark, K. F., & Winter, A. S. (2013). Vocal indices of stress: A review. *Journal of Voice, 27*(3), 390.e21–390.e29. https://doi.org/10.1016/j.jvoice.2012.12.010

Ginsburg, H. P. (1997). *Entering the child's mind: The clinical interview in psychological research and practice*. Cambridge University Press. https://doi.org/10.1017/CBO9780511527777

Harnsberger, J. D., Hollien, H., Martin, C. A., & Hollien, K. A. (2009). Stress and deception in speech: Evaluating layered voice analysis. *Journal of Forensic Sciences, 54*(3), 642–650. https://doi.org/10.1111/j.1556-4029.2009.01026.x

Haywood, H. C., & Lidz, C. S. (2007). *Dynamic assessment in practice: Clinical and educational applications*. Cambridge University Press. https://doi.org/10.1017/CBO9780511607516

Hembree, R. (1988). Correlates, causes, effects, and treatment of test anxiety. *Review of Educational Research, 58*(1), 47–77. https://doi.org/10.3102/00346543058001047

Henkel, O., Hills, L., Boxer, A., Roberts, B., & Levonian, Z. (2024). Can large language models make the grade? An empirical study evaluating LLMs ability to mark short answer questions in K-12 education. In *Proceedings of the Eleventh ACM Conference on Learning @ Scale* (pp. 300–304). ACM. https://doi.org/10.1145/3657604.3664693

Henkel, O., Hills, L., Roberts, B., & McGrane, J. (2023). *Can LLMs grade short-answer reading comprehension questions: An empirical study with a novel dataset* (arXiv:2310.18373v2) [Preprint]. arXiv. https://doi.org/10.48550/arXiv.2310.18373

Horwitz, E. K., Horwitz, M. B., & Cope, J. (1986). Foreign language classroom anxiety. *The Modern Language Journal, 70*(2), 125–132. https://doi.org/10.1111/j.1540-4781.1986.tb05256.x

Huxham, M., Campbell, F., & Westwood, J. (2012). Oral versus written assessments: A test of student performance and attitudes. *Assessment & Evaluation in Higher Education, 37*(1), 125–136. https://doi.org/10.1080/02602938.2010.515012

Joughin, G. (2010). *A short guide to oral assessment*. Leeds Met Press in association with University of Wollongong. https://eprints.leedsbeckett.ac.uk/id/eprint/2804

Karpicke, J. D., & Aue, W. R. (2015). The testing effect is alive and well with complex materials. *Educational Psychology Review, 27*(2), 317–326. https://doi.org/10.1007/s10648-015-9309-3

Karpicke, J. D., & Blunt, J. R. (2011). Retrieval practice produces more learning than elaborative studying with concept mapping. *Science, 331*(6018), 772–775. https://doi.org/10.1126/science.1199327

Koenecke, A., Nam, A., Lake, E., Nudell, J., Quartey, M., Mengesha, Z., Toups, C., Rickford, J. R., Jurafsky, D., & Goel, S. (2020). Racial disparities in automated speech recognition. *Proceedings of the National Academy of Sciences, 117*(14), 7684–7689. https://doi.org/10.1073/pnas.1915768117

Kortemeyer, G. (2023). Toward AI grading of student problem solutions in introductory physics: A feasibility study. *Physical Review Physics Education Research, 19*(2), 020163. https://doi.org/10.1103/PhysRevPhysEducRes.19.020163

Krathwohl, D. R. (2002). A revision of Bloom's taxonomy: An overview. *Theory Into Practice, 41*(4), 212–218. https://doi.org/10.1207/s15430421tip4104_2

Lantolf, J. P., & Poehner, M. E. (2004). Dynamic assessment of L2 development: Bringing the past into the future. *Journal of Applied Linguistics, 1*(1), 49–72. https://doi.org/10.1558/japl.1.1.49.55872

Liang, W., Yuksekgonul, M., Mao, Y., Wu, E., & Zou, J. (2023). GPT detectors are biased against non-native English writers. *Patterns, 4*(7), 100779. https://doi.org/10.1016/j.patter.2023.100779

Meadows, M., & Billington, L. (2005). *A review of the literature on marking reliability* [Report commissioned by the National Assessment Agency]. AQA Centre for Education Research and Policy.

Memon, M. A., Joughin, G. R., & Memon, B. (2010). Oral assessment and postgraduate medical examinations: Establishing conditions for validity, reliability and fairness. *Advances in Health Sciences Education, 15*(2), 277–289. https://doi.org/10.1007/s10459-008-9111-9

Milani, A., Franzoni, V., Florindi, E., Omarbekova, A., Bekmanova, G., & Yergesh, B. (2025). When AI is fooled: Hidden risks in LLM-assisted grading. *Education Sciences, 15*(11), 1419. https://doi.org/10.3390/educsci15111419

Mizumoto, A., & Eguchi, M. (2023). Exploring the potential of using an AI language model for automated essay scoring. *Research Methods in Applied Linguistics, 2*(2), 100050. https://doi.org/10.1016/j.rmal.2023.100050

Mujtaba, D., Mahapatra, N., Arney, M., Yaruss, J. S., Gerlach-Houck, H., Herring, C., & Bin, J. (2024). Lost in transcription: Identifying and quantifying the accuracy biases of automatic speech recognition systems against disfluent speech. In *Proceedings of the 2024 Conference of the North American Chapter of the Association for Computational Linguistics: Human Language Technologies (Volume 1: Long Papers)* (pp. 4795–4809). Association for Computational Linguistics. https://doi.org/10.18653/v1/2024.naacl-long.269

National Research Council. (2003). *The polygraph and lie detection*. The National Academies Press. https://doi.org/10.17226/10420

Nisbett, R. E., & Wilson, T. D. (1977). Telling more than we can know: Verbal reports on mental processes. *Psychological Review, 84*(3), 231–259. https://doi.org/10.1037/0033-295X.84.3.231

Pack, A., Barrett, A., & Escalante, J. (2024). Large language models and automated essay scoring of English language learner writing: Insights into validity and reliability. *Computers and Education: Artificial Intelligence, 6*, 100234. https://doi.org/10.1016/j.caeai.2024.100234

Pan, S. C., & Rickard, T. C. (2018). Transfer of test-enhanced learning: Meta-analytic review and synthesis. *Psychological Bulletin, 144*(7), 710–756. https://doi.org/10.1037/bul0000151

Panickssery, A., Bowman, S. R., & Feng, S. (2024). *LLM evaluators recognize and favor their own generations* (arXiv:2404.13076) [Preprint]. arXiv. https://doi.org/10.48550/arXiv.2404.13076

Papoutsaki, A., Sangkloy, P., Laskey, J., Daskalova, N., Huang, J., & Hays, J. (2016). WebGazer: Scalable webcam eye tracking using user interactions. In *Proceedings of the Twenty-Fifth International Joint Conference on Artificial Intelligence (IJCAI-16)* (pp. 3839–3845). AAAI Press.

Piaget, J. (1929). *The child's conception of the world* (J. Tomlinson & A. Tomlinson, Trans.). Routledge & Kegan Paul.

Plato. (1966). *Apology* (H. N. Fowler, Trans.). In *Plato in twelve volumes* (Vol. 1). Harvard University Press; William Heinemann. Perseus Digital Library, Perseus:text:1999.01.0170.

Plato. (1967). *Meno* (W. R. M. Lamb, Trans.). In *Plato in twelve volumes* (Vol. 3). Harvard University Press; William Heinemann. Perseus Digital Library, Perseus:text:1999.01.0178.

Rayner, K. (2009). Eye movements in reading: Models and data. *Journal of Eye Movement Research, 2*(5), Article 2, 1–10. https://doi.org/10.16910/jemr.2.5.2

Regulation (EU) 2024/1689 of the European Parliament and of the Council of 13 June 2024 laying down harmonised rules on artificial intelligence (Artificial Intelligence Act). (2024). *Official Journal of the European Union*, L, 2024/1689. http://data.europa.eu/eli/reg/2024/1689/oj

Roediger, H. L., III, & Karpicke, J. D. (2006). Test-enhanced learning: Taking memory tests improves long-term retention. *Psychological Science, 17*(3), 249–255. https://doi.org/10.1111/j.1467-9280.2006.01693.x

Rowland, C. A. (2014). The effect of testing versus restudy on retention: A meta-analytic review of the testing effect. *Psychological Bulletin, 140*(6), 1432–1463. https://doi.org/10.1037/a0037559

Sarabi Asl, S., Rashtchi, M., & Rezaie, G. (2024). The effects of interactionist versus interventionist dynamic assessment models on Iranian EFL learners' speaking sub-skills: A mixed-method study. *Asian-Pacific Journal of Second and Foreign Language Education, 9*, Article 12. https://doi.org/10.1186/s40862-023-00237-x

Seeskin, K. (1987). *Dialogue and discovery: A study in Socratic method*. State University of New York Press. https://doi.org/10.1515/9781438419329

Semmelmann, K., & Weigelt, S. (2018). Online webcam-based eye tracking in cognitive science: A first look. *Behavior Research Methods, 50*(2), 451–465. https://doi.org/10.3758/s13428-017-0913-7

Shermis, M. D. (2014). State-of-the-art automated essay scoring: Competition, results, and future directions from a United States demonstration. *Assessing Writing, 20*, 53–76. https://doi.org/10.1016/j.asw.2013.04.001

Sotiriadou, P., Logan, D., Daly, A., & Guest, R. (2020). The role of authentic assessment to preserve academic integrity and promote skill development and employability. *Studies in Higher Education, 45*(11), 2132–2148. https://doi.org/10.1080/03075079.2019.1582015

Stad, F. E., Wiedl, K. H., Vogelaar, B., Bakker, M., & Resing, W. C. M. (2019). The role of cognitive flexibility in young children's potential for learning under dynamic testing conditions. *European Journal of Psychology of Education, 34*(1), 123–146. https://doi.org/10.1007/s10212-018-0379-8

van der Vleuten, C. P. M., Schuwirth, L. W. T., Scheele, F., Driessen, E. W., & Hodges, B. (2010). The assessment of professional competence: Building blocks for theory development. *Best Practice & Research Clinical Obstetrics and Gynaecology, 24*(6), 703–719. https://doi.org/10.1016/j.bpobgyn.2010.04.001

van Gog, T., & Sweller, J. (2015). Not new, but nearly forgotten: The testing effect decreases or even disappears as the complexity of learning materials increases. *Educational Psychology Review, 27*(2), 247–264. https://doi.org/10.1007/s10648-015-9310-x

Verga, P., Hofstatter, S., Althammer, S., Su, Y., Piktus, A., Arkhangorodsky, A., Xu, M., White, N., & Lewis, P. (2024). *Replacing judges with juries: Evaluating LLM generations with a panel of diverse models* (arXiv:2404.18796) [Preprint]. arXiv. https://doi.org/10.48550/arXiv.2404.18796

von der Embse, N., Jester, D., Roy, D., & Post, J. (2018). Test anxiety effects, predictors, and correlates: A 30-year meta-analytic review. *Journal of Affective Disorders, 227*, 483–493. https://doi.org/10.1016/j.jad.2017.11.048

Vygotsky, L. S. (1978). *Mind in society: The development of higher psychological processes* (M. Cole, V. John-Steiner, S. Scribner, & E. Souberman, Eds.). Harvard University Press.

Wang, P., Li, L., Chen, L., Cai, Z., Zhu, D., Lin, B., Cao, Y., Kong, L., Liu, Q., Liu, T., & Sui, Z. (2024). Large language models are not fair evaluators. In *Proceedings of the 62nd Annual Meeting of the Association for Computational Linguistics (Volume 1: Long Papers)* (pp. 9440–9450). Association for Computational Linguistics. https://doi.org/10.18653/v1/2024.acl-long.511

Wass, V., Wakeford, R., Neighbour, R., & Van der Vleuten, C. (2003). Achieving acceptable reliability in oral examinations: An analysis of the Royal College of General Practitioners membership examination's oral component. *Medical Education, 37*(2), 126–131. https://doi.org/10.1046/j.1365-2923.2003.01417.x

Weber-Wulff, D., Anohina-Naumeca, A., Bjelobaba, S., Foltýnek, T., Guerrero-Dib, J., Popoola, O., Šigut, P., & Waddington, L. (2023). Testing of detection tools for AI-generated text. *International Journal for Educational Integrity, 19*, 26. https://doi.org/10.1007/s40979-023-00146-z

Woldeab, D., & Brothen, T. (2019). 21st century assessment: Online proctoring, test anxiety, and student performance. *International Journal of E-Learning & Distance Education, 34*(1). https://www.ijede.ca/index.php/jde/article/view/1106

Wolfsdorf, D. (2013). Socratic philosophizing. In J. Bussanich & N. D. Smith (Eds.), *The Bloomsbury companion to Socrates* (pp. 34ff.). Bloomsbury.

Yoder-Himes, D. R., Asif, A., Kinney, K., Brandt, T. J., Cecil, R. E., Himes, P. R., Cashon, C., Hopp, R. M. P., & Ross, E. (2022). Racial, skin tone, and sex disparities in automated proctoring software. *Frontiers in Education, 7*, 881449. https://doi.org/10.3389/feduc.2022.881449

Zheng, L., Chiang, W.-L., Sheng, Y., Zhuang, S., Wu, Z., Zhuang, Y., Lin, Z., Li, Z., Li, D., Xing, E. P., Zhang, H., Gonzalez, J. E., & Stoica, I. (2023). Judging LLM-as-a-judge with MT-Bench and Chatbot Arena. In *Advances in Neural Information Processing Systems 36 (Datasets and Benchmarks Track)*. https://doi.org/10.48550/arXiv.2306.05685

## Appendix A — Source Verification Ledger

This manuscript follows a strict rule: no source is cited as if it had been checked unless it was. This appendix records the level at which each work in the reference list was verified, so that a reader or examiner can see how much weight each citation bears. Every work was located in the research pass of 21 November 2026 (Gadfly ticket GAD-T254). The research notes, with the full reference, the page opened, the verified quotation or paraphrase, and a confidence rating for each source, are kept with the project in `docs/research/`, and their consolidated ledger is `docs/research/references/VERIFICATION.md`.

**Levels.**

- **Full text read.** The work itself (or, where noted in brackets, the relevant chapters or sections) was opened and the cited content was checked against it. Books whose cited passages were located by searching the work's own text, with the rest unread, would be listed separately in A.1; there are none at present.
- **Abstract or publisher page read.** The abstract, publisher landing page or index record was read; the body was not. Claims attributed to these works are confined to what such pages establish.
- **Record located.** The bibliographic record was confirmed but the content was not read. Such works are cited only for central, widely documented claims and are flagged for a library check.

### A.1 Full text read (in whole or in the relevant parts)

Bisra et al. (2018) [abstract, introduction, moderator table, discussion]; Bloom et al. (1956) [OCR of the 1956 edition: introduction to the six classes, Comprehension on extrapolation, Application on writing test items]; Bransford & Schwartz (1999) [framing sections, pp. 8–9 of the laboratory copy, and the closing section]; Casabianca et al. (2023) [Introduction and Discussion]; Chi et al. (1994) [abstract, pp. 452–454]; Coghlan et al. (2021) [open-access copy, the argument sections]; Damphousse et al. (2007) [abstract and executive summary, pp. i–iii]; De Beer (2006); Dunlosky et al. (2013) [abstract, Sections 1, 2.5 and 8.5]; Ericsson & Simon (1998) [pp. 178–184]; European Commission (2025) [Section 7 on Article 5(1)(f), paras. 247–263, and para. 5 on status]; Feuerstein et al. (n.d.) [opening section, the structure of the instruments, the passages on mediation and graduated prompting]; Fuchs et al. (2011) [author manuscript: introduction, measure, discussion]; Joughin (2010) [pp. 7, 10–12, 14–16]; Karpicke & Blunt (2011) [abstract, Experiment 1, Fig. 1]; Krathwohl (2002); Meadows & Billington (2005) [title page, pp. 18 and 41]; Milani et al. (2025) [repository copy: abstract, Sections 3 and 4.1]; National Research Council (2003) [Chapter 6, the subsection on voice stress analysis]; Pan & Rickard (2018) [abstract, coding, the three-factor framework]; Papoutsaki et al. (2016) [author copy: abstract, evaluation]; Piaget (1929) [Introduction, Sections 1–2, pp. 2–14]; Plato (1966) [Stephanus 21b–23a and 30e–31a, Perseus]; Plato (1967) [82b, 82e and 84a–85d, Perseus]; Rayner (2009) [benchmark data, pp. 2–3]; Regulation (EU) 2024/1689 [EUR-Lex: Recitals 18, 44 and 56; Arts. 3(34), 3(39), 5(1)(f), 6(3), 14 and 113; Annex III, point 3]; Roediger & Karpicke (2006) [abstract, Experiment 1, Experiment 2 design]; Rowland (2014) [abstract, method, overall effect, moderator table]; Sarabi Asl et al. (2024) [introduction and models section, pp. 2–5]; Shermis (2014) [abstract, data, agreement results, limitations]; Stad et al. (2019) [abstract, introduction, procedure, scoring]; van der Vleuten et al. (2010) [the sections on specificity, objectivity and sampling]; van Gog & Sweller (2015) [abstract and introduction]; Verga et al. (2024) [arXiv version 2: Sections 2.2, 3.1, 4.1–4.4, Tables 1–2]; Vygotsky (1978) [Chapter 6, pp. 85–87, community scan]; Wang et al. (2024) [arXiv version: Sections 1–3]; Wolfsdorf (2013) [introduction, the sections on Vlastos and Benson, conclusion; proof copy]; Yoder-Himes et al. (2022) [abstract, methods, results, limitations]; Zheng et al. (2023) [arXiv version 4: Sections 3.3, 3.4, 4.2].

### A.2 Abstract or publisher page read

Adesope et al. (2017); Akimov & Malin (2020); Alessio et al. (2017); Barnett & Ceci (2002); Benson (1995) [with Wolfsdorf's (2013) summary of the argument]; Benson (2000); Bond & DePaulo (2006); Buolamwini & Gebru (2018); Caffrey et al. (2008); Chi et al. (1989) [abstract on p. 145, from the page image]; Chiang & Lee (2023); Chiang et al. (2024); Dendir & Maxwell (2020); DePaulo et al. (2003); Ericsson & Simon (1980); Fox et al. (2011); Giddens et al. (2013); Ginsburg (1997); Harnsberger et al. (2009); Haywood & Lidz (2007) [description and Chapter 1 preview]; Hembree (1988); Henkel et al. (2023) [preprint]; Henkel et al. (2024); Horwitz et al. (1986); Huxham et al. (2012); Karpicke & Aue (2015); Koenecke et al. (2020); Kortemeyer (2023) [APS abstract and arXiv abstract; the quoted sentence is from the arXiv abstract]; Lantolf & Poehner (2004); Liang et al. (2023) [abstract, with the PMC summary and results passage]; Memon et al. (2010); Mizumoto & Eguchi (2023); Mujtaba et al. (2024); Nisbett & Wilson (1977); Pack et al. (2024); Panickssery et al. (2024) [preprint; abstract and Fig. 1]; Seeskin (1987); Semmelmann & Weigelt (2018); Sotiriadou et al. (2020); von der Embse et al. (2018); Wass et al. (2003); Weber-Wulff et al. (2023); Woldeab & Brothen (2019).

### A.3 Record located

**Cited directly at this level.** Biggs & Collis (1982). The book was not opened. Section 2.7 uses only the commonly described distinction between answers of one point, several unconnected points, integrated points and generalized points, and attributes no wording to the book.

**Named in the text only through a secondary source that was read.** These primary works were located but not opened. Each appears in the text in the form "as cited in" and is not in the reference list:

- Vlastos, G. (1983). The Socratic elenchus. *Oxford Studies in Ancient Philosophy, 1*, 27–58. Quoted as cited in Wolfsdorf (2013), with Vlastos's page numbers.
- Poehner, M. E. (2008). *Dynamic assessment: A Vygotskian approach to understanding and promoting L2 development*. Springer. Quoted as cited in Sarabi Asl et al. (2024).
- Williamson, D. M., Xi, X., & Breyer, F. J. (2012). A framework for evaluation and use of automated scoring. *Educational Measurement: Issues and Practice, 31*(1), 2–13. Thresholds as reported by Casabianca et al. (2023).
- Grigorenko, E. L., & Sternberg, R. J. (1998). Dynamic testing. *Psychological Bulletin, 124*(1), 75–111. Named inside a quotation from De Beer (2006).
- Joughin, G. (1998). Dimensions of oral assessment. *Assessment & Evaluation in Higher Education, 23*(4), 367–378. The six dimensions are cited from Joughin's own 2010 restatement.

### A.4 Corrections made to the working draft

Numbered, cumulative and never renumbered. Items 1 to 28 were found by the research pass (GAD-T254) in popular retellings of the literature or in the project's own documentation, and the dissertation was drafted to avoid them; items from 29 were found while drafting.

1. **The elenchus shows inconsistency, not falsity.** A contradiction probe tests whether a student's claims fit together; it cannot decide which is wrong (Section 2.2).
2. **There is no single "Socratic method."** Vlastos's standard elenchus fits only part of the dialogues (Wolfsdorf, 2013).
3. **The *Meno* slave-boy passage is not a model of non-leading questions.** Many questions at 84d–85b lead; Piaget's rule against suggestion is the better model (Sections 2.2, 2.3).
4. **"Thinking aloud changes reasoning" is overstated.** Plain think-aloud did not change accuracy; explaining to a listener did, and every form of verbalizing slowed people down (Fox et al., 2011).
5. **Nisbett and Wilson (1977) did not show verbal reports are worthless.** Reports can be accurate when the cause is salient and plausible.
6. **"Someone relaying another source's answers tends to contradict themselves"** has no support in the sources reviewed. It is treated as a hypothesis (Sections 1.6, 2.12).
7. **"Structure makes oral exams reliable" is overstated.** Reliability comes mostly from sampling; in Wass et al. (2003) content accounted for more score variance than examiners.
8. **The accent-credibility effect is not settled.** Replications of the finding usually cited for it are mixed; the dissertation does not rely on it.
9. **Huxham et al. (2012) does not show that anxiety has no effect on oral scores.**
10. **"Test anxiety causes poor performance" (Hembree, 1988)** is a causal reading of mostly correlational data; the stronger evidence is that scores rise when anxiety is treated.
11. **Roediger and Karpicke (2006) shows retention, not understanding.** Restudying led at five minutes, and the final test was free recall of the same passage.
12. **The testing effect can vanish** without feedback when practice-test success is 50% or lower (Rowland, 2014).
13. **Testing benefits do not spread to untested material** (Pan & Rickard, 2018).
14. **Chi et al. (1989) is correlational,** and answering an examiner is explaining to another, not self-explanation in the strict sense.
15. **Bloom's taxonomy is not a strict ladder,** and a "what would change if" question inside the student's own scenario is extrapolation, not application.
16. **The ZPD passage says "more capable peers."** "More knowledgeable other" is not in it, and the term "scaffolding" is not Vygotsky's. The 1978 text is an edited translation.
17. **"Dynamic assessment predicts better than static tests" is overstated** (Caffrey et al., 2008; Fuchs et al., 2011).
18. **Prompt counts and gain scores are not "learning potential"** when students start at different levels (De Beer, 2006).
19. **Zheng et al. (2023) does not show self-enhancement bias,** and its headline agreement figure is for pairwise preferences with ties excluded.
20. **"Machines grade essays as well as humans" needs its context:** human–human agreement was only 0.61–0.85, and construct validity and gaming were left open (Shermis, 2014).
21. **The .70 / .10 / .15 thresholds apply to a set of answers, not to one answer** (Williamson et al., 2012, as cited in Casabianca et al., 2023).
22. **Liang et al. (2023) is about detectors, not graders.**
23. **Gender Shades measured gender classification, not face detection.** The direct proctoring evidence is Yoder-Himes et al. (2022).
24. **The webcam tracker's error of about four degrees** was measured against a commercial tracker with four participants; it does not mean webcam tracking matches a commercial eye tracker.
25. **Jitter tends to fall, not rise, under stress** where any effect is found, and findings are mixed (Giddens et al., 2013).
26. **The voice-stress "15%" in the NIJ field study** is the average share of lies detected, not overall accuracy (Damphousse et al., 2007).
27. **Dendir and Maxwell (2020) inferred cheating from a score drop;** they did not observe it.
28. **The project's privacy documentation says "Nothing infers emotion."** The Commission's guidelines give detecting "emotional arousal and anxiousness" during examinations as an example of prohibited emotion recognition in education (Section 2.11).
29. **Memon et al. (2010) does not support a cost argument.** The first draft of Section 1.1 said institutions abandoned oral examinations partly because of their cost. The abstract, the level at which the work was read, says only that some countries moved away from oral examinations in postgraduate medical certification because of continuing concerns about reliability and fairness. The cost point now rests on the testing time estimated by van der Vleuten et al. (2010).

### A.5 Checks closed

- **Citation details corrected by the research pass** and used in the reference list: Woldeab and Brothen (2019) is in the *International Journal of E-Learning & Distance Education*; Bond and DePaulo (2006) is in *Personality and Social Psychology Review*; Memon et al. is dated 2010; the Sotiriadou et al. (2020) DOI resolves to the title given in the reference list.
- **AI Act Annex III, point 3,** was read on EUR-Lex, closing an earlier reading from an unofficial mirror.

### A.6 First drafting pass (GAD-T256, 21 November 2026)

Drafted the abstract, Chapters 1 and 2 and the reference list, and outlined Chapters 3 to 8. Every work cited in Chapters 1 and 2 is listed above at its recorded level, and every quotation was taken from a passage recorded as seen in the research notes. Item 29 was found and corrected.

**Open items.**

- **Read the primary works now cited only through secondary sources** (A.3), and Biggs and Collis (1982), before quoting any of them directly.
- **Check figures blocked by paywalls:** the effect sizes in Adesope et al. (2017) and Caffrey et al. (2008); the nine dimensions in Barnett and Ceci (2002); the agreement statistics in Mizumoto and Eguchi (2023) and Pack et al. (2024); the marks and anxiety measures in Huxham et al. (2012).
- **Read in full the works Chapter 2 relies on most heavily at the abstract level:** Wass et al. (2003), Koenecke et al. (2020), Huxham et al. (2012) and Fox et al. (2011).
- **Plato translations:** the Perseus translations (Fowler, Lamb) were used; their copyright status for long quotation was not checked.
- **AI Act dates:** confirm the application dates for the high-risk rules (Annex III) after any amendments of 2025–2026.
- **Bibliographic details:** the end page of Wolfsdorf (2013) and the book's catalogue year (2012 or 2013); the date and publication details of Feuerstein et al. (n.d.); the volume number of Benson (1995), taken from search listings.
- **Sources still to find for Chapter 3:** the measurement-theory literature (item response theory, adaptive testing, the design of experiments) on which the formal model will draw.
