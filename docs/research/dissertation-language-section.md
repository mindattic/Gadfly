# Dissertation insert: examining in a second language, and in the student's first

*Prepared 2026-11-22 in the Gadfly repository (GAD-T553) for later merging into Dissertation.ElenchusAtScale, which this
session could read but not edit. The evidence behind it, with each source's verification level, is in
`docs/research/esl-and-korean.md`. The text follows the manuscript's conventions: APA 7 author-date citations, claims
confined to the level at which each work was read, "abstract" marking quotations taken from an abstract, and Appendix A
as the ledger of verification levels. Section numbers assume the manuscript as it stood on 22 November 2026; if the
attention insert (`dissertation-attention-chapter.md`, GAD-T513) is merged first, nothing here is renumbered except the
Appendix A section, which becomes A.11.*

## Where it goes

| Part | Insert into | Effect on numbering |
|---|---|---|
| **A.** New Sections 2.5.3 "Speaking in a second language" and 2.5.4 "Examining in the student's first language" | Chapter 2 (`chapters/02-literature.md`), after Section 2.5.2 (Speech recognition) | None; Section 2.5's title becomes "Construct-irrelevant variance: anxiety, accent, language and speech recognition". |
| **B.** New finding in Section 2.12 (or 2.13 after the attention insert), "Synthesis and gaps" | Chapter 2 | Adds one numbered finding and one gap bullet. |
| **C.** New Section 5.3.5 "Language background" | Chapter 5, after Section 5.3.4 (Time and anxiety) | None. |
| **D.** Amendment to Section 6.4, "Populations the evidence does not cover" | Chapter 6 | None. |
| **E.** Additions to Sections 7.3 (Measures) and 7.4 (Thresholds and decision rules) | Chapter 7 | None. |
| **F.** Appendix A: corrections and a new section of ledger entries | `chapters/90-appendix-verification-ledger.md` | Adds A.10 (A.11 if the attention insert is merged first). |
| **G.** Reference list entries | `chapters/80-references.md` | Alphabetical insertion. |

The Section 2.1 roadmap sentence that introduces Section 2.5 should gain: "including what is known about students who
are examined in a second language, and about examining in a language other than English."

---

## A. Chapter 2 additions

### 2.5.3 Speaking in a second language

Section 2.5.2 showed that recognizers make more errors for some groups of speakers. Students who speak English as a
second language are the largest such group in most universities that teach in English, and the evidence that they are
served worse is now consistent. Whisper recognizes native accents more accurately than non-native ones, with word error
related to the speaker's first language and proficiency, and recognizes read speech better than conversation (Graham &
Roll, 2024, abstract). A larger evaluation of ten models from three families on more than 300 varieties of English found,
in its authors' words, that "as models increase in size and L1 English performance, the relative disparity for L2
speakers worsens" (Roll & Graham, 2025, p. 133); speakers of tonal first languages had about twice the word error rate of
speakers of other second-language backgrounds and nearly three times that of native speakers. Progress in recognition
does not, then, close the gap by itself, and a system cannot assume that its recognizer is fair because it is accurate.

The most relevant data for a spoken examination are spontaneous. In a corpus of 58 Korean learners of English speaking in
tutoring sessions, more than half of a general recognizer's errors were tied to features of spontaneous second-language
speech, such as filler words, self-repairs and ungrammatical expressions, and fine-tuning on that speech reduced the word
error rate by 44.2% (Kim et al., 2024; record only, see Appendix A). Fine-tuning is not free of risk: one study found
that naive fine-tuning of Whisper on learner speech lowered the average error but widened the gap for weaker learners
(record only, see Appendix A). For a system that uses a hosted recognizer, the levers are narrower: fixing the language
of recognition, and biasing the recognizer toward the words the examination expects, which providers now offer as
"keyterm" prompting.

