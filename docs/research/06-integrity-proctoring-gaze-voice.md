# Research note 06: Academic integrity, proctoring evidence, and the limits of gaze and voice-stress signals

Prepared 2026-11-21 for Gadfly (GAD-T254). This note checks what the evidence says about cheating in unproctored
online exams, the fairness and side effects of remote proctoring, how accurate webcam gaze tracking is, whether a
"reading" pattern can be seen through a webcam, and whether voice "stress" measures detect anything useful. It also
checks the EU AI Act text and the Commission's guidelines on the emotion-recognition ban in education, because
Gadfly's voice-strain indicators (GAD-T232) and adaptive probing (GAD-T233) sit close to that line. The questions
are: can gaze and voice-strain signals be more than low-confidence reviewer context, is voice strain at risk of being
"emotion recognition", what follows for the design, and is Socratic follow-up a stronger integrity control than
surveillance.

How to read the verification levels:
- **Full text read**: the cited passage was checked in the work itself; the entry names the part that was read.
- **Abstract/publisher page read**: claims are limited to what the abstract or publisher/index page says.
- **Record located only**: the bibliographic record was confirmed, the content was not read; nothing is claimed
  beyond the title and record.

## A. Does proctoring change scores? (evidence that unproctored online exams are inflated)

### Alessio2017
- **APA:** Alessio, H. M., Malay, N. J., Maurer, K., Bailer, A. J., & Rubin, B. (2017). Examining the effect of
  proctoring on online test scores. *Online Learning, 21*(1). https://doi.org/10.24059/olj.v21i1.885
- **Link:** https://olj.onlinelearningconsortium.org/index.php/olj/article/view/885
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (journal article page with abstract).
- **Verified quote or claim:** "Students scored, on average, 17 points lower [95% CI: 14, 20]" and "used significantly
  less time in online tests that used proctoring software versus unproctored tests." Sample: 147 students in several
  sections of one online course, linear mixed-effects models.
- **What it establishes:** In one course, proctored online tests produced markedly lower scores and shorter test
  times than unproctored ones, which the authors read as consistent with unproctored-test advantage (cheating or
  open-resource use). It is one course at one university, and the abstract notes the disparity varied across exams
  and sections.
- **Relevance to Gadfly:** Supports the premise that unsupervised remote assessment is inflated and that some
  integrity control is needed. It does not say which control; it does not validate gaze or voice signals.
- **Confidence:** Medium - clear effect, but single-course observational design and only the abstract was read.

### DendirMaxwell2020
- **APA:** Dendir, S., & Maxwell, R. S. (2020). Cheating in online courses: Evidence from online proctoring.
  *Computers in Human Behavior Reports, 2*, 100033. https://doi.org/10.1016/j.chbr.2020.100033
- **Link:** https://api.openalex.org/works/doi:10.1016/j.chbr.2020.100033 (abstract record);
  https://www.sciencedirect.com/science/article/pii/S2451958820300336 and https://doaj.org/article/aa3ab9a201f44c44854292da11561291
  both returned HTTP 403.
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (full abstract via the OpenAlex record; publisher page blocked).
- **Verified quote or claim:** "Analysis of exam scores shows that online proctoring was associated with a decrease in
  average performance in both courses." and "We interpret these results as evidence that cheating took place in the
  online courses prior to proctoring."
- **What it establishes:** A before/after quasi-experiment in two online courses: when webcam-recording proctoring was
  introduced, scores fell, and the fall held up in regressions with controls. The authors infer prior cheating; a
  change in test environment (anxiety, stricter conditions) is an alternative explanation the design cannot fully
  rule out.
- **Relevance to Gadfly:** Same as Alessio2017: motivates some integrity design. Note the "cheating" inference is
  the authors' interpretation of a score drop, not direct observation of cheating.
- **Confidence:** Medium - consistent with Alessio2017; quasi-experimental and abstract-only.

## B. Ethics, fairness and student experience of remote proctoring

### Coghlan2021
- **APA:** Coghlan, S., Miller, T., & Paterson, J. (2021). Good proctor or "Big Brother"? Ethics of online exam
  supervision technologies. *Philosophy & Technology, 34*(4), 1581-1606. https://doi.org/10.1007/s13347-021-00476-1
- **Link:** https://pmc.ncbi.nlm.nih.gov/articles/PMC8407138
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (PMC open-access copy; first ~100,000 characters covering the argument and
  conclusions; tail was the reference list).
