# Dissertation insert: Socratic examination across languages, and grading it fairly

*Prepared 2026-11-22 in the Gadfly repository (GAD-T550, part of GAD-T526 "Korean exams") for later merging into
Dissertation.ElenchusAtScale, which this session could read but not edit. It describes the method as built in Gadfly at commit
`e0ae88c` and the parts still being built (marked), and it complements the GAD-T553 insert
(`dissertation-language-section.md`), which reviews the literature on second-language speakers and cross-lingual judging
(new Sections 2.5.3 and 2.5.4) and adds Section 5.3.5 on language background. This insert adds no new sources: every
citation below is one that insert already introduces and records in Appendix A, cited at the level recorded there ("abstract"
where only the abstract was read). The as-built engineering is in Gadfly's `docs/LOCALIZATION.md`. The text follows the
manuscript's conventions: APA 7 author-date citations, plain declarative prose, and section numbers as the manuscript stood
on 22 November 2026.*

## Where it goes

| Part | Insert into | Effect on numbering |
|---|---|---|
| **A.** New Section 4.12 "Examining across languages" | Chapter 4 (`chapters/04-gadfly-method.md`), after Section 4.11 (Requirements table) | None. If the editor prefers the requirements table to close the chapter, insert as 4.11 and renumber the table 4.12. |
| **B.** Four new rows in the requirements table, L1 to L4 | Section 4.11 | Adds rows; no renumbering. |
| **C.** New Section 5.4.4 "Fairness across languages" | Chapter 5 (`chapters/05-validity-fairness-integrity.md`), after Section 5.4.3 (Monitoring score gaps) | None. |
| **D.** Amendment to Section 5.4.3 | Chapter 5 | Replaces the paragraph's last two sentences. |
| **E.** Additions to Sections 7.3 (Measures) and 7.4 (Thresholds and decision rules) | Chapter 7 | None. Sits beside the GAD-T553 insert's Chapter 7 additions. |

Merge order: after the GAD-T553 insert, since Sections A and C refer to its Sections 2.5.3, 2.5.4 and 5.3.5. If that insert
is not merged, replace those cross-references with "Gadfly's research note on second-language speakers".

---

## A. Section 4.12 Examining across languages

A Socratic examination is a conversation, and a student who must hold it in a language they do not command is examined on
two things at once. Gadfly therefore lets an examination be taken in a language other than the one its material is written
in, Korean first. The design problem is to change the language of the conversation without changing the examination: the
same objectives, the same standard of evidence and the same rubric must apply whichever language the student speaks.

**What is translated, and what is not.** The source material, the concept map, the rubric and the grading policy are never
translated. They are the standard against which every answer is judged, and a translated rubric would make each language a
different examination with its own, unvalidated standard. What changes with the language is what the student reads and
hears: the examination's title, instructions and objective titles; the examiner's questions; the examiner's fixed lines
(acknowledgements and the brief sounds that fill a pause, each reviewed to be non-evaluative in the new language); the
student interface; and the recognizer's and the voice's language.

**The language is decided, recorded and fixed.** An examination lists the languages it is offered in. The student chooses one
before beginning, and it is recorded on the attempt and fixed for it: an attempt cannot resume in another language, and
everything that depends on language reads the attempt's record, never the browser, the interface setting or anything
inferred from the student's voice. Separately, the student may state their primary language; it too is recorded on the
attempt, so that a later change to the student's profile never changes how a finished attempt was conducted. An English
examination taken by a student whose primary language is English, or who stated none, runs exactly as before the feature
existed, down to the hash of its prompts.

**The instructor approves what the student reads.** The localized texts begin as a machine translation of the English,
which the system labels as a draft. An instructor reads each text beside its English, corrects it and approves it. Until
then a student who chooses that language sees the draft with a statement that it was machine-translated and not yet checked.
This is human oversight at the point where a mistranslation would change the question a student believes they are
answering, and it is recorded: every draft, edit and approval is kept with its before and after texts.

**The examiner speaks the examination language over the original material** *(being built, GAD-T535)*. The examiner's
questions, its check that no question gives away its answer, and its quick reading of each answer are written in the
examination language, while the examiner reads the source material and rubric as written. Each language is a separate,
versioned prompt set built by adding fixed rules to the English one, never by rewording it, so the session record names
exactly the wording that examined the student. The cues the examiner reads from an answer, such as hedging, vagueness or an
admission of not knowing, have their own rules in Korean, and those rules fire only on Korean script, so that English
technical terms inside a Korean answer, which are common, are still read by the English rules *(GAD-T537)*.

**The judges read the answer as given** *(being built, GAD-T536)*. The panel judges a Korean answer directly against the
English rubric; it does not judge a machine translation of the answer, which would add a second, unmeasured source of error
between the student's words and the score. This is the configuration in which an evaluator reading target-language answers
against English references agreed best with human judges (Doddapaneni et al., 2025, abstract). Each judge quotes the
student's own words and glosses them in English, so a human grader can check the reading. The judges are told the
examination's language and nothing about the student's: the evidence reviewed in Section 2.5.3 suggests that labelling a
speaker as non-native invites the bias it means to prevent.