Second-language speakers also pause differently. Korean learners of English differed from native speakers in speed,
length of run and silent pauses, and above all in "the frequency of pauses within clauses," which stimulated recall tied
to the work of formulating speech in the second language (Kahng, 2014, abstract). A within-speaker comparison found that
first- and second-language speech pause alike at clause boundaries, but the second-language speech pauses more often
inside clauses, and that the mid-clause pause rate was the one measure that consistently separated the two in interviews
(Huensch, 2023, abstract). The turn-taking norms of the first language are not the
explanation: across ten languages, Korean among them, conversations avoid overlap and minimize silence, and the languages'
mean gaps differ by no more than about 250 milliseconds (Stivers et al., 2009, abstract). A second-language speaker's
long pause is planning, not a yielded turn. An examiner that ends an answer after a fixed silence will therefore cut off
second-language speakers mid-thought more often than native speakers, which Section 2.5.1 already identified as time
pressure falling on the wrong students.

How the examiner speaks matters too. The classic experimental finding is that "comprehension was best assisted when the
content of the directions was repeated and rephrased in interaction," while reducing the linguistic complexity of input
in advance was "not a significant factor" (Pica et al., 1987, abstract). Speech rate matters in one direction: fast speech
reduced lower-intermediate learners' comprehension, but much slower speech added little (Griffiths, 1992; record only).
In written tests, linguistically simplified mathematics items gave second-language students small gains without changing
the mathematics (Abedi & Lord, 2001; record only), and a meta-analysis of test accommodations for English learners found a
negligible average effect, with only dictionaries and glossaries clearly helping (Kieffer et al., 2009; record only). The
lesson for a synthetic examiner is that plain wording is cheap and harmless but small in effect, and that the larger aid
is interactional: the student must be able to have a question repeated and rephrased without cost.

The last route is the grader. Human raters of second-language speech are not neutral either: raters who had themselves
learned a test taker's first language rated those test takers more leniently (Winke et al., 2013, abstract). Language
models carry covert prejudice against non-standard varieties of English that shows in decisions even when their overt
statements are favourable, and preference training widened that gap (Hofmann et al., 2024, abstract). Studies of language
models as graders are so far preprints, but they point the same way. GPT-4o partly inferred essay writers' first language,
and "scoring error for non-native English speakers increases when the LLM correctly identifies them as non-native" (Yang et
al., 2025, abstract). Two open models penalized non-native phrasing and informal language on essay tasks at equal
correctness, a bias that "persists despite explicit counter-bias instructions in the grading prompt" (Jadhav et al.,
2026, abstract). No study tests spoken, transcribed answers. Together these imply that an instruction to ignore form is
necessary but not sufficient, that telling a grader the student is a second-language speaker may make matters worse, and
that the only adequate assurance is a measurement: scores for answers of equal content and unequal form.

### 2.5.4 Examining in the student's first language

The alternative is to examine students in their first language against material written in another. This raises the
same three questions in a new form. Recognition quality outside English is reported mostly by vendors; for Korean, the
figures published by Gadfly's recognition provider are vendor claims and no independent benchmark of its realtime model
was found (Appendix A). Korean is also usually scored by character rather than word error, because word spacing is
inconsistent in Korean writing.

Judging is the harder question. Language-model evaluators calibrated against 20,000 human judgments in eight languages
inflated scores, particularly in lower-resource and non-Latin-script languages, and their authors recommend calibration
against native-speaker judgments (Hada et al., 2024, abstract). Judges' ability on Korean long-form answers tracks their
ability on English benchmarks, but they struggle with hard reasoning and with cultural misrepresentation (Son et al., 2024,
abstract). An evaluator that read target-language answers against English reference answers and English rubrics agreed
with human judges better than proprietary models (Doddapaneni et al., 2025, abstract), which is the configuration an
examination of a Korean answer against English course material would use. But consistency across languages is low: five
models judging the same content in 25 languages reached an average Fleiss' kappa of about 0.3, and neither scale nor
multilingual training repaired it, though an ensemble helped (Fu & Liu, 2025, abstract). The evidence therefore supports
cross-lingual judging as feasible, by a panel, with human calibration in the language of examination before its scores are
trusted.

### Addition to the synthesis (Section 2.12)

