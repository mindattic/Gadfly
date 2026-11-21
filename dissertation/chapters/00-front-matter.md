# Abstract

*Draft, 21 November 2026. To be revised when Chapters 3 to 8 are drafted.*

Capable language models have broken the assumption on which the written examination rested: that the words submitted were produced by the person whose name is on them. Watching students more closely is a weak remedy, because proctoring, gaze tracking, voice analysis and detectors of machine-written text either cannot identify an individual who cheated or misfire on identifiable groups of students. The older remedy is to change the assessment: to question the student aloud and follow up on what they say. Oral examination resists outsourced answers but does not scale. This dissertation asks whether a synthetic examiner, a system of language models that questions a student aloud in the Socratic manner, writes each follow-up from the student's own previous answer and judges the answers against the instructor's material, can conduct such examinations at scale, and under what conditions the evidence it gathers is fit to inform a grade. Using Gadfly, a working system, as its case, it argues that a Socratic examination is a procedure for gathering evidence about understanding, and that the soundness of that evidence depends less on the capability of the examiner's models than on conditions that the literature makes explicit: wide sampling of objectives; scoring anchored to fixed rubrics rather than to the path the conversation took; controls for anxiety, accent, disfluency and transcription error; a panel of judges from different model families combined with blind human grading and reconciliation; and integrity signals kept as context, never as verdicts. The dissertation reviews six literatures against a recorded verification ledger, states a formal model of examination as sequential evidence-gathering by questioning, states the method as requirements an implementation can be checked against, and designs the empirical test that would show whether a system meets them. It reports no empirical evaluation, and it records that no study yet tests language models grading spoken Socratic answers.

**Keywords:** Socratic method; elenchus; oral assessment; viva; large language models; LLM-as-a-judge; automated scoring; dynamic assessment; retrieval practice; academic integrity; remote proctoring; speech recognition fairness; AI Act

# Statement on Authorship and Method

This dissertation is being written in collaboration between its author and a large language model, and its research notes were prepared by language-model agents working under the project's verification rule. The thesis, the decision to pursue it, and the final judgment on every claim belong to the author. Section 1.7 describes the method in detail.

The verification rule is that no source is cited as checked unless it was. Every work in the reference list was located in a research pass on 21 November 2026 and is recorded in Appendix A at the level at which it was read: full text read, abstract or publisher page read, or record located. Claims attributed to works read only at the abstract level are confined to what the abstract establishes. Primary works that were located but not opened are cited only through a secondary source that was read, in the form "as cited in", and Appendix A lists them. Quotations appear only where the exact text was seen. The verification pass also found 28 claims, in popular retellings of the literature or in the project's own documentation, that were wrong or overstated; Appendix A, Section A.4, lists them.

# Abbreviations

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