- **Verified quote or claim:** "facial recognition functions in OP systems sometimes have more difficulty recognizing
  darker skin tones"; "Some test-takers may simply have idiosyncratic exam-taking styles, or disabilities and
  impairments" (that trigger flags). Paraphrase: adopting proctoring tools without conditions is argued to be
  unjustified; some tools may be acceptable "as proctors' assistants" if ethical issues are addressed, and
  institutions should ask whether alternative assessment types could be used instead.
- **What it establishes:** A philosophical analysis (not empirical) concluding that AI proctoring raises fairness,
  privacy, autonomy and wrongful-accusation concerns; flags need human review and staff may over-trust them.
- **Relevance to Gadfly:** Directly supports Gadfly's rule that integrity signals are reviewer context, never
  verdicts, and its preference for assessment design (Socratic follow-up) over surveillance.
- **Confidence:** High - full text read; the claims are normative and the article is well cited.

### WoldeabBrothen2019
- **APA:** Woldeab, D., & Brothen, T. (2019). 21st century assessment: Online proctoring, test anxiety, and student
  performance. *International Journal of E-Learning & Distance Education, 34*(1).
  https://www.ijede.ca/index.php/jde/article/view/1106
- **Link:** https://www.ijede.ca/index.php/jde/article/view/1106
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read.
- **Verified quote or claim:** Paraphrase of abstract: in 631 students, higher trait test anxiety was associated with
  lower exam scores, and this was especially true for high-anxiety students taking online-proctored exams.
- **What it establishes:** Proctoring may interact with test anxiety so that anxious students are disadvantaged.
  Correlational; anxiety measured as a trait.
- **Relevance to Gadfly:** Challenges any signal that tracks arousal. If anxious students both score lower and show
  more "strain", a strain metric partly measures anxiety, not dishonesty or misunderstanding; probing harder on
  strain (GAD-T233) could compound the disadvantage.
- **Confidence:** Medium - abstract only, correlational.

### BuolamwiniGebru2018
- **APA:** Buolamwini, J., & Gebru, T. (2018). Gender shades: Intersectional accuracy disparities in commercial gender
  classification. *Proceedings of the 1st Conference on Fairness, Accountability and Transparency, PMLR 81*, 77-91.
  https://proceedings.mlr.press/v81/buolamwini18a.html
- **Link:** https://proceedings.mlr.press/v81/buolamwini18a.html
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (PMLR page).
- **Verified quote or claim:** "darker-skinned females are the most misclassified group (with error rates of up to
  34.7%)"; "The maximum error rate for lighter-skinned males is 0.8%."
- **What it establishes:** Commercial gender-classification APIs (2017-2018 versions) had much higher error on
  darker-skinned women. It is about gender classification, not face detection or gaze.
- **Relevance to Gadfly:** Background for why face-based integrity modules (face count, presence, gaze region) need
  per-group error testing. See caution below: do not cite it as proof that face detection fails.
- **Confidence:** High for what it measured; Low as direct evidence about proctoring.

### YoderHimes2022
- **APA:** Yoder-Himes, D. R., Asif, A., Kinney, K., Brandt, T. J., Cecil, R. E., Himes, P. R., Cashon, C., Hopp,
  R. M. P., & Ross, E. (2022). Racial, skin tone, and sex disparities in automated proctoring software. *Frontiers
  in Education, 7*, 881449. https://doi.org/10.3389/feduc.2022.881449
- **Link:** https://www.frontiersin.org/journals/education/articles/10.3389/feduc.2022.881449/full
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (abstract, methods, results summary and limitations section).
- **Verified quote or claim:** Paraphrase: in ~357 students across four STEM courses (Fall 2020) using Respondus
  Monitor, students with darker skin and Black students were significantly more likely to be flagged for instructor
  review, and women with the darkest skin tones were flagged most. Authors' limitations: one product and version,
  one institution, only 41 students in the darker group, low interrater reliability on fine skin-tone categories,
  no data on whether instructors acted on flags.
- **What it establishes:** Real deployed proctoring face detection produced flag rates that differed by skin tone and
  sex. Effect size estimates are uncertain given the small darker-skin group.
- **Relevance to Gadfly:** Directly relevant to the MindAttic.Webcam presence and gaze modules: flags that depend on
  face/eye detection will be unequally noisy across students. Supports capping confidence, Low severity, and
  never auto-penalising; argues for measuring flag rates by group before any expansion.
- **Confidence:** Medium - full text read, but single product and modest subgroup size.