New finding: **Second-language speakers are disadvantaged at every stage of a synthetic oral examination, and each stage
has its own remedy.** The recognizer makes more errors on their speech and better models do not close the gap (Roll &
Graham, 2025); they pause inside clauses where a fixed end-of-answer silence cuts them off (Kahng, 2014); repetition and
rephrasing in interaction aid their comprehension more than simplification does (Pica et al., 1987); and graders, human
and machine, can be swayed by form, with machine graders not reliably corrected by instruction (Jadhav et al., 2026).
Examining in the student's first language moves the problem to cross-lingual judging, which is feasible but inconsistent
without a panel and human calibration (Fu & Liu, 2025; Doddapaneni et al., 2025).

New gap bullet: **No study measures the scoring of spoken, transcribed answers from second-language speakers by language
models, nor end-of-turn silences suited to second-language speakers in examinations.** Both are open questions for the
empirical test of Chapter 7.

---

## C. Section 5.3.5 Language background

Language background reaches the score by all three routes that Section 5.3 distinguished. Through the transcript, because
recognizers make more errors on second-language speech (Section 2.5.3). Through the path, because a second-language
speaker's mid-clause pauses look like the end of an answer and because the examiner's content cues, such as hedging and
vagueness, are read from English word lists that second-language speakers may trip more often (`PathDrivers.cs`). And
through the judges, because graders can penalize form even when told not to.

Gadfly's answer, designed in GAD-T551 and not yet built, is to ask and not to infer. The student states the language they
speak most comfortably and the language of the examination. When the two are the same, nothing changes. When they differ,
the examination adapts: the recognizer's language is pinned and biased toward the examination's vocabulary within the
provider's limits; the end-of-answer silence starts longer and its shortened "finished sentence" form starts later; the
examiner's questions are plain, and the student may ask for any question to be repeated or said another way at no cost
and outside the scoring path; and the judges are not told the student's language, because the evidence suggests that
labelling invites the bias (Yang et al., 2025), but read a transcript from which fillers and abandoned starts have been
removed by a fixed rule. The declared language is kept out of everything the graders see and is used for one other purpose
only: to group the fairness views by language, which until now required an institution to paste in a grouping.

None of these choices is a value from a source. The longer silences, in particular, are reasoned from the direction of the
pause evidence, not from any measured threshold, and the claim that the panel scores content rather than form for these
students rests, as Section 5.3.2 said of all students, on the prompt until the probe is run. The probe for this case is
specific: the calibration answers rewritten with the surface features most common in Korean speakers' English (articles,
plurals, tense, dropped subjects, prepositions) and with fillers, scored beside the fluent originals. The key monitoring
measure is the difference between panel and blind human scores by language group, since a group whose panel scores fall
below its human scores is being marked down by the panel and not for its understanding.

Asking rather than inferring is also the lawful course. Gadfly is a high-risk system under the AI Act (Section 5.8.1) and
must examine its data for bias. The amended Act permits processing special categories of personal data for bias detection
only where no effective alternative exists (Article 4a, as consolidated on 27 July 2026), and a self-declared language,
which is not itself a special category, is such an alternative. Inferring language background, and with it ethnic origin,
from the voice would come close to the biometric categorisation that Article 5(1)(g) prohibits, and is not needed when the
student can say.

## D. Amendment to Section 6.4

Add to the list of populations the evidence does not cover: "students examined in a second language, for whom no study
measures the machine scoring of spoken, transcribed answers, and students examined in a language other than English
against English material, for whom the evidence on cross-lingual judging comes from benchmark tasks, not from assessment
(Sections 2.5.3, 2.5.4)."

## E. Chapter 7 additions

**Section 7.3 (Measures).** Add: word error rate and recall of the examination's key terms by declared primary language,
against human reference transcripts; the share of answers cut off by the end-of-answer silence, by group; requests to
repeat or rephrase a question per question asked; the panel-minus-human score difference by group; and the score shift on
paraphrases of equal content and second-language form.