**The human grader reads the original, with a translation beside it.** For the human grader the original words are the
record. The review page, the grading page and the blind second marker's sheet show them as given and offer, on request, a
machine translation into English, placed under each original and labelled as a machine translation that is not the record.
The translation is never stored with the attempt or exported with it. Graders are not shown the student's primary language,
because it is not evidence of understanding and could prime the grade. The translation is an aid to reading, not an input to
the grade, and a grader whose decision turns on it is told to consult someone who reads the language; the method therefore
assumes, for summative use, that a grader or a second marker reads the examination language.

## B. Rows for the requirements table (Section 4.11)

| Req. | Requirement | Evidence | Gadfly as built (commit `e0ae88c`) | Status | Proposed |
|---|---|---|---|---|---|
| L1 | One standard across languages: material, rubric and policy untranslated | Section 4.12 | Only titles, instructions, objective titles, questions and interface change with the language | Met | — |
| L2 | The language of an attempt recorded, fixed and never inferred | Section 4.12; Section 5.3.5 | Attempt language and stated primary language copied to the attempt at its start; English-English attempts unchanged | Met | — |
| L3 | Student-facing translations approved by a person before they are presented as the instructor's | Section 4.12 | Machine draft, side-by-side review and approval, audited; unapproved drafts shown with a notice | Met | — |
| L4 | Judges score the answer as given against the original rubric; graders read the original | Doddapaneni et al. (2025, abstract); Section 4.12 | Graders: original as record, labelled translation on request, primary language hidden. Panel: in progress | Partly | GAD-T535, GAD-T536 |

## C. Section 5.4.4 Fairness across languages

Examining in several languages adds a fairness question of its own: whether the same understanding earns the same level in
each. Section 2.5.4 gives two reasons to expect that it may not. Language-model evaluators have inflated scores in
lower-resource and non-Latin-script languages (Hada et al., 2024, abstract), and their consistency across languages is low,
improved by an ensemble but not by scale (Fu & Liu, 2025, abstract). Gadfly's panel is such an ensemble, but the improvement
reported is relative, and an ensemble's agreement with itself says nothing about its agreement with human markers in the new
language.

Gadfly does not assume that the panel is fair across languages; it measures it in three places, by two groupings. The groupings
are the language the attempt was taken in and the primary language the student stated. They answer different questions. The
first asks whether a Korean attempt is graded like an English one; the second asks whether a student examined in their second
language is graded like one examined in their first, whichever language that is, which is the question for the many students
who will take an English examination in a language they learned.

The first place is the comparison of the panel's levels with the human examiner's levels by group, which Section 5.4.3
identified as the one fairness check that bears directly on the grade. A group whose panel levels fall below its human levels
is being marked down by the panel, not for its understanding. The second is the path: how often the examiner pressed harder,
read an answer as hedging or vague, or closed an answer on a silence, by group, since these cues decide which questions a
student is asked. The third is calibration. The panel is accepted for an examination only on a double-marked set
(Section 4.9), and the systematic difference between the panel and the two markers can now be read by language as well as
overall, so that an acceptance carried by the English answers does not hide a Korean group the panel marks down.

Each comparison keeps the rules every fairness view in Gadfly follows. Only owners and institution administrators see them,
since they never grade blind; a group of fewer than five students is shown by name and size only; and a total is withheld
whenever a group is, so that it cannot be subtracted back out. The stated primary language is used for these aggregates and
for adapting the examination, and for nothing else: it is never shown beside an answer, never given to a judge and never
inferred. A gap by language, finally, is not a finding. It may come from the recognizer, the examiner's cues, the judges, the
human graders, or from real differences in what the groups have learned; the comparison of the two graders' levels is what
separates the last from the rest, and it is why that comparison is the primary measure.

## D. Amendment to Section 5.4.3

Replace the paragraph's last two sentences ("The comparison of the two graders' scores by group can be made from data the
build already keeps, and it is the one fairness check that bears directly on the grade. Chapter 7 includes it.") with:

"The comparison of the two graders' levels by group is the one fairness check that bears directly on the grade. The build
now makes it by accommodation, by examination language and by the primary language the student stated, and the calibration
of the panel can be read by the same groups (Section 5.4.4). Chapter 7 makes it a measure."

## E. Chapter 7 additions

**Section 7.3, add:** "By examination language and by stated primary language: the panel's agreement with the human
markers (quadratic-weighted kappa) and its standardized mean difference from them, computed within each group on the
double-marked set; the difference between panel and human levels by group; and, for the localized texts, the share of
machine drafts changed by the instructor before approval, as a measure of how far the machine draft can be trusted."

**Section 7.4, add:** "The method is fit to inform a grade in a language only if the panel meets the agreement thresholds
within that language's group, not only overall, on at least as many double-marked answers as the thresholds require, marked by
graders who read the language. A group whose panel-minus-human difference exceeds the standardized mean difference threshold
is a reason to withhold the panel's weight for that language until it is explained."

## Appendix A and references

No new entries. Hada et al. (2024), Fu and Liu (2025) and Doddapaneni et al. (2025) are entered, with their verification
levels, by the GAD-T553 insert; this insert cites them at the same level.