## C. Detecting AI-written answers

### WeberWulff2023
- **APA:** Weber-Wulff, D., Anohina-Naumeca, A., Bjelobaba, S., Foltýnek, T., Guerrero-Dib, J., Popoola, O., Šigut,
  P., & Waddington, L. (2023). Testing of detection tools for AI-generated text. *International Journal for
  Educational Integrity, 19*, 26. https://doi.org/10.1007/s40979-023-00146-z
- **Link:** https://arxiv.org/abs/2306.15666v2 (preprint page, journal reference Int J Educ Integr 19, 26 (2023))
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (arXiv abstract; journal reference confirmed there and in the
  Leeds repository listing). Author list taken from the arXiv record; check against the journal page before quoting
  in full.
- **Verified quote or claim:** "The researchers conclude that the available detection tools are neither accurate nor
  reliable" and they "have a main bias towards classifying the output as human-written rather than detecting
  AI-generated text." Also: "content obfuscation techniques significantly worsen the performance of tools."
- **What it establishes:** Fourteen tested detectors (including Turnitin and PlagiarismCheck) were unreliable in 2023,
  and simple paraphrasing defeats them further.
- **Relevance to Gadfly:** Supports not adding an "AI-written" classifier to typed answers; integrity should come
  from asking the student to explain and extend their own answer, which a detector cannot do.
- **Confidence:** High - clear published conclusion; detectors change, but no later evidence here overturns it.

## D. Webcam gaze tracking and reading detection

### Papoutsaki2016
- **APA:** Papoutsaki, A., Sangkloy, P., Laskey, J., Daskalova, N., Huang, J., & Hays, J. (2016). WebGazer: Scalable
  webcam eye tracking using user interactions. In *Proceedings of the Twenty-Fifth International Joint Conference on
  Artificial Intelligence (IJCAI-16)* (pp. 3839-3845). AAAI Press.
- **Link:** https://jeffhuang.com/papers/WebGazer_IJCAI16.pdf (author PDF); https://www.ijcai.org/Abstract/16/540
  (listed in search, not opened)
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (author PDF extracted to text: abstract, evaluation, Tables 1-2 discussion,
  conclusion).
- **Verified quote or claim:** Online study (76 participants analysed): "Sampling cursor activity (RR+C) has the
  smallest average error of 174.9 pixels, followed by ... (RR+F+C) with an error of 210.6 pixels." In-lab comparison
  against a Tobii EyeX (4 participants): "The average visual angle was 4.17 or 1.6 inches." Best case in the
  conclusion: "the mean error is 104 pixels in a remote online study."
- **What it establishes:** Browser webcam gaze estimation has errors of roughly 100-250 px, about 4 degrees of visual
  angle, and relies on clicks/cursor movement for self-calibration. Error rose during scrolling tasks.
- **Relevance to Gadfly:** Limits the webcam "reading pattern" and gaze-region signals. A 4-degree error is far larger
  than a single reading saccade (see Rayner2009), so word-by-word reading cannot be resolved; at best, coarse region
  (on screen / off screen, left/right) and long horizontal sweeps. A voice exam has few clicks, so WebGazer-style
  click calibration is weak there.
- **Confidence:** High - figures read directly in the paper.

### SemmelmannWeigelt2018
- **APA:** Semmelmann, K., & Weigelt, S. (2018). Online webcam-based eye tracking in cognitive science: A first
  look. *Behavior Research Methods, 50*(2), 451-465. https://doi.org/10.3758/s13428-017-0913-7
- **Link:** Europe PMC REST record for DOI 10.3758/s13428-017-0913-7 (PMID 28593605); Springer page redirected to
  login.
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (full abstract via Europe PMC).
- **Verified quote or claim:** "in-lab data exhibit an offset of about 172 px (15% of screen size, 3.94° visual angle)
  in the fixation task, while online data is slightly less accurate (18% of screen size, 207 px), and shows higher
  variance."
- **What it establishes:** Independent replication of WebGazer accuracy: ~4 degrees offset in the lab, 15-18% of
  screen size, worse and more variable online. Good enough for coarse regions (eyes vs mouth on a face image), not
  fine detail.
- **Relevance to Gadfly:** Confirms that the gaze module can at most say roughly where on screen a student is looking,
  with large between-person variance. Supports keeping gaze events at capped confidence and Low severity.
- **Confidence:** High - specific numbers in the abstract, consistent with Papoutsaki2016.