**Section 7.4 (Thresholds and decision rules).** Add, as provisional rules to be fixed before data collection: the
second-language form probe passes if mean panel scores shift by no more than 0.25 on the 0 to 10 scale; a language group
is flagged when its panel-minus-human difference departs from the first-language-English group's by more than 0.5 points;
an examination in a language other than English is released for unsupervised grading only once panel-human agreement on a
double-marked calibration set in that language is at least that achieved in English.

## F. Appendix A

Corrections (continue the numbering):

- **The 2025 SLaTE paper on recognition bias is by Roll and Graham, not Graham and Roll.** The 2024 JASA Express Letters
  study of Whisper lists Graham first; the 2025 SLaTE paper lists Roll first. Cite each as published.
- **Korean is not a tonal language.** The tonal-first-language penalty in Roll and Graham (2025) does not transfer directly
  to Korean speakers, who fall among the non-tonal second-language groups in that study.
- **Recognizer keyterm limits differ between batch and realtime.** Gadfly's vocabulary cap (60 terms) exceeded its
  provider's documented realtime limit (50 terms, 1,000 characters) on 22 November 2026; a fix was filed.

New section, "Language background (GAD-T553, 22 November 2026)": ledger entries for the sources below at the levels
recorded in `docs/research/esl-and-korean.md`. Abstract or publisher page read: Graham & Roll (2024); Kahng (2014); the
Huensch (2023); Stivers et al. (2009); Pica et al. (1987); Winke et al. (2013); Hofmann et al. (2024);
Yang et al. (2025); Jadhav et al. (2026); Hada et al. (2024); Son et al. (2024); Doddapaneni et al. (2025); Fu & Liu (2025);
Zhao et al. (2018); Zhang et al. (2021). Full text read: Roll & Graham (2025); Han et al. (2024) (first page). Record
located only, content from a named secondary source: Griffiths (1992); Abedi & Lord (2001); Kieffer et al. (2009); Kim et
al. (2024); Bang et al. (2020). Vendor documentation: ElevenLabs keyterm prompting, realtime API reference, Korean
speech-to-text page and models page; AI Act Articles 4a and 10 (Commission AI Act Service Desk).

## G. References

Abedi, J., & Lord, C. (2001). The language factor in mathematics tests. *Applied Measurement in Education, 14*(3),
219–234. https://doi.org/10.1207/S15324818AME1403_2

Bang, J.-U., Yun, S., Kim, S.-H., Choi, M.-Y., Lee, M.-K., Kim, Y.-J., Kim, D.-H., Park, J., Lee, Y.-J., & Kim, S.-H.
(2020). KsponSpeech: Korean spontaneous speech corpus for automatic speech recognition. *Applied Sciences, 10*(19), 6936.
https://doi.org/10.3390/app10196936

Doddapaneni, S., Khan, M. S. U. R., Venkatesh, D., Dabre, R., Kunchukuttan, A., & Khapra, M. M. (2025). Cross-lingual
auto evaluation for assessing multilingual LLMs. In *Proceedings of the 63rd Annual Meeting of the Association for
Computational Linguistics* (pp. 29297–29329). https://doi.org/10.18653/v1/2025.acl-long.1419

Fu, X., & Liu, W. (2025). *How reliable is multilingual LLM-as-a-judge?* arXiv. https://arxiv.org/abs/2505.12201

Graham, C., & Roll, N. (2024). Evaluating OpenAI's Whisper ASR: Performance analysis across diverse accents and speaker
traits. *JASA Express Letters, 4*(2). https://doi.org/10.1121/10.0024876

Griffiths, R. (1992). Speech rate and listening comprehension: Further evidence of the relationship. *TESOL Quarterly,
26*(2), 385–390. https://doi.org/10.2307/3587015

Hada, R., Gumma, V., de Wynter, A., Diddee, H., Ahmed, M., Choudhury, M., Bali, K., & Sitaram, S. (2024). Are large
language model-based evaluators the solution to scaling up multilingual evaluation? In *Findings of the Association for
Computational Linguistics: EACL 2024* (pp. 1051–1070). https://doi.org/10.18653/v1/2024.findings-eacl.71