### Rayner1998
- **APA:** Rayner, K. (1998). Eye movements in reading and information processing: 20 years of research.
  *Psychological Bulletin, 124*(3), 372-422. https://doi.org/10.1037/0033-2909.124.3.372
- **Link:** Europe PMC REST record for PMID 9849112 (abstract); PubMed page showed only a cookie wall; doi.org
  redirected to APA (not opened). No free full text found.
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read.
- **Verified quote or claim:** "The basic theme of the review is that eye movement data reflect moment-to-moment
  cognitive processes in the various tasks examined."
- **What it establishes:** The standard review of reading eye movements (fixations, saccades, perceptual span). The
  specific numbers usually attributed to it were verified in Rayner2009 below, not in this paper.
- **Relevance to Gadfly:** Framing for the "reading pattern" module: reading is defined by small, fast, regular
  movements measured with lab-grade trackers.
- **Confidence:** Medium - abstract only; numbers taken from the later open paper by the same author.

### Rayner2009
- **APA:** Rayner, K. (2009). Eye movements in reading: Models and data. *Journal of Eye Movement Research, 2*(5):2,
  1-10. https://doi.org/10.16910/jemr.2.5.2
- **Link:** https://bop.unibe.ch/JEMR/article/download/2281/3477/8505
- **Retrieved:** 2026-11-21
- **Verification:** Full text read ("benchmark data" section, p. 2-3).
- **Verified quote or claim:** "the average fixation duration in reading is about 200-250 ms" and "the average saccade
  length (for alphabetic writing systems) is about 7-9 letter spaces"; readers "make regressions ... about 10-15% of
  the time" (citing Rayner 1998, 2009).
- **What it establishes:** Reading consists of ~quarter-second fixations separated by jumps of a few words' width,
  with frequent skips and regressions.
- **Relevance to Gadfly:** Combined with ~4 degree webcam error and ~30 Hz webcams, the defining features of reading
  are below what a webcam tracker can resolve. A "reading" flag from webcam gaze is at best a coarse
  horizontal-sweep heuristic; it also cannot see reading from a second screen or a phone held near the camera, and it
  will fire on students reading Gadfly's own question text.
- **Confidence:** High - numbers read directly.

## E. Voice stress analysis and deception cues

### NRC2003
- **APA:** National Research Council. (2003). *The polygraph and lie detection*. Committee to Review the Scientific
  Evidence on the Polygraph. The National Academies Press. https://doi.org/10.17226/10420
- **Link:** https://www.nationalacademies.org/read/10420/chapter/8 (Chapter 6, Alternative Techniques and
  Technologies, pp. 154-177); table of contents at https://www.nationalacademies.org/read/10420/chapter/1
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (Chapter 6, "Voice Stress Analysis" subsection under "Detection of Deception from
  Demeanor", pp. 166-168, and Conclusions, p. 173). DOI confirmed on Crossref (api.crossref.org/works/10.17226/10420).
- **Verified quote or claim:** "Although proponents of voice stress analysis claim high levels of accuracy, empirical
  research on the validity of the technique has been far from encouraging." (p. 167); "Overall, this research and the
  few controlled tests conducted over the past decade offer little or no scientific basis for the use of the computer
  voice stress analyzer" (p. 168); "The meta-analytic literature fails to identify any pattern of facial or body
  movement that generally signals deception." (p. 164)
- **What it establishes:** A national scientific review found no scientific basis for voice stress analysis as a
  deception detector, and no general facial/body cue to deception.
- **Relevance to Gadfly:** Supports the existing rule that voice strain is never a lie, deception or integrity
  verdict. It also means the strain metric has no validated meaning for integrity at all.
- **Confidence:** High - authoritative review, passages read.

### Harnsberger2009
- **APA:** Harnsberger, J. D., Hollien, H., Martin, C. A., & Hollien, K. A. (2009). Stress and deception in speech:
  Evaluating layered voice analysis. *Journal of Forensic Sciences, 54*(3), 642-650.
  https://doi.org/10.1111/j.1556-4029.2009.01026.x
- **Link:** Europe PMC REST record (PMID 19432740)
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (full abstract via Europe PMC).
- **Verified quote or claim:** "the 'true positive' (or hit) rates for all examiners averaged near chance (42-56%)";
  "the false positive rate was very high, ranging from 40% to 65%"; LVA "operated at about chance levels in the
  detection of truth, deception, and the presence of high and low vocal stress states."
- **What it establishes:** A commercial voice analyser failed to detect even induced vocal stress, not only
  deception, under double-blind testing with 48 speakers.
- **Relevance to Gadfly:** Shows that "stress from voice" products can fail at stress itself. Gadfly's own metrics are
  simpler and descriptive, but no validation exists that they track anything specific.
- **Confidence:** High - clear numeric abstract in a peer-reviewed forensic journal.

### Damphousse2007
- **APA:** Damphousse, K. R., Pointon, L., Upchurch, D., & Moore, R. K. (2007). *Assessing the validity of voice
  stress analysis tools in a jail setting* (NCJ 219031). Report to the U.S. Department of Justice, National Institute
  of Justice. https://www.ojp.gov/pdffiles1/nij/grants/219031.pdf
- **Link:** https://www.ojp.gov/library/publications/assessing-validity-voice-stress-analysis-tools-jail-setting;
  https://www.ojp.gov/pdffiles1/nij/grants/219031.pdf
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (abstract and executive summary, pp. i-iii, extracted from the PDF).
- **Verified quote or claim:** "The programs were not able to detect deception at a rate any better than chance."
  Executive summary: 319 arrestees; sensitivity "very low (15%)" (LVA 21%, CVSA 8%); specificity averaged 92%; False
  Positive Index averaged 9.4 (LVA 5.0, CVSA 14.11); expert-novice correlations 0.11 to 0.52.
- **What it establishes:** In a real high-stakes field setting with urinalysis ground truth, two VSA products missed
  most lies and produced several false accusations per true detection; interpretations were unreliable between
  operators.
- **Relevance to Gadfly:** Base-rate lesson: when the behaviour of interest is uncommon, even a high-specificity
  signal yields more false than true flags. Applies to any strain-based or gaze-based "suspicion" in exams.
- **Confidence:** High - primary report read.

### Giddens2013
- **APA:** Giddens, C. L., Barron, K. W., Byrd-Craven, J., Clark, K. F., & Winter, A. S. (2013). Vocal indices of
  stress: A review. *Journal of Voice, 27*(3), 390.e21-390.e29. https://doi.org/10.1016/j.jvoice.2012.12.010
- **Link:** Europe PMC REST record (PMID 23462686)
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read (structured abstract via Europe PMC).
- **Verified quote or claim:** "Increase in fundamental frequency is the most commonly reported effect of stress in
  well-controlled trials. The trend, however, is not universal. A reduction in noise as reflected by the diminished
  vocal jitter is reported, but less frequently." Conclusions: stress type, gender and individual baseline autonomic
  tone "may explain the primarily equivocal findings".
- **What it establishes:** Across 50+ studies, raised pitch is the most common acoustic correlate of stress but is
  inconsistent; jitter often goes down, not up, under stress; findings are mostly equivocal.
- **Relevance to Gadfly:** Gadfly's within-student baseline is the right instinct (individual differences dominate),
  but even with a baseline, pitch and jitter changes are not specific to stress, and the direction of jitter change is
  not settled. Loudness, pitch and rate also change with question difficulty, fatigue and microphone position.
- **Confidence:** Medium - abstract only, but a review whose conclusion is explicitly cautious.

### DePaulo2003
- **APA:** DePaulo, B. M., Lindsay, J. J., Malone, B. E., Muhlenbruck, L., Charlton, K., & Cooper, H. (2003). Cues to
  deception. *Psychological Bulletin, 129*(1), 74-118. https://doi.org/10.1037/0033-2909.129.1.74
- **Link:** Europe PMC REST record (PMID 12555795)
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read.
- **Verified quote or claim:** 1,338 estimates of 158 cues; liars "are more tense" and "tell less compelling tales";
  "However, many behaviors showed no discernible links, or only weak links, to deceit."
- **What it establishes:** Behavioural cues to lying exist on average but are mostly weak; individual cue values
  (including vocal ones) were not checked here.
- **Relevance to Gadfly:** Aggregate small effects cannot support individual judgments about a student. Also notes
  content cues (less compelling, fewer details) - which is what Socratic follow-up probes directly.
- **Confidence:** High for the abstract-level conclusion.

### BondDePaulo2006
- **APA:** Bond, C. F., Jr., & DePaulo, B. M. (2006). Accuracy of deception judgments. *Personality and Social
  Psychology Review, 10*(3), 214-234. https://doi.org/10.1207/s15327957pspr1003_2
- **Link:** Europe PMC REST record (PMID 16859438)
- **Retrieved:** 2026-11-21
- **Verification:** Abstract/publisher page read.
- **Verified quote or claim:** "people achieve an average of 54% correct lie-truth judgments, correctly classifying 47%
  of lies as deceptive and 61% of truths as nondeceptive"; "people are more accurate in judging audible than visible
  lies".
- **What it establishes:** Across 206 documents and 24,483 judges, unaided humans barely beat chance at spotting lies.
- **Relevance to Gadfly:** Human reviewers shown a strain line or gaze flag will not convert it into accurate
  integrity judgments; the risk is anchoring. Supports showing such context only after the content-based grade and
  keeping it out of blinded grading (as Gadfly already does).
- **Confidence:** High - large meta-analysis, abstract figures clear.

## F. EU AI Act

### EUAIAct2024
- **APA:** Regulation (EU) 2024/1689 of the European Parliament and of the Council of 13 June 2024 laying down
  harmonised rules on artificial intelligence (Artificial Intelligence Act). *Official Journal of the European Union*,
  L, 2024/1689, 12.7.2024. http://data.europa.eu/eli/reg/2024/1689/oj
- **Link:** https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=OJ:L_202401689 (recitals, Art. 3, Art. 5 read);
  Annex III read on the unofficial mirror https://artificialintelligenceact.eu/annex/3/ because the EUR-Lex HTML was
  truncated by the fetch tool before the annexes.
- **Retrieved:** 2026-11-21
- **Verification:** Full text read for Recitals 18 and 44, Art. 3(34), Art. 3(39), Art. 5(1)(f) on EUR-Lex. Annex III
  point 3 read on a secondary mirror only - recheck on EUR-Lex before relying on exact wording.
- **Verified quote or claim:** Art. 5(1)(f) prohibits "the placing on the market, the putting into service for this
  specific purpose, or the use of AI systems to infer emotions of a natural person in the areas of workplace and
  education institutions, except where the use of the AI system is intended to be put in place or into the market for
  medical or safety reasons". Art. 3(39): "'emotion recognition system' means an AI system for the purpose of
  identifying or inferring emotions or intentions of natural persons on the basis of their biometric data". Art.
  3(34): biometric data is personal data "relating to the physical, physiological or behavioural characteristics of a
  natural person". Recital 18: emotions "such as happiness, sadness, anger, surprise, disgust, embarrassment,
  excitement, shame, contempt, satisfaction and amusement"; "It does not include physical states, such as pain or
  fatigue"; and not "the mere detection of readily apparent expressions, gestures or movements, unless they are used
  for identifying or inferring emotions". Recital 44: "There are serious concerns about the scientific basis of AI
  systems aiming to identify or infer emotions", citing "the limited reliability, the lack of specificity and the
  limited generalisability". Annex III 3(d) (mirror): "AI systems intended to be used for monitoring and detecting
  prohibited behaviour of students during tests" (high-risk).
- **What it establishes:** Inferring emotions from voice or face in education is banned (Art. 5 applies from
  2 February 2025, per the Commission guidelines below); test-monitoring AI and learning-outcome evaluation (3(b)) are
  high-risk. The legislator itself records doubts about the science of emotion inference.
- **Relevance to Gadfly:** Gadfly is both a learning-outcome evaluator (3(b)) and a test monitor (3(d)), so EU sales
  carry high-risk obligations. The voice-strain feature is the one component that could fall under the outright ban.
- **Confidence:** High for the quoted articles and recitals; Medium for Annex III wording (mirror only).

### EC_Guidelines2025
- **APA:** European Commission. (2025). *Commission Guidelines on prohibited artificial intelligence practices
  established by Regulation (EU) 2024/1689 (AI Act)* (C(2025) 5052 final, 29 July 2025).
- **Link:** https://digital-strategy.ec.europa.eu/en/library/commission-publishes-guidelines-prohibited-artificial-intelligence-ai-practices-defined-ai-act
  -> https://ec.europa.eu/newsroom/dae/redirection/document/112367 (PDF downloaded and read); secondary summary
  https://fpf.org/blog/red-lines-under-eu-ai-act-unpacking-the-prohibition-of-emotion-recognition-in-the-workplace-and-education-institutions/
- **Retrieved:** 2026-11-21
- **Verification:** Full text read (section 7, Article 5(1)(f), paras 247-263, pp. 78-84; para 5 on status; para 429
  on application date). The library page is dated February 2025; the downloaded document is headed C(2025) 5052 final,
  Brussels 29.7.2025.
- **Verified quote or claim:** Para 247: emotions "should be understood in a wide sense and not interpreted
  restrictively". Para 251 lists behavioural biometrics including "voice, and keystrokes through to eye tracking".
  Para 255, education example: eye tracking "when examining students online to track the fixation point and movement
  of the eyes (gaze point, e.g., to detect if unauthorized material is used) is not prohibited, because the system
  does not identify or infer emotions. By contrast, if the system is also used to detect emotions, such as emotional
  arousal and anxiousness, this would fall within the scope of the prohibition." Para 257: "The general monitoring of
  stress levels at the workplace is not permitted under health or safety aspects." Para 258: safety means "protection
  of life and health and not to protect other interests, for example property against theft or fraud." Para 5:
  "These Guidelines are non-binding."
- **What it establishes:** The Commission reads "emotion" broadly, treats voice as biometric data, explicitly allows
  gaze-point tracking in online exams to detect unauthorised material, and explicitly places "emotional arousal and
  anxiousness" detection in exams inside the ban. Integrity (fraud prevention) does not qualify for the safety
  exception.
- **Relevance to Gadfly:** This is the decisive source. Gadfly's gaze-region and reading-pattern events look like the
  permitted example. The voice-strain indicator, labelled "possible vocal strain / hesitation" and fed into adaptive
  probing, is close to "emotional arousal and anxiousness" detection; renaming alone may not help if the purpose is
  still to infer an inner state.
- **Confidence:** High for what the guidelines say; they are non-binding and only the CJEU interprets the Act
  authoritatively.

## G. Assessment design as the integrity control

### Sotiriadou2020
- **APA:** Sotiriadou, P., Logan, D., Daly, A., & Guest, R. (2020). The role of authentic assessment to preserve
  academic integrity and promote skill development and employability. *Studies in Higher Education, 45*(11),
  2132-2148. https://doi.org/10.1080/03075079.2019.1582015
- **Link:** Crossref API record for the DOI; https://www.tandfonline.com/doi/full/10.1080/03075079.2019.1582015
  returned HTTP 403.
- **Retrieved:** 2026-11-21
- **Verification:** Record located only (Crossref metadata: title, authors, volume, issue, pages; published online
  26 February 2019). Abstract and findings not read.
- **Verified quote or claim:** None from the work. The title states the claim under study (authentic assessment,
  including interactive oral assessment per secondary university pages, as a way to preserve academic integrity).
- **What it establishes:** Only that a peer-reviewed study of this question exists.
- **Relevance to Gadfly:** A lead for evidence that conversational, unscripted assessment is itself an integrity
  control. Needs to be read before it is cited for any finding.
- **Confidence:** Low - content not read.

## Corrections and cautions

- **Woldeab & Brothen 2019 is not in *Online Learning*.** It is in the *International Journal of E-Learning & Distance
  Education* 34(1). (Their 2021 follow-up on video surveillance is also in IJEDE.)
- **Bond & DePaulo 2006 is in *Personality and Social Psychology Review*,** not *Psychological Bulletin* (DePaulo et
  al. 2003 is the *Psychological Bulletin* paper).
- **Gender Shades is about gender classification, not face detection.** It is often cited as showing that proctoring
  face detection fails darker-skinned students. The direct proctoring evidence is Yoder-Himes et al. 2022 (one product,
  one university, 41 students in the darkest group).
- **"WebGazer is comparable to a commercial eye tracker" overstates it.** The 4.17-degree figure is WebGazer's error
  measured against a Tobii EyeX, with 4 participants. It means about 4 degrees of disagreement, not parity.
- **Reading statistics (200-250 ms fixations, 7-9 letter saccades) are widely attributed to Rayner 1998.** They were
  verified here in Rayner 2009 (JEMR), which cites 1998; the 1998 full text was not read.
- **Voice stress and jitter.** It is often assumed that jitter rises under stress; Giddens et al. 2013 report jitter
  reductions under stress where an effect is seen, and call the overall findings equivocal.
- **NIJ voice-stress figure.** The "15 percent of lies detected" headline is the average sensitivity across five drugs
  (LVA 21%, CVSA 8%), with specificity ~92%; it is not a 15% overall accuracy.
- **Dendir & Maxwell's "cheating" is an inference from a score drop,** not observed cheating; vendor press releases
  (ProctorU via newswise) present it more strongly than the abstract.
- **EU guidelines status.** Commission guidelines on Art. 5 are non-binding (para 5). The version read is C(2025) 5052
  final dated 29 July 2025, while the library page carries a February 2025 date; cite the document reference, not the
  page date.
- **High-risk application date not checked.** Annex III obligations were scheduled to apply later than Art. 5; whether
  any later amendment moved that date was not verified in this note. Check before quoting a deadline.
- **Gadfly's own PRIVACY.md says "Nothing infers emotion".** The Commission's para 255 example suggests a regulator
  could see a "vocal strain" indicator in an exam as detecting "emotional arousal and anxiousness", whatever the label.

## Not located

- None of the requested sources was missing. Not read in full: Rayner 1998 (no open copy), Dendir & Maxwell 2020
  (publisher blocked; abstract only), Annex III on EUR-Lex itself (fetch truncation; mirror used).

## Synthesis for Gadfly

- **Unproctored remote exams are inflated** (Alessio2017, DendirMaxwell2020), so integrity design matters. But the
  evidence shows that proctoring lowers scores, not that any particular signal identifies a cheater.
- **Gaze signals: coarse context only.** Webcam gaze error is about 4 degrees, 100-250 px, worse online and variable
  between people (Papoutsaki2016, SemmelmannWeigelt2018), while reading is defined by 7-9 letter jumps and quarter-second
  fixations (Rayner2009). A webcam "reading pattern" cannot reliably tell reading notes from reading the question,
  thinking, or looking away. Keep gaze at capped confidence and Low severity, never in the grade, and explain it to
  reviewers in those terms. Face/eye detection flag rates can differ by skin tone and sex (YoderHimes2022), so measure
  flag rates by group before expanding these modules.
- **Voice-strain signals: no demonstrated validity for integrity or understanding.** Voice stress products perform at
  chance even for stress (Harnsberger2009), fail in the field with ~9 false flags per true one (Damphousse2007), and
  the NRC found "little or no scientific basis" (NRC2003). Acoustic stress correlates are inconsistent (Giddens2013),
  deception cues are weak (DePaulo2003), and humans given such cues stay near chance (BondDePaulo2006). Anxious
  students are already disadvantaged under proctoring (WoldeabBrothen2019). Strain cannot be more than low-confidence
  context, and arguably should not be shown as "strain" at all.
- **EU AI Act risk for voice strain is real.** Voice is biometric data (Art. 3(34); guidelines para 251); "emotion" is
  read broadly (para 247); the guidelines name "emotional arousal and anxiousness" detection during online exams as
  prohibited (para 255); integrity or fraud prevention is not a safety reason (para 258). Describing the output as
  "possible vocal strain / hesitation" and using it to steer probing (GAD-T233) looks like inferring an inner state.
  Gaze-point tracking for unauthorised material is the guidelines' own example of a non-prohibited use.
- **Design implications:** (1) Turn voice-strain indicators off for EU tenants and EU-resident students by default, and
  keep the PRIVACY.md counsel note. (2) Everywhere, stop labelling the line as "strain", "stress" or any state; if it
  is kept, show raw acoustic deltas only, and keep it out of grades and blinded grading (already the case).
  (3) Do not let `strain.vsBaseline` drive adaptive probing; drive probing from answer content (vagueness, missing
  causal links, hedging words in the transcript, which is text and outside the biometric ban). (4) Treat the
  first-two-answers baseline as confounded (warm-up nerves, easier opening questions). (5) Show integrity context to
  reviewers after the content-based score, not before, to limit anchoring.
- **Regulatory scope beyond the ban:** Gadfly evaluates learning outcomes and monitors tests, both Annex III point 3
  high-risk uses (3(b), 3(d)); EU sales need the high-risk compliance programme regardless of the strain decision.
- **Socratic follow-up as the main integrity control:** The evidence here supports this indirectly. Surveillance signals
  are weak, biased or legally exposed; AI-text detectors are unreliable (WeberWulff2023); and the only deception cues
  with some support are about content ("less compelling tales", DePaulo2003). A follow-up built from the student's own
  previous answer tests whether they can explain, extend and defend it, which a scripted or AI-written answer does not
  carry. Coghlan2021 also points to alternative assessment types as the ethical first choice. Direct empirical evidence
  that conversational or oral follow-up reduces cheating was not verified here (Sotiriadou2020 located only); that is
  the next thing to read before making the claim publicly.
- **What the evidence does not support:** using gaze or voice signals to accuse, to grade, to raise severity, or to
  decide who gets harder questions; claims that Gadfly "detects cheating" or "detects stress".