Han, S., Chung, M., & Kim, S. (2024). Constructing Korean learners' L2 speech corpus of seven languages for automatic
pronunciation assessment. In *Proceedings of LREC-COLING 2024* (pp. 3772–3781). https://aclanthology.org/2024.lrec-main.334

Hofmann, V., Kalluri, P. R., Jurafsky, D., & King, S. (2024). AI generates covertly racist decisions about people based
on their dialect. *Nature, 633*, 147–154. https://doi.org/10.1038/s41586-024-07856-5

Huensch, A. (2023). Effects of speaking task and proficiency on the midclause pausing characteristics of L1 and L2
speech from the same speakers. *Studies in Second Language Acquisition, 45*(4), 1031–1055.
https://doi.org/10.1017/S0272263123000323

Jadhav, R., Danve, J., & Shaw, S. (2026). *Implicit grading bias in large language models: How writing style affects
automated assessment across math, programming, and essay tasks.* arXiv. https://arxiv.org/abs/2603.18765

Kahng, J. (2014). Exploring utterance and cognitive fluency of L1 and L2 English speakers: Temporal measures and
stimulated recall. *Language Learning, 64*(4), 809–854. https://doi.org/10.1111/lang.12084

Kieffer, M. J., Lesaux, N. K., Rivera, M., & Francis, D. J. (2009). Accommodations for English language learners taking
large-scale assessments: A meta-analysis on effectiveness and validity. *Review of Educational Research, 79*(3),
1168–1201. https://doi.org/10.3102/0034654309332490

Kim, H., Myung, J., Kim, S., Lee, S., Kang, D., & Kim, J. (2024). LearnerVoice: A dataset of non-native English learners' spontaneous speech. In *Proceedings of
Interspeech 2024*. https://www.isca-archive.org/interspeech_2024/kim24v_interspeech.pdf

Pica, T., Young, R., & Doughty, C. (1987). The impact of interaction on comprehension. *TESOL Quarterly, 21*(4), 737–758.
https://doi.org/10.2307/3586992

Roll, N., & Graham, C. (2025). Scaling conformation bias in automatic speech recognition. In *Proceedings of the 10th
Workshop on Speech and Language Technology in Education (SLaTE 2025)* (pp. 133–137). https://doi.org/10.21437/SLaTE.2025-27

Son, G., Ko, H., Lee, H., Kim, Y., & Hong, S. (2024). *LLM-as-a-judge & reward model: What they can and cannot do.*
arXiv. https://arxiv.org/abs/2409.11239

Stivers, T., Enfield, N. J., Brown, P., Englert, C., Hayashi, M., Heinemann, T., Hoymann, G., Rossano, F., de Ruiter, J. P.,
Yoon, K.-E., & Levinson, S. C. (2009). Universals and cultural variation in turn-taking in conversation. *Proceedings of
the National Academy of Sciences, 106*(26), 10587–10592. https://doi.org/10.1073/pnas.0903616106

Winke, P., Gass, S., & Myford, C. (2013). Raters' L2 background as a potential source of bias in rating oral performance.
*Language Testing, 30*(2), 231–252. https://doi.org/10.1177/0265532212456968

Yang, K., Raković, M., Gašević, D., & Chen, G. (2025). *Does the prompt-based large language model recognize students'
demographics and introduce bias in essay scoring?* arXiv. https://arxiv.org/abs/2504.21330

Zhang, J., Zhang, Z., Wang, Y., Yan, Z., Song, Q., Huang, Y., Li, K., Povey, D., & Wang, Y. (2021). speechocean762: An
open-source non-native English speech corpus for pronunciation assessment. In *Proceedings of Interspeech 2021*.
https://arxiv.org/abs/2104.01378

Zhao, G., Sonsaat, S., Silpachai, A., Lucic, I., Chukharev-Hudilainen, E., Levis, J., & Gutierrez-Osuna, R. (2018).
L2-ARCTIC: A non-native English speech corpus. In *Proceedings of Interspeech 2018* (pp. 2783–2787).
https://doi.org/10.21437/Interspeech.2018-1110
