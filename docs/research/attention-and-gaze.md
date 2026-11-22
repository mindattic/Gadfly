# Attention, eye contact and gentle reminders in the exam room

Prepared 2026-11-22 for Gadfly (GAD-T513). The owner asked for "the science first": what is known about attention,
holding attention, the "eye tricks" that draw attention, and stimuli that gently remind a student to look at the
examiner. This note answers that and grades five recent design proposals against the evidence. It builds on note
[06](06-integrity-proctoring-gaze-voice.md) (webcam gaze accuracy, proctoring fairness, the EU AI Act),
[02](02-oral-assessment-anxiety-fairness.md) (test anxiety) and [EVIDENCE_MAP.md](../EVIDENCE_MAP.md) mechanic 4 (the gaze
fade), and it closes the open item those documents and the dissertation share: "no source in the verified research
addresses gaze aversion while thinking" (EVIDENCE_MAP mechanic 4; ElenchusAtScale Appendix A, corrections 34 and 42, A.7).
That literature exists, and it points in one direction.

Nothing here changes code. The dissertation-ready version of the argument is
[dissertation-attention-chapter.md](dissertation-attention-chapter.md).

**Verification levels** follow the ledger ([references/VERIFICATION.md](references/VERIFICATION.md)): **Full** (the cited
part of the work was read), **Abstract** (abstract or publisher/index record read; claims stay within it), **Record**
(bibliographic record only; nothing claimed beyond it, or the claim is marked as coming from a named secondary source).
Many publisher pages refused automated access; abstracts were then read through Europe PMC, Crossref, Semantic Scholar,
arXiv or institutional repositories. Every source was retrieved 2026-11-20 to 2026-11-22.

---

## 1. Executive summary: what to do

1. **Stop asking for eye contact while the student is thinking.** Looking away while working out a hard answer is a
   normal, measurable way people manage cognitive load, it rises with difficulty even over a video link, and blocking it
   lowers accuracy (Glenberg et al., 1998; Doherty-Sneddon & Phelps, 2005; Abeles & Yuval-Greenberg, 2017; Phelps et al.,
   2006). In the gap between a question and the first word of the answer, children looked away 62-92% of the time
   (Doherty-Sneddon et al., 2012). No reminder of any kind should fire in that gap.
2. **While the student speaks, expect the face about two thirds of the time, not all of it.** Adults in face-to-face
   conversation look off the partner's face about 29% of the time while speaking (Rogers et al., 2018); on a video call,
   speaking cut face gaze with a very large effect (partial eta squared .73; Mansour & Kuhn, 2019). Judge a rolling share
   of time over 20-30 s, not a single look away, and never react to a look away under about 8 s.
3. **Separate the integrity question from the social one.** The integrity premise is "not reading something off screen".
   Only **off-screen** gaze held for a long time is integrity context; on-screen-but-not-on-her-face (the transcript, the
   timer, the middle distance) is not. Reminders should key on off-screen gaze only.
4. **The reminder is the examiner, not the screen.** The best-supported re-orienting cue is the agent herself: a small,
   slow movement (a head tilt, a lean, raised brows) and, if needed, a short varied spoken line at a pause, never
   mid-sentence (D'Mello et al., 2012; Hutt et al., 2021; Szafir & Mutlu, 2012; Bartram et al., 2003; Bailey & Konstan,
   2006).
5. **Drop the white-out; soften the existing fade.** A growing full-screen whitening is a large luminance onset, the kind
   of change that is most intrusive (Theeuwes, 1991), it washes out the face that is the target, risks glare, and reads
   as a punishment that escalates, which anxious students attend to most (Eysenck et al., 2007). If any visual cue is
   kept, dim and soften the **surroundings** while her face stays sharp and bright (Hata et al., 2016), lightly (about
   30-40% dim, 3-4 px blur), slowly, and with a quick clean recovery.
6. **Hide the self-view during answers; do not show it live while answering.** Seeing oneself during a learning session
   raised appearance anxiety and lowered memory for the content (Tien et al., 2023), and an EEG fatigue marker rose with
   self-view on (Xu et al., 2024). The live self-view is most costly exactly while the student is speaking under
   evaluation. Check framing on the get-ready page, then show only a small "camera on" indicator.
7. **Window focus: warn and pause, never auto-cancel, never delete.** A third-strike cancellation with no results saved
   is a solely automated decision with a significant effect (GDPR Art. 22), which the French regulator says must never
   interrupt an exam or trigger a decision without a human check (CNIL, 2023, paras. 38, 40); the Italian regulator
   cleared a university on Art. 22 only because a teacher decided (Garante, 2021). Focus-loss events have no published
   validation and known false triggers (screen readers, OS dialogs, notifications).
8. **Make the examiner warm, not mighty.** Stylisation is safe if it is consistent; uncanniness comes from mismatched
   realism, especially of the eyes (MacDorman et al., 2009; Kätsyri et al., 2015). Keep the sclera white and unglowed so
   her gaze is readable (Yorzinski et al., 2021). No study shows that green or bright irises make people look at eyes
   longer. A friendly examiner lowers anxiety; an unfriendly one raises it (Chittaro & Serafini, 2023; Lucas et al., 2014).
9. **Keep "Mm-hm"; replace the typing ambience with an embodied "thinking" moment.** Contingent backchannels help
   speakers (Bavelas et al., 2000). For waits, a natural spoken filler plus a thinking gesture improved perceived latency,
   whereas an artificial processing sound was no better than silence (Maslych et al., 2025), and symbolic "processing"
   cues pulled gaze off the agent's face (Gonzales et al., 2025). Keystrokes are intermittent changing-state sound, the
   class most disruptive to verbal thinking (Ellermeier & Zimmer, 2014; Szalma & Hancock, 2011).
10. **The cheapest anxiety reducer is a full rehearsal** with the same examiner and room (a peer-led mock OSCE: d = 0.69
    on state anxiety; Lima et al., 2026), plus a plain statement of what the camera is for and what it never decides.

---

## 2. The owner's five proposals, graded

Verdict key: **Keep** (supported as proposed), **Modify** (the aim is supported, the mechanism should change), **Drop**
(the evidence points against it). Confidence is how far the verdict can rest on the evidence, not how strongly it is
worded.

| # | Proposal | Evidence | Verdict | Confidence | How |
|---|---|---|---|---|---|
| 1 | Hide the student's self-view during the exam; show it live only while answering, frozen to a snapshot after | **Partly supports.** Hiding it: two experiments favour (Tien et al., 2023: self-view raised appearance anxiety, and appearance anxiety predicted lower learning; Xu et al., 2024: more EEG alpha, a fatigue marker, with self-view on, N = 32), two found no effect (Bennett et al., 2021: gamma = -.09, p = .29; Savage & Couture Bue, 2023), and large surveys associate "mirror anxiety" with fatigue (Fauville et al., 2023: beta = 0.18, N = 9,787; Ratan et al., 2022: r = .26). **Contradicts** the "live while answering" part: self-focus costs most while performing under evaluation (Eysenck et al., 2007; Belletier et al., 2015). | **Modify** | Medium | Hide self-view for the whole exam. Framing and lighting check on the get-ready page (answers Ratan's caveat that people lose track of how they present). During the exam a small "camera on" dot; a student may open a tiny preview on demand (accommodation and reassurance). No student face snapshots in the transcript. |
| 2 | Window-focus strikes: warn immediately; 3 strikes cancels the exam with no results saved | **Supports** the immediate warning as a deterrent notice. **Contradicts** auto-cancel and deletion: GDPR Art. 22(1) and (3); CNIL (2023) paras. 37-40 ("must never lead to an automatic decision with an immediate effect"; an exam should not be interrupted "solely because of an automatic detection"); Garante (2021) cleared Bocconi on Art. 22 only because a teacher decided; AI Act Annex III 3(d) (high-risk) and Art. 86 (explanation). No peer-reviewed false-positive rate exists for blur/visibilitychange. The cheating gain such controls buy is small for hard-to-search content (proctored vs unproctored Delta = 0.20 overall, smaller for hard-to-search tests; Steger et al., 2020). Proctoring raises anxiety without changing performance or temptation (Conijn et al., 2022). | **Modify** (drop the cancellation and deletion) | High on the legal side; Medium on the behavioural side | Immediate, neutral notice; the examiner and clock pause while away; every event logged with duration and the question it fell in; after repeated or long absences the attempt is marked "needs instructor review" with the log attached. Results and transcript are always kept. Accessibility exemption per student. See spec 4.4. |
| 3 | White-out that grows the longer the eyes leave her frame, clearing on return | **Contradicts.** No study compares darkening with whitening, so this rests on basic findings: luminance onsets disrupt the attended task where offsets did not (Theeuwes, 1991); large luminance jumps cause discomfort glare; whitening erases the face that is the target, where gradual peripheral degradation with a sharp target guides gaze (Hata et al., 2016); an escalating punitive effect captures anxious students' attention and taxes working memory (Eysenck et al., 2007); and it fires on functional gaze aversion (Section 3.1). | **Drop** | Medium (inference, no head-to-head study) | Replace with the reminder ladder in spec 4.2: examiner gesture, then a light peripheral dim with her face kept sharp, then a spoken line at a pause. Soften the current fade (85% dim, 12 px) to about 35% and 4 px. |
| 4 | Conversation layout: examiner left, student right, transcript below the examiner, always scrolled to newest, with snapshot thumbnails | **Partly supports.** A chat convention is familiar and auto-scroll removes a task. But a transcript that grows below her pulls the eyes down and off her face while the student is answering, and constant extra visual channels can divide attention (Alpsülün & Türel, 2026: F(2,87) = 12.01, eta squared = .216). Student face thumbnails are a self-view by another name (proposal 1). | **Modify** | Low-Medium (no direct test of this layout) | Keep left/right and auto-scroll. During the student's answer, show only the current question as a caption close under her face; the full transcript is collapsed or visually quiet until the answer ends. Examiner thumbnails optional; no student face thumbnails (a neutral avatar or initials instead). Eye tracking should treat the transcript as on-screen, never as "away". |
| 5 | Avatar: luminous blue, "powerful and all-knowing but not scary", green eyes "so the student is more apt to stare into them"; "Mm-hm" fillers; quiet typing ambience during waits | **Mixed.** Stylisation is defensible: photographic realism has mostly null effects on social presence while behavioural realism (gaze, responsiveness) helps (Oh et al., 2018); uncanniness comes from mismatched realism and distorted faces, especially eyes (MacDorman et al., 2009; Diel et al., 2022). **No study** tests blue skin, glow, or iris colour on gaze dwell; eyes draw first fixations for their social meaning, not their brightness. "All-knowing" pulls against anxiety evidence (Lucas et al., 2014; Vallefuoco et al., 2025; Chittaro & Serafini, 2023). "Mm-hm" is supported if contingent (Bavelas et al., 2000; Gratch et al., 2006). Typing ambience is not supported (Maslych et al., 2025; Gonzales et al., 2025; Szalma & Hancock, 2011). | **Modify** | Medium for fillers and sound; Low for colour (no evidence either way) | Keep the blue look, test it for appeal and eeriness with students; keep sclera white and unglowed; normal-sized eyes, iris realism matched to the skin; frame her as warm and attentive, not mighty. Keep the contingent "Mm-hm" and one acknowledgement. Replace typing with a "thinking" gesture and a short line; A/B test before keeping any ambience. |

---

## 3. The evidence, by question

### 3.1 Does looking at a face help or hurt someone who is thinking?

It hurts when the thinking is hard. This is the most important finding for Gadfly, because the exam room currently
reacts to every look away of 2 s (6 s early in an answer).

- **Looking away is load management.** "Averting the gaze improves performance", and its frequency rises with the
  difficulty of the processing, not with embarrassment or demand characteristics (Glenberg et al., 1998, Abstract).
  Children questioned face to face or over a live video link looked away more on harder questions in both settings;
  "the primary function of averting gaze is to manage the cognitive load" (Doherty-Sneddon & Phelps, 2005, Abstract).
  Across three experiments gaze aversion rose with difficulty and was absent on very easy tasks: "gaze aversion is an
  active avoidance strategy" (Abeles & Yuval-Greenberg, 2017, Abstract). People move their eyes about twice as often when
  searching long-term memory (Ehrlichman & Micic, 2012, Abstract).
- **Blocking it costs accuracy.** Five-year-olds trained to look away "performed significantly better on challenging
  questions" than controls (Phelps et al., 2006, Abstract; the commonly quoted 72.5% vs 55.9% comes from a BPS Research
  Digest summary, not the abstract). Holding eye contact disrupted visuospatial imagination where looking at other
  visual material did not, so the cost is specific to the face (Markson & Paterson, 2009, Abstract). Eye contact "shares
  domain-general cognitive resource" with word retrieval and slowed it when retrieval and selection demands were both
  high (Kajimura & Nomura, 2016, Abstract). A communicative context with eye contact impaired working memory (Nemeth et
  al., 2013, Abstract). Hessels (2020, Full, first part) reviews the same point from conversation research: "looking at
  the other indeed interferes with the production of spontaneous speech".
- **How much looking away is normal.** In the thinking gap between question and answer, children looked away 62-92% of
  the time depending on group and difficulty (typical development 70% easy, 92% hard; autism 62% and 82%); while
  listening to the question about 23-30%; while speaking about 21-27% (Doherty-Sneddon et al., 2012, Full). Adults in
  four-minute face-to-face conversations looked off the partner's face about 29% of the time while speaking against
  about 10% while listening; mutual **eye** contact was only 12% of the conversation, in instances of about 0.36 s,
  while mutual **face** gaze was about 60% in episodes of about 2.2 s (Rogers et al., 2018, Full; the 2.2 s figure is
  often misquoted as eye contact). Speakers look away at the start of an utterance (Kendon, 1967, as quoted in Hessels,
  2020). On a Skype call, talking reduced face gaze against listening, F(1,58) = 160, partial eta squared = .73, whether
  or not participants believed the call was live (Mansour & Kuhn, 2019, Full).
- **How long mutual gaze is comfortable.** Across 428 people aged 11-79 from 56 nationalities, the preferred duration of
  mutual gaze with a face on screen was 3.3 +/- 0.7 s (Binetti et al., 2016, Full). Given a choice, people looked longer
  at averted than at direct gaze, and direct gaze raised skin conductance at 2 s and 5 s (Helminen et al., 2011,
  Abstract). Eye contact is detected early and changes concurrent processing (Senju & Johnson, 2009, Abstract); live
  direct gaze raises arousal when one knows one is being attended to, and "longer periods of eye contact may be disruptive
  for cognitive performance" (Hietanen, 2018, Full, first part).
- **What the examiner's own gaze does.** A presenter who looked at the camera 30% of the time improved viewers' recall
  over a video link (Fullwood & Doherty-Sneddon, 2006, Abstract). That supports the examiner looking at the student. It
  says nothing about requiring the student to look back.

**For Gadfly.** A requirement to hold the gaze on her face while answering asks the student to give up the strategy that
helps most at the moments the exam most wants to see, and partly measures tolerance of eye contact instead of
understanding. The existing "thinking grace" (6 s in the first 10 s) is a step in the right direction but far too short:
the thinking gap alone is mostly spent looking away.

### 3.2 Who pays most: culture, autism, social anxiety, ADHD

- **Culture.** On "think" questions, Canadians and Trinidadians looked up on about 80% of trials, Japanese participants
  looked down on 75% (McCarthy et al., 2006, Full, Table 1); Canadians looked up when thinking only when they knew they
  were watched (McCarthy et al., 2008, Abstract). Japanese participants rated eye-contact faces as angrier and less
  approachable (Akechi et al., 2013, Abstract), and the angle at which people feel looked at differs by culture (Uono &
  Hietanen, 2015, Abstract). A single "on her face" rule will look different for different groups.
- **Autism.** Gaze differences to social stimuli are reliable (meta-analyses: mean effect 0.55 over 38 studies,
  Chita-Tegmark, 2016; Hedges g up to about 0.5 over 122 studies, larger for interactive and human stimuli, Frazier et
  al., 2017; both Abstract). When gaze was **constrained** to the eye region, autistic participants showed abnormally
  high subcortical (amygdala) activation, and the authors warn that "forcing individuals with autism to look in the eyes
  might be counterproductive and elicit more anxiety" (Hadjikhani et al., 2017, Full). First-hand accounts: "Eye contact
  interrupts the flow of my thoughts" (Trevisan et al., 2017, Full). The gaze fade is a forcing rule of exactly this kind.
- **Social anxiety.** Reduced eye contact with emotional faces, g = -0.67 (negative) and -0.49 (positive) (Günther et al.,
  2021, meta-analysis, Abstract); people with high social anxiety avoid faces mainly while speaking and interacting (Chen
  et al., 2020, Abstract); gaze-anxious people looked at the face less only while talking (Mansour & Kuhn, 2019, Full).
- **ADHD.** No solid evidence of an ADHD-specific eye-contact difference was found; reported differences seem to come with
  co-occurring autism or anxiety. Stated as a gap, not a finding.

**For Gadfly.** The eye-contact accommodation (GAD-T261) helps declared students. The bigger fix is a default that does
not penalise normal gaze aversion, so undeclared autistic, socially anxious and culturally different students are not
filtered by it (ElenchusAtScale 6.4 names this gap).

### 3.3 What re-orients attention gently?

- **Kinds of cue.** Stimulus-driven (exogenous) cues win early selection whatever the observer intends (Theeuwes, 2010,
  Abstract), but an observer engaged elsewhere may not be captured by an onset at all (Yantis & Jonides, 1990,
  Abstract). Motion onset captures attention (Abrams & Christ, 2003, Abstract). In the periphery, motion was missed under
  2% of the time against up to 25% for colour changes, and slow blinks and slow small movements were rated least
  distracting (Bartram et al., 2003, Full).
- **Onsets vs offsets.** "Contrary to an offset transient, an onset transient presented at the attended location
  disrupted performance" (Theeuwes, 1991, Abstract). A brightening is an onset, a dimming an offset; whitening the screen
  is the more intrusive of the two. (Onsets do not always dominate: Miller, 1989.)
- **Subtle guidance.** Brief luminance or warm-cool modulations in the periphery draw the eye without awareness (Bailey et
  al., 2009, Abstract via secondary pages); gradual blur of everything except the target guides attention without being
  noticed (Hata et al., 2016, Abstract-level). In VR, subtle methods guide gaze only probabilistically (Grogorick et al.,
  2018, Abstract).
- **Gaze-reactive tutors.** Gaze Tutor spoke short lines when no gaze was detected for 5 s; it "successfully reoriented
  students' attentional patterns" and improved deep-reasoning learning, with little effect on motivation (D'Mello et al.,
  2012, Abstract, N = 48). In 287 classroom students, interventions that used the student's name, restated content or
  asked a question reduced mind wandering and improved retention for low-prior-knowledge students; the authors' rules are
  that the design must "fail-soft", students "should not feel like they are being watched", phrases are never repeated
  in a session, and interventions never interrupt the tutor's speech (Hutt et al., 2021, Full). They also criticise
  Gaze Tutor's rule because it "would mischaracterize students who close their eyes to concentrate". A robot that raised
  its voice and gestured when attention dropped improved recall 43% over baseline (Szafir & Mutlu, 2012, Full, N = 30).
- **Timing.** Interruptions in the middle of a task, against at task boundaries, took 3-27% longer, produced about twice
  the errors and about twice the rise in anxiety (Bailey & Konstan, 2006, Abstract via search); interruptions at good
  breakpoints cost less (Iqbal & Bailey, 2005).
- **Habituation.** Warnings that change their appearance resisted habituation in fMRI (Anderson et al., 2016, Abstract).
- **Anxiety.** Anxiety "increases the extent to which processing is influenced by the stimulus-driven attentional system"
  and impairs goal-directed attention (Eysenck et al., 2007, Abstract). Being watched by an evaluator disrupted executive
  attention (Belletier et al., 2015, Abstract). Vigilance tasks "reduce task engagement and increase distress" (Warm et
  al., 2008, Abstract).
- **Mind wandering in tests.** During aptitude tests, mind wandering "was consistently associated with worse performance"
  (Mrazek et al., 2012, Abstract); it increases over time on task (Zanesco et al., 2025, meta-analysis, Abstract) and
  harms complex tasks more (Randall et al., 2014, meta-analysis, Abstract). Being asked questions reduces it: interpolated
  tests roughly halved mind wandering in online lectures and lowered anxiety toward the final test (Szpunar et al., 2013,
  Abstract). A Socratic exam is a continuous stream of questions, which is itself the strongest attention support.
- **Visual degradation as feedback.** No study tests dimming, blurring or whitening as gaze-contingent behavioural
  feedback. Degraded text has an almost null effect on recall (Xie et al., 2018, reanalysed by Weißgerber et al., 2021),
  so a light blur probably costs little cognition, but nothing shows it helps. WCAG 2.3.1 defines a general flash as a
  pair of opposing luminance changes of 10% or more over a large area, no more than three per second; a single slow fade
  is not a flash, but rapid look-away/look-back cycling near the threshold could become one (W3C, Full).

**For Gadfly.** The examiner is the best reminder: small, slow, at a breakpoint, varied, rare. A visual cue, if kept,
should be peripheral and leave her face sharp. Punitive escalation is the opposite of what the evidence favours.

### 3.4 The examiner's appearance and voice

- **Stylisation and uncanniness.** The uncanny valley "exists only under specific conditions", with good support for
  perceptual mismatch (Kätsyri et al., 2015, Abstract). "A mismatch in the size and texture of the eyes and face was
  especially prone to make a character eerie", and atypical proportions disturb more on photorealistic faces (MacDorman et
  al., 2009, Abstract; Seyama & Nagayama, 2007, Abstract). Material stylisation drives appeal (Zell et al., 2015,
  Abstract). Imperfect human-likeness lowered trust in an investment game (Mathur & Reichling, 2016, Abstract). A
  meta-analysis found face distortion the strongest manipulation (g = 1.46; Diel et al., 2022, figures from repository
  snippets, Abstract via Crossref). **No study** tests blue skin or glow.
- **Eyes.** Gaze is read from the white sclera against the iris (Kobayashi & Kohshima, 2001); with a white sclera gaze
  direction was judged above 98% correctly whatever the iris colour, and pigmented sclera with dark irises cut accuracy
  and slowed fixation (Yorzinski et al., 2021, Full, N = 51). **No study** shows iris colour changes how long people look
  at eyes; people fixate eyes first for their social meaning, not their salience (Birmingham et al., record only). Iris
  colour effects on trust are weak and confounded by face shape (Kleisner et al., 2013, Abstract).
- **Presence and learning.** Across 152 studies, photographic realism had mostly null effects on social presence while
  behavioural realism (mutual gaze, responsiveness) had mostly positive ones (Oh et al., 2018, Full, partly).
  Pedagogical agents help learning a little (g+ = 0.20, 3D agents 0.11; Castro-Alonso et al., 2021, Abstract;
  AI-powered agents g = 0.43, Dai et al., 2024, Abstract); humanoid agents reduced cognitive load where "character"
  agents slightly raised it (Davis, 2018, Abstract).
- **Anxiety and framing.** Framing an interviewer as a computer "lowers evaluation fears and impression management"
  (Lucas et al., 2014, Abstract). Rehearsing an oral assessment with a virtual human produced more stress than with a
  human teacher, with similar engagement (Vallefuoco et al., 2025, Abstract, N = 50); a progressively less friendly
  virtual examiner raised anxiety (Chittaro & Serafini, 2023, Abstract). Watching-eyes cues cut antisocial behaviour risk
  by 35% in one meta-analysis (Dear et al., 2019, Abstract) but had no effect on generosity or moral judgement in others
  (Northover et al., 2017a, 2017b, Abstract); a deterrent from "being watched" is plausible but modest and contested.
- **Backchannels and waits.** Listeners shape the story through their responses (Bavelas et al., 2000, Abstract);
  contingent, not random, nods and sounds build rapport (Gratch et al., 2006, Record). Turn gaps are fractions of a second
  across languages (Stivers et al., 2009, Abstract). With 1.5-6.5 s delays, a natural spoken filler with a thinking
  gesture improved perceived response time at 4 s and 6.5 s and was preferred by 65%, while a spinner with a
  "continuous processing sound" was no better than silence (Maslych et al., 2025, Full, N = 54); symbolic delay cues made
  people look away from the agent's face (Gonzales et al., 2025, Abstract, N = 24). Speech noise harms performance most
  (g = -0.43) and intermittent noise more than continuous (Szalma & Hancock, 2011, Full); changing-state sound such as
  keystrokes is what disrupts verbal memory (Ellermeier & Zimmer, 2014, Abstract). No study tests typing sounds as a
  waiting cue.

### 3.5 Self-view and being watched

The large associations between seeing oneself and video-call fatigue are correlational (Fauville et al., 2023; Ratan et
al., 2022). The direct manipulations are mixed: Tien et al. (2023) and Xu et al. (2024) favour hiding self-view; Bennett
et al. (2021) and Savage & Couture Bue (2023) found no effect, and in Savage's study being on camera (with no self-view)
was what lowered maths scores. The theory itself is acknowledged as untested (Bailenson, 2021, Full). The consistent
thread is that **being watched** is the stressor (Shockley et al., 2021; Belletier et al., 2015; Conijn et al., 2022);
hiding the mirror removes one channel of it, cheaply. What is said about the camera matters too: an exam room that
explains "the camera checks that you are present and alone; it never grades you or decides anything" addresses the
larger stressor.

### 3.6 Focus loss and proportionate responses

- **Validity.** No peer-reviewed study measures how often `blur`/`visibilitychange` fire on honest students. Known
  triggers besides switching away: OS dialogs and notifications, a second monitor, screen readers, magnifiers, dictation
  software. The CNIL asks institutions to plan for screen-reader and magnifier users and for temporary loss of connection
  (CNIL, 2023, paras. 4, 10). Automated proctoring flags missed all six instructed cheaters in a small randomised test
  (Bergmans et al., 2021, Abstract).
- **Benefit.** Unproctored ability tests score higher by Delta = 0.20, less for measures hard to look up on the internet
  (Steger et al., 2020, meta-analysis, Abstract). An adaptive spoken dialogue built from the student's own words is
  about as hard to look up as an assessment gets; the CNIL names oral defences as formats that need no remote
  surveillance (para. 7).
- **Costs.** Proctoring "had no effect on the temptation to cheat, exam difficulty or performance" but raised test anxiety
  (Conijn et al., 2022, Abstract); 91.6% of surveyed students said webcam watching made them anxious (Kharbat & Abu
  Daabes, 2021, Full).
- **Law.** GDPR Art. 22: a right not to be subject to a decision based solely on automated processing with legal or
  similarly significant effects, and to human intervention and contest (Full). Garante provv. 317/2021 (Bocconi,
  Respondus): fined (EUR 200,000, reduced to 150,000 on appeal), behaviour analysis held to be profiling; Art. 22 not
  engaged only because the decision was a teacher's (Full, first 100k characters). CNIL délibération 2023-058: automated
  tools "must never lead to an automatic decision with an immediate effect for the candidate", a human check comes before
  any decision, and an exam is not to be interrupted "solely because of an automatic detection" (paras. 37-40, Full). AI
  Act Annex III 3(d): test monitoring is high-risk; Art. 86: right to an explanation (Full). Secondary sources report
  that a 2026 amendment moved Annex III obligations to 2 December 2027; not verified in the Official Journal. US:
  Ogletree v. Cleveland State (N.D. Ohio 2022) held room scans unreasonable and weighed whether they were "truly, and
  uniquely, effective" (Full). UK Equality Act 2010 s. 20(3) and ADA: a fixed strike rule is a "provision, criterion or
  practice" needing a reasonable-adjustment route.

### 3.7 Test anxiety and oral exams

Test anxiety relates small-to-moderately and negatively to performance (von der Embse et al., 2018, meta-analysis of 238
studies, Abstract). Anxiety pulls attention toward threat and away from the task (Eysenck et al., 2007). Students are more
nervous about oral than written assessment but find it more useful and authentic (Huxham et al., 2012, Abstract). A
randomised peer-led mock OSCE cut state anxiety (STAI 54.8 vs 63.6, d = 0.69; Lima et al., 2026, Abstract); a passive
mock-viva video raised knowledge but not calm (Knight et al., 2013, Record). Expressive writing about worries before an
exam raised scores in field experiments (Ramirez & Beilock, 2011, Abstract). Frequent low-stakes questioning lowers
anxiety toward the final test (Szpunar et al., 2013).

---

## 4. Design specifications

Each number below is a design choice reasoned from the cited studies, not a value any study tested. Each should be
tuned on pilot data (Section 5).

### 4.1 Phase-aware gaze policy (replaces the 2 s / 6 s rule)

| Phase | Starts | Ends | Expected gaze | Reminder |
|---|---|---|---|---|
| Listening | examiner starts the question | examiner's last word | mostly on her face (about 70-90%; Rogers et al., 2018; Doherty-Sneddon et al., 2012) | none; record only |
| Thinking gap | examiner's last word | student's first word (or 20 s) | mostly away (62-92%; Doherty-Sneddon et al., 2012) | **none, ever** |
| Speaking | student's first word | end of answer | on face about 2/3 of the time (about 29% away; Rogers et al., 2018) | only by the rule below |
| Examiner thinking (wait) | answer ends | next question starts | anything | none |

**Speaking rule.** Classify each gaze sample as *face* (her stage plus the calibrated margin, as now), *on-screen* (anywhere
else in the window, including the transcript and captions) or *off-screen*. Fire the first reminder step only when
**both** (a) continuous off-screen gaze has lasted 8 s or more and (b) off-screen share over the last 30 s exceeds 50%.
On-screen-but-not-face never fires anything. Tracking quality below "fair" (GAD-T305) raises both thresholds by half or
switches reminders off, because a 4-degree tracker cannot afford tight thresholds (note 06). Thresholds are per answer;
reset at each new question.

**Why 8 s and 30 s.** Spontaneous looks away during speech are short and frequent; a preferred mutual-gaze episode is
about 3.3 s (Binetti et al., 2016) and face-gaze episodes about 2.2 s (Rogers et al., 2018), so several seconds away is
normal. Gaze Tutor's 5 s rule for *no* valid gaze was criticised for catching eyes closed in concentration (Hutt et al.,
2021); 8 s off-screen *and* a majority share over 30 s separates "reading something" from thinking more cleanly.

### 4.2 The reminder ladder (replaces the fade and the white-out)

1. **Examiner gesture** (first trigger). A small, slow movement toward the student over about 0.6-1 s: a slight head tilt
   or lean, raised brows, a soft blink, with her eyes on the camera (Bartram et al., 2003; Abrams & Christ, 2003; Oh et
   al., 2018). No sound, no screen change. Many students will not consciously notice it, which is the point.
2. **Peripheral soften** (if off-screen continues for 4 s more). Everything except her stage dims to at most 35% and
   blurs to at most 4 px over 2 s; her face stays at full brightness and sharpness (Hata et al., 2016). Restore over
   300-400 ms when the gaze returns. Hysteresis: once restored, no new soften for 5 s, so the screen never cycles near
   the WCAG 2.3.1 flash limit. Never brighten or whiten.
3. **Spoken line** (if off-screen continues 6 s more, and only at a pause in the student's speech of 700 ms or more, never
   over them; Bailey & Konstan, 2006; Hutt et al., 2021). One short, warm, varied line, never the same twice in a session
   (Anderson et al., 2016): "Take your time; I'm right here." / "Whenever you're ready, tell me more." Never "look at me".
   At most once per question, at most three times per exam.
4. **Record, never act.** Every step is logged as low-confidence reviewer context (as now: confidence capped at 0.4,
   severity at most Low, hidden from blind graders). No step ever changes the score, the question path or the clock.

Under the eye-contact accommodation, steps 1-3 are off (as now). Integrity notes keep working.

### 4.3 Self-view policy

- Get-ready page: full self-view with the framing, lighting and calibration checks (GAD-T305).
- During the exam: **no self-view**. A small "camera on" indicator near the edge of the window; a "show my camera"
  toggle opens a thumbnail-sized preview for students who want reassurance (and closes itself after 10 s).
- No live self-view while answering, and no frozen snapshots of the student's face in the transcript.
- Plain text on the get-ready page: what the camera checks (present, alone, looking at the screen), that frames never
  leave the device, and that it never grades or decides.

### 4.4 Window focus (replaces the 3-strike cancellation)

- Ignore blips under 1 s (the current integrity.js threshold is 0.5 s; 1 s also absorbs most OS chrome clicks).
- On a longer loss: the examiner stops speaking and the answer clock pauses; on return a neutral banner says "The exam
  paused while the window was in the background", and the examiner repeats the last sentence of the question.
- Log each event with its duration and the question it fell in; show the log to reviewers after the blind score.
- Three or more events, or one over 60 s, in an attempt: mark the attempt **"needs instructor review"** and tell the
  student so, with a box to explain. Nothing is cancelled automatically and nothing is deleted; the instructor decides,
  and the student can contest (GDPR Art. 22(3); CNIL 2023 paras. 38-40; AI Act Art. 86).
- An accommodation per student turns the counting off (screen readers, magnifiers, dictation).
- No visible strike counter during the exam (a running count is a standing threat cue; Eysenck et al., 2007).

### 4.5 Layout

- Examiner on the left, student on the right, newest at the bottom with auto-scroll: keep.
- While the student is answering: the current question as a one- or two-line caption directly under her face; the
  transcript panel collapsed or dimmed so it does not pull the eyes down. It expands again when the examiner starts
  talking.
- Examiner snapshots optional. Student entries carry initials or a neutral icon, not their face.
- Everything inside the window counts as on-screen for the gaze policy (4.1).

### 4.6 Examiner design

- Keep the blue, stylised look, with every part of the face at the same level of stylisation (skin, eyes, teeth, lip
  sync). No photoreal iris texture on a glowing face; no enlarged eyes (MacDorman et al., 2009; Seyama & Nagayama, 2007).
- Sclera white and outside the glow; a clear iris-sclera edge (Kobayashi & Kohshima, 2001; Yorzinski et al., 2021). Green
  irises are fine; expect no extra dwell from them.
- Behaviour over looks: look at the camera when asking and listening; look away briefly when "thinking" during waits
  (models gaze aversion as normal); mutual-gaze stretches of a few seconds, broken naturally (Binetti et al., 2016).
- Tone: warm, attentive, plainly an AI ("I'm an AI examiner; take your time"), not "all-knowing" (Lucas et al., 2014;
  Chittaro & Serafini, 2023).
- Fillers: keep the single receipt line when an answer ends and the contingent "Mm-hm" at the student's pauses (already
  built, GAD-T496; Bavelas et al., 2000). For waits over about 2 s, a thinking gesture (glance aside, slight nod) plus one
  short line (as now, ExaminerFiller); a second line only past about 15-20 s (Maslych et al., 2025).
- Typing ambience (GAD-T502): default it off, or keep it only as an A/B arm; if kept, keep it very quiet, stop it at
  least 500 ms before she speaks, and never play it while the student is speaking.

### 4.7 Anxiety support

- A free, unlimited practice run in the same room with the same examiner before any graded attempt (Lima et al., 2026).
- An optional one-minute "anything on your mind before we start?" prompt on the get-ready page (Ramirez & Beilock, 2011),
  never stored or graded.

---

## 5. Open questions and pilot tests

| Question | Design | Primary metric | Secondary metrics |
|---|---|---|---|
| Does the phase-aware policy (4.1) cost integrity? | Randomise attempts: current fade vs 4.1 + 4.2; include a relay condition (a confederate source off-screen) | Detection of the relay condition by reviewers | Judged score within students; answer length; reminder rate per 10 answering minutes, by group |
| Does gaze enforcement hurt honest answers? | Within-student, honest students only: answers with vs without reminders (extends GAD-T448) | Judged score of the answer and the next one | Latency to first word; disfluency; self-reported anxiety (single item after the exam) |
| Which reminder works best? | Three arms: gesture only, gesture + soften, gesture + soften + spoken | Seconds to return to on-screen after the trigger | Anxiety item; perceived fairness; reminders per exam (habituation over time) |
| Darkening vs white-out (if the owner wants the comparison) | Two arms, short sessions, honest students | Return latency | Discomfort rating; anxiety; next-answer score |
| Self-view | Hidden vs live-while-answering vs on-demand thumbnail | Appearance-anxiety item (as Tien et al., 2023) | Judged score; fatigue item; preview toggle use |
| Waits | Typing ambience vs thinking gesture + line vs silence | Perceived wait and naturalness | Gaze on face during the wait (Gonzales et al., 2025); anxiety item |
| Focus events | Log every blur/visibility event with OS, assistive-tech flag and browser | False-trigger rate among students later confirmed honest | Rate by group and assistive technology |
| Examiner look | Blue stylised vs a natural-skin version of the same model | Appeal and eeriness indices (Ho & MacDorman style) | Gaze share on face; anxiety item |
| Gaze norms | Record gaze share by phase for consenting honest students | Distribution of face / on-screen / off-screen share by phase | Differences by self-declared group (only with consent and in aggregate) |

Every comparison by group follows the GAD-T261/T447 rules: groups under 5 are withheld, and nothing is stored that
infers emotion (note 06; the gaze-point use is outside the AI Act's emotion ban, but an "anxiety" reading of the camera
would not be).

---

## 6. Corrections and cautions

- **"People look away while thinking" now has sources.** It was unsupported in the earlier ledger (ElenchusAtScale
  corrections 34 and 42). It is supported at abstract level by Glenberg et al. (1998), Doherty-Sneddon & Phelps (2005)
  and Abeles & Yuval-Greenberg (2017), and at full-text level by Doherty-Sneddon et al. (2012), Mansour & Kuhn (2019) and
  Rogers et al. (2018). The reviewer wording "often thinking" is now defensible; "a well-supported sign of thinking" still
  overstates it as a statement about a particular look away.
- **Rogers et al. (2018):** about 2.2 s is mutual *face* gaze; mutual *eye* contact averaged about 0.36 s and 12% of the
  conversation.
- **Phelps et al. (2006):** the 72.5% vs 55.9% figure is from a BPS Research Digest summary, not the abstract read.
- **Glenberg et al. (1998):** no recall percentages were verified; cite the direction only.
- **Tanaka & Sung** is 2016 (vol. 46), though the DOI carries 2013 (online first).
- **Alemdag (2022)** is DOI 10.1007/s10639-022-11154-w; 10.1007/s10639-022-11532-4 is Polat (2022).
- **Kobayashi & Kohshima (1997)** is 10.1038/42842; the gaze-signal quote is from their 2001 paper.
- **Castro-Alonso et al. (2021)** has four authors; DOI 10.1007/s10648-020-09587-1.
- **Abrams & Christ (2003)** is 10.1111/1467-9280.01458.
- **Harrison et al., "Rethinking the progress bar"** is UIST 2007, not 2010.
- **Woldeab & Brothen (2021)** had no unproctored comparison group; it cannot show proctoring raises anxiety, only how
  anxiety related to scores among proctored students.
- **Bocconi fine** is EUR 200,000 in the decision, reduced to EUR 150,000 by the Milan court.
- **Stivers et al. (2009):** the abstract gives a 250 ms spread around the cross-language mean; the "about 200 ms gap" is
  not verified here.
- **Fullwood & Doherty-Sneddon (2006)** is about the *presenter's* gaze; its audio-only study was null.

## 7. Not verified, and gaps

- Full text not read for many abstract-level entries (publisher blocks). Not verified at all: Diel et al. (2022)
  moderator effect sizes (repository snippets only), Schroeder et al. (2013) pooled g, Northover et al. (2017) pooled
  numbers, Shiwa et al. (2009) 1 s preference, Gratch et al. (2006) content, Schwind et al. (2018), Mor & Winquist (2002),
  Fejfar & Hoyle (2000), Jones & Macken (1993), Myers (1985), the UvA ruling text, the WP251 Art. 22 guidance, AI Act
  Art. 14 and the 2026 timing amendment.
- **No study** of: a 3D avatar examiner with gaze enforcement; darkening vs whitening as feedback; iris colour and gaze
  dwell; blue or glowing agents; typing sounds as a wait cue; browser focus-loss false-positive rates; hiding self-view
  during an evaluative oral exam.

---

## Sources

APA 7. Level in brackets.

**Gaze aversion and eye contact.**
Abeles, D., & Yuval-Greenberg, S. (2017). Just look away: Gaze aversions as an overt attentional disengagement mechanism. *Cognition, 168*, 99-109. https://doi.org/10.1016/j.cognition.2017.06.021 [Abstract]
Akechi, H., Senju, A., Uibo, H., Kikuchi, Y., Hasegawa, T., & Hietanen, J. K. (2013). Attention to eye contact in the West and East: Autonomic responses and evaluative ratings. *PLoS ONE, 8*(3), e59312. https://doi.org/10.1371/journal.pone.0059312 [Abstract]
Binetti, N., Harrison, C., Coutrot, A., Johnston, A., & Mareschal, I. (2016). Pupil dilation as an index of preferred mutual gaze duration. *Royal Society Open Science, 3*(7), 160086. https://doi.org/10.1098/rsos.160086 [Full]
Chen, J., van den Bos, E., & Westenberg, P. M. (2020). A systematic review of visual avoidance of faces in socially anxious individuals. *Journal of Anxiety Disorders, 70*, 102193. https://doi.org/10.1016/j.janxdis.2020.102193 [Abstract]
Chita-Tegmark, M. (2016). Social attention in ASD: A review and meta-analysis of eye-tracking studies. *Research in Developmental Disabilities, 48*, 79-93. https://doi.org/10.1016/j.ridd.2015.10.011 [Abstract]
Doherty-Sneddon, G., Bruce, V., Bonner, L., Longbotham, S., & Doyle, C. (2002). Development of gaze aversion as disengagement from visual information. *Developmental Psychology, 38*(3), 438-445. https://doi.org/10.1037/0012-1649.38.3.438 [Abstract]
Doherty-Sneddon, G., & Phelps, F. G. (2005). Gaze aversion: A response to cognitive or social difficulty? *Memory & Cognition, 33*(4), 727-733. https://doi.org/10.3758/BF03195338 [Abstract]
Doherty-Sneddon, G., Riby, D. M., & Whittle, L. (2012). Gaze aversion as a cognitive load management strategy in autism spectrum disorder and Williams syndrome. *Journal of Child Psychology and Psychiatry, 53*(4), 420-430. https://doi.org/10.1111/j.1469-7610.2011.02481.x [Full]
Ehrlichman, H., & Micic, D. (2012). Why do people move their eyes when they think? *Current Directions in Psychological Science, 21*(2), 96-100. https://doi.org/10.1177/0963721412436810 [Abstract]
Frazier, T. W., et al. (2017). A meta-analysis of gaze differences to social and nonsocial information between individuals with and without autism. *JAACAP, 56*(7), 546-555. https://doi.org/10.1016/j.jaac.2017.05.005 [Abstract]
Fullwood, C., & Doherty-Sneddon, G. (2006). Effect of gazing at the camera during a video link on recall. *Applied Ergonomics, 37*(2), 167-175. https://doi.org/10.1016/j.apergo.2005.05.003 [Abstract]
Glenberg, A. M., Schroeder, J. L., & Robertson, D. A. (1998). Averting the gaze disengages the environment and facilitates remembering. *Memory & Cognition, 26*(4), 651-658. https://doi.org/10.3758/BF03211385 [Abstract]
Günther, V., Kropidlowski, A., Schmidt, F. M., Koelkebeck, K., Kersting, A., & Suslow, T. (2021). Attentional processes during emotional face perception in social anxiety disorder: A systematic review and meta-analysis of eye-tracking findings. *Progress in Neuro-Psychopharmacology and Biological Psychiatry, 111*, 110353. https://doi.org/10.1016/j.pnpbp.2021.110353 [Abstract]
Hadjikhani, N., et al. (2017). Look me in the eyes: Constraining gaze in the eye-region provokes abnormally high subcortical activation in autism. *Scientific Reports, 7*, 3163. https://doi.org/10.1038/s41598-017-03378-5 [Full]
Helminen, T. M., Kaasinen, S. M., & Hietanen, J. K. (2011). Eye contact and arousal: The effects of stimulus duration. *Biological Psychology, 88*(1), 124-130. https://doi.org/10.1016/j.biopsycho.2011.07.002 [Abstract]
Hessels, R. S. (2020). How does gaze to faces support face-to-face interaction? A review and perspective. *Psychonomic Bulletin & Review, 27*(5), 856-881. https://doi.org/10.3758/s13423-020-01715-w [Full, first part]
Hietanen, J. K. (2018). Affective eye contact: An integrative review. *Frontiers in Psychology, 9*, 1587. https://doi.org/10.3389/fpsyg.2018.01587 [Full, first part]
Kajimura, S., & Nomura, M. (2016). When we cannot speak: Eye contact disrupts resources available to cognitive control processes during verb generation. *Cognition, 157*, 352-357. https://doi.org/10.1016/j.cognition.2016.10.002 [Abstract]
Kendon, A. (1967). Some functions of gaze-direction in social interaction. *Acta Psychologica, 26*, 22-63. https://doi.org/10.1016/0001-6918(67)90005-4 [Record; quoted as cited in Hessels, 2020]
Mansour, H., & Kuhn, G. (2019). Studying "natural" eye movements in an "unnatural" social environment. *Quarterly Journal of Experimental Psychology, 72*(8), 1913-1925. https://doi.org/10.1177/1747021818819094 [Full]
Markson, L., & Paterson, K. B. (2009). Effects of gaze-aversion on visual-spatial imagination. *British Journal of Psychology, 100*(3), 553-563. https://doi.org/10.1348/000712608X371762 [Abstract]
McCarthy, A., Lee, K., Itakura, S., & Muir, D. W. (2006). Cultural display rules drive eye gaze during thinking. *Journal of Cross-Cultural Psychology, 37*(6), 717-722. https://doi.org/10.1177/0022022106292079 [Full]
McCarthy, A., Lee, K., Itakura, S., & Muir, D. W. (2008). Gaze display when thinking depends on culture and context. *Journal of Cross-Cultural Psychology, 39*(6), 716-729. https://doi.org/10.1177/0022022108323807 [Abstract]
Nemeth, D., Turcsik, A. B., Farkas, G., & Janacsek, K. (2013). Social communication impairs working-memory performance. *Applied Neuropsychology: Adult, 20*(3), 211-214. https://doi.org/10.1080/09084282.2012.685134 [Abstract]
Phelps, F. G., Doherty-Sneddon, G., & Warnock, H. (2006). Helping children think: Gaze aversion and teaching. *British Journal of Developmental Psychology, 24*(3), 577-588. https://doi.org/10.1348/026151005X49872 [Abstract]
Rogers, S. L., Speelman, C. P., Guidetti, O., & Longmuir, M. (2018). Using dual eye tracking to uncover personal gaze patterns during social interaction. *Scientific Reports, 8*, 4271. https://doi.org/10.1038/s41598-018-22726-7 [Full]
Schneier, F. R., Rodebaugh, T. L., Blanco, C., Lewin, H., & Liebowitz, M. R. (2011). Fear and avoidance of eye contact in social anxiety disorder. *Comprehensive Psychiatry, 52*(1), 81-87. https://doi.org/10.1016/j.comppsych.2010.04.006 [Abstract]
Senju, A., & Johnson, M. H. (2009). The eye contact effect: Mechanisms and development. *Trends in Cognitive Sciences, 13*(3), 127-134. https://doi.org/10.1016/j.tics.2008.11.009 [Abstract]
Tanaka, J. W., & Sung, A. (2016). The "eye avoidance" hypothesis of autism face processing. *Journal of Autism and Developmental Disorders, 46*(5), 1538-1552. https://doi.org/10.1007/s10803-013-1976-7 [Abstract]
Trevisan, D. A., Roberts, N., Lin, C., & Birmingham, E. (2017). How do adults and teens with self-declared autism spectrum disorder experience eye contact? *PLoS ONE, 12*(11), e0188446. https://doi.org/10.1371/journal.pone.0188446 [Full]
Uono, S., & Hietanen, J. K. (2015). Eye contact perception in the West and East: A cross-cultural study. *PLoS ONE, 10*(2), e0118094. https://doi.org/10.1371/journal.pone.0118094 [Abstract]
Alpsülün, M., & Türel, Y. K. (2026). [Instructor visibility in instructional video]. *Journal of Computer Assisted Learning, 42*(2), e70201. https://doi.org/10.1002/jcal.70201 [Abstract; title not recorded in the pass]

**Attention, reminders, mind wandering.**
Abrams, R. A., & Christ, S. E. (2003). Motion onset captures attention. *Psychological Science, 14*(5), 427-432. https://doi.org/10.1111/1467-9280.01458 [Abstract]
Anderson, B. B., Vance, A., Kirwan, C. B., Jenkins, J. L., & Eargle, D. (2016). From warning to wallpaper: Why the brain habituates to security warnings and what can be done about it. *Journal of Management Information Systems, 33*(3), 713-743. https://doi.org/10.1080/07421222.2016.1243947 [Abstract]
Bailey, B. P., & Konstan, J. A. (2006). On the need for attention-aware systems. *Computers in Human Behavior, 22*(4), 685-708. https://doi.org/10.1016/j.chb.2005.12.009 [Abstract, via search]
Bailey, R., McNamara, A., Sudarsanam, N., & Grimm, C. (2009). Subtle gaze direction. *ACM Transactions on Graphics, 28*(4), 100. https://doi.org/10.1145/1559755.1559757 [Abstract, via secondary pages]
Bartram, L., Ware, C., & Calvert, T. (2003). Moticons: Detection, distraction and task. *International Journal of Human-Computer Studies, 58*(5), 515-545. https://doi.org/10.1016/S1071-5819(03)00021-1 [Full]
D'Mello, S., Olney, A., Williams, C., & Hays, P. (2012). Gaze tutor: A gaze-reactive intelligent tutoring system. *International Journal of Human-Computer Studies, 70*(5), 377-398. https://doi.org/10.1016/j.ijhcs.2012.01.004 [Abstract]
Eysenck, M. W., Derakshan, N., Santos, R., & Calvo, M. G. (2007). Anxiety and cognitive performance: Attentional control theory. *Emotion, 7*(2), 336-353. https://doi.org/10.1037/1528-3542.7.2.336 [Abstract]
Grogorick, S., Albuquerque, G., Tauscher, J.-P., & Magnor, M. (2018). Comparison of unobtrusive visual guidance methods in an immersive dome environment. *ACM Transactions on Applied Perception, 15*(4). https://doi.org/10.1145/3238303 [Abstract]
Hata, H., Koike, H., & Sato, Y. (2016). Visual guidance with unnoticed blur effect. *AVI '16*, 28-35. https://doi.org/10.1145/2909132.2909254 [Abstract-level]
Hutt, S., Krasich, K., Brockmole, J. R., & D'Mello, S. K. (2021). Breaking out of the lab: Mitigating mind wandering with gaze-based attention-aware technology in classrooms. *CHI '21*. https://doi.org/10.1145/3411764.3445269 [Full]
Iqbal, S. T., & Bailey, B. P. (2005). Investigating the effectiveness of mental workload as a predictor of opportune moments for interruption. *CHI EA '05*, 1489-1492. https://doi.org/10.1145/1056808.1056948 [Abstract, via search]
Miller, J. (1989). The control of attention by abrupt visual onsets and offsets. *Perception & Psychophysics, 45*(6), 567-571. https://doi.org/10.3758/BF03208064 [Abstract]
Mrazek, M. D., Smallwood, J., Franklin, M. S., Chin, J. M., Baird, B., & Schooler, J. W. (2012). The role of mind-wandering in measurements of general aptitude. *Journal of Experimental Psychology: General, 141*(4), 788-798. https://doi.org/10.1037/a0027968 [Abstract]
Randall, J. G., Oswald, F. L., & Beier, M. E. (2014). Mind-wandering, cognition, and performance: A theory-driven meta-analysis of attention regulation. *Psychological Bulletin, 140*(6), 1411-1431. https://doi.org/10.1037/a0037428 [Abstract]
Szafir, D., & Mutlu, B. (2012). Pay attention! Designing adaptive agents that monitor and improve user engagement. *CHI '12*, 11-20. https://doi.org/10.1145/2207676.2207679 [Full]
Szpunar, K. K., Khan, N. Y., & Schacter, D. L. (2013). Interpolated memory tests reduce mind wandering and improve learning of online lectures. *PNAS, 110*(16), 6313-6317. https://doi.org/10.1073/pnas.1221764110 [Abstract]
Theeuwes, J. (1991). Exogenous and endogenous control of attention: The effect of visual onsets and offsets. *Perception & Psychophysics, 49*(1), 83-90. https://doi.org/10.3758/BF03211619 [Abstract]
Theeuwes, J. (2010). Top-down and bottom-up control of visual selection. *Acta Psychologica, 135*(2), 77-99. https://doi.org/10.1016/j.actpsy.2010.02.006 [Abstract]
Warm, J. S., Parasuraman, R., & Matthews, G. (2008). Vigilance requires hard mental work and is stressful. *Human Factors, 50*(3), 433-441. https://doi.org/10.1518/001872008X312152 [Abstract]
Weißgerber, S. C., Brunmair, M., & Rummer, R. (2021). Null and void? Errors in meta-analysis on perceptual disfluency and recommendations to improve meta-analytical reproducibility. *Educational Psychology Review, 33*. https://doi.org/10.1007/s10648-020-09579-1 [Abstract, via search]
Xie, H., Zhou, Z., & Liu, Q. (2018). Null effects of perceptual disfluency on learning outcomes in a text-based educational context: A meta-analysis. *Educational Psychology Review, 30*, 745-771. https://doi.org/10.1007/s10648-018-9442-x [Abstract, via search]
Yantis, S., & Jonides, J. (1990). Abrupt visual onsets and selective attention: Voluntary versus automatic allocation. *JEP: Human Perception and Performance, 16*(1), 121-134. https://doi.org/10.1037/0096-1523.16.1.121 [Abstract]
Zanesco, A. P., Denkova, E., & Jha, A. P. (2025). Mind-wandering increases in frequency over time during task performance: An individual-participant meta-analytic review. *Psychological Bulletin, 151*(2), 217-239. https://doi.org/10.1037/bul0000424 [Abstract]
W3C. (2023). *Understanding Success Criterion 2.3.1: Three Flashes or Below Threshold* (WCAG 2.2). https://www.w3.org/WAI/WCAG22/Understanding/three-flashes-or-below-threshold.html [Full]

**Agents and avatars.**
Bavelas, J. B., Coates, L., & Johnson, T. (2000). Listeners as co-narrators. *Journal of Personality and Social Psychology, 79*(6), 941-952. https://doi.org/10.1037/0022-3514.79.6.941 [Abstract]
Castro-Alonso, J. C., Wong, R. M., Adesope, O. O., & Paas, F. (2021). Effectiveness of multimedia pedagogical agents predicted by diverse theories: A meta-analysis. *Educational Psychology Review, 33*(3), 989-1015. https://doi.org/10.1007/s10648-020-09587-1 [Abstract]
Chittaro, L., & Serafini, M. (2023). Desktop virtual reality as an exposure method for test anxiety. *Multimedia Tools and Applications*. https://doi.org/10.1007/s11042-023-16917-2 [Abstract]
Dai, C.-P., Ke, F., Pan, Y., Moon, J., & Liu, Z. (2024). Effects of artificial intelligence-powered virtual agents on learning outcomes in computer-based simulations: A meta-analysis. *Educational Psychology Review, 36*(1), 31. https://doi.org/10.1007/s10648-024-09855-4 [Abstract]
Davis, R. O. (2018). The impact of pedagogical agent gesturing in multimedia learning environments: A meta-analysis. *Educational Research Review, 24*, 193-209. https://doi.org/10.1016/j.edurev.2018.05.002 [Abstract]
Dear, K., Dutton, K., & Fox, E. (2019). Do 'watching eyes' influence antisocial behavior? A systematic review & meta-analysis. *Evolution and Human Behavior, 40*(3), 269-280. https://doi.org/10.1016/j.evolhumbehav.2019.01.006 [Abstract]
Diel, A., Weigelt, S., & MacDorman, K. F. (2022). A meta-analysis of the uncanny valley's independent and dependent variables. *ACM Transactions on Human-Robot Interaction, 11*(1), 1-33. https://doi.org/10.1145/3470742 [Abstract; effect sizes from repository snippets]
Gonzales, D. M., Kalamkar, S., Jörg, S., & Grubert, J. (2025). Behavioral and symbolic fillers as delay mitigation for embodied conversational agents in virtual reality [Preprint, accepted to IEEE TVCG]. arXiv:2508.11781 [Abstract]
Gratch, J., et al. (2006). Virtual rapport. In *Intelligent Virtual Agents (IVA 2006)*, LNCS 4133. https://doi.org/10.1007/11821830_2 [Record]
Kätsyri, J., Förger, K., Mäkäräinen, M., & Takala, T. (2015). A review of empirical evidence on different uncanny valley hypotheses. *Frontiers in Psychology, 6*, 390. https://doi.org/10.3389/fpsyg.2015.00390 [Abstract]
Kleisner, K., Priplatova, L., Frost, P., & Flegr, J. (2013). Trustworthy-looking face meets brown eyes. *PLoS ONE, 8*(1), e53285. https://doi.org/10.1371/journal.pone.0053285 [Abstract]
Kobayashi, H., & Kohshima, S. (2001). Unique morphology of the human eye and its adaptive meaning. *Journal of Human Evolution, 40*(5), 419-435. https://doi.org/10.1006/jhev.2001.0468 [Abstract]
Lucas, G. M., Gratch, J., King, A., & Morency, L.-P. (2014). It's only a computer: Virtual humans increase willingness to disclose. *Computers in Human Behavior, 37*, 94-100. https://doi.org/10.1016/j.chb.2014.04.043 [Abstract]
MacDorman, K. F., Green, R. D., Ho, C.-C., & Koch, C. T. (2009). Too real for comfort? Uncanny responses to computer generated faces. *Computers in Human Behavior, 25*(3), 695-710. https://doi.org/10.1016/j.chb.2008.12.026 [Abstract]
Maslych, M., et al. (2025). Mitigating response delays in free-form conversations with LLM-powered intelligent virtual agents. *CUI '25*. arXiv:2507.22352 [Full, most]
Mathur, M. B., & Reichling, D. B. (2016). Navigating a social world with robot partners: A quantitative cartography of the Uncanny Valley. *Cognition, 146*, 22-32. https://doi.org/10.1016/j.cognition.2015.09.008 [Abstract]
Northover, S. B., Pedersen, W. C., Cohen, A. B., & Andrews, P. W. (2017). Artificial surveillance cues do not increase generosity: Two meta-analyses. *Evolution and Human Behavior, 38*(1), 144-153. https://doi.org/10.1016/j.evolhumbehav.2016.07.001 [Abstract]
Oh, C. S., Bailenson, J. N., & Welch, G. F. (2018). A systematic review of social presence: Definition, antecedents, and implications. *Frontiers in Robotics and AI, 5*, 114. https://doi.org/10.3389/frobt.2018.00114 [Full, partly]
Seyama, J., & Nagayama, R. S. (2007). The uncanny valley: Effect of realism on the impression of artificial human faces. *Presence, 16*(4), 337-351. https://doi.org/10.1162/pres.16.4.337 [Abstract]
Stivers, T., et al. (2009). Universals and cultural variation in turn-taking in conversation. *PNAS, 106*(26), 10587-10592. https://doi.org/10.1073/pnas.0903616106 [Abstract]
Vallefuoco, E., Varni, G., & Zancanaro, M. (2025). Almost the same: Experiential and behavioral effects in talking with virtual humans. *Virtual Reality*. https://doi.org/10.1007/s10055-025-01166-5 [Abstract]
Yorzinski, J. L., Thorstenson, C. A., & Nguyen, T. P. (2021). Sclera and iris color interact to influence gaze perception. *Frontiers in Psychology, 12*, 632616. https://doi.org/10.3389/fpsyg.2021.632616 [Full]
Zell, E., et al. (2015). To stylize or not to stylize? The effect of shape and material stylization on the perception of computer-generated faces. *ACM Transactions on Graphics, 34*(6), 184. https://doi.org/10.1145/2816795.2818126 [Abstract]

**Self-view, proctoring, anxiety, sound.**
Bailenson, J. N. (2021). Nonverbal overload: A theoretical argument for the causes of Zoom fatigue. *Technology, Mind, and Behavior, 2*(1). https://doi.org/10.1037/tmb0000030 [Full]
Belletier, C., et al. (2015). Choking under monitoring pressure: Being watched by the experimenter reduces executive attention. *Psychonomic Bulletin & Review, 22*(5), 1410-1416. https://doi.org/10.3758/s13423-015-0804-9 [Abstract]
Bennett, A. A., Campion, E. D., Keeler, K. R., & Keener, S. K. (2021). Videoconference fatigue? Exploring changes in fatigue after videoconference meetings during COVID-19. *Journal of Applied Psychology, 106*(3), 330-344. https://doi.org/10.1037/apl0000906 [Full]
Bergmans, L., Bouali, N., Luttikhuis, M., & Rensink, A. (2021). On the efficacy of online proctoring using Proctorio. *CSEDU 2021*, 279-290. https://www.scitepress.org/Papers/2021/103996 [Abstract]
CNIL. (2023). *Délibération n° 2023-058 du 8 juin 2023 portant adoption d'une recommandation relative aux dispositifs de surveillance à distance des examens*. https://www.cnil.fr [Full]
Conijn, R., Kleingeld, A., Matzat, U., & Snijders, C. (2022). The fear of big brother: The potential negative side-effects of proctored exams. *Journal of Computer Assisted Learning, 38*(6), 1521-1534. https://doi.org/10.1111/jcal.12651 [Abstract]
Ellermeier, W., & Zimmer, K. (2014). The psychoacoustics of the irrelevant sound effect. *Acoustical Science and Technology, 35*(1), 10-16. https://doi.org/10.1250/ast.35.10 [Abstract]
Fauville, G., Luo, M., Queiroz, A. C. M., Lee, A., Bailenson, J. N., & Hancock, J. (2023). Video-conferencing usage dynamics and nonverbal mechanisms exacerbate Zoom Fatigue, particularly for women. *Computers in Human Behavior Reports, 10*, 100271. https://doi.org/10.1016/j.chbr.2023.100271 [Full]
Garante per la protezione dei dati personali. (2021). *Ordinanza ingiunzione nei confronti di Università Commerciale "Luigi Bocconi" di Milano*, provv. n. 317 of 16 September 2021, doc. web 9703988. https://www.garanteprivacy.it [Full, most]
Huxham, M., Campbell, F., & Westwood, J. (2012). Oral versus written assessments: A test of student performance and attitudes. *Assessment & Evaluation in Higher Education, 37*(1), 125-136. https://doi.org/10.1080/02602938.2010.515012 [Abstract]
Kharbat, F. F., & Abu Daabes, A. S. (2021). E-proctored exams during the COVID-19 pandemic: A close understanding. *Education and Information Technologies, 26*, 6589-6605. https://doi.org/10.1007/s10639-021-10458-7 [Full]
Lima, L. M., Favarato, M. H., & Tibério, I. F. L. C. (2026). Empowering medical students: Peer-led OSCE reduces anxiety. *PLoS ONE, 21*(2), e0340407. https://doi.org/10.1371/journal.pone.0340407 [Abstract]
Ogletree v. Cleveland State University, No. 1:21-cv-00500 (N.D. Ohio Aug. 22, 2022) [Full]
Ramirez, G., & Beilock, S. L. (2011). Writing about testing worries boosts exam performance in the classroom. *Science, 331*(6014), 211-213. https://doi.org/10.1126/science.1199427 [Abstract]
Ratan, R., Miller, D. B., & Bailenson, J. N. (2022). Facial appearance dissatisfaction explains differences in Zoom fatigue. *Cyberpsychology, Behavior, and Social Networking, 25*(2), 124-129. https://doi.org/10.1089/cyber.2021.0112 [Full]
Regulation (EU) 2016/679 (General Data Protection Regulation), Art. 22. [Full]
Regulation (EU) 2024/1689 (Artificial Intelligence Act), Annex III point 3(d), Art. 86. [Full; Art. 14 not opened in this pass]
Savage, L. R., & Couture Bue, A. C. (2023). Objectifying the classroom [full title not recorded in this pass]. *Body Image, 46*, 313-323. https://doi.org/10.1016/j.bodyim.2023.06.009 [Abstract]
Shockley, K. M., et al. (2021). The fatiguing effects of camera use in virtual meetings: A within-person field experiment. *Journal of Applied Psychology, 106*(8), 1137-1155. https://doi.org/10.1037/apl0000948 [Abstract]
Steger, D., Schroeders, U., & Gnambs, T. (2020). A meta-analysis of test scores in proctored and unproctored ability assessments. *European Journal of Psychological Assessment, 36*(1), 174-184. https://doi.org/10.1027/1015-5759/a000494 [Abstract]
Szalma, J. L., & Hancock, P. A. (2011). Noise effects on human performance: A meta-analytic synthesis. *Psychological Bulletin, 137*(4), 682-707. https://doi.org/10.1037/a0023685 [Full]
Tien, I. S., Imundo, M. N., & Bjork, E. L. (2023). Viewing oneself during synchronous online learning increases appearance anxiety and decreases memory for lecture content. *Applied Cognitive Psychology, 37*, 443-451. https://doi.org/10.1002/acp.4048 [Abstract]
UK Equality Act 2010, s. 20(3). https://www.legislation.gov.uk/ukpga/2010/15/section/20 [Full]
von der Embse, N., Jester, D., Roy, D., & Post, J. (2018). Test anxiety effects, predictors, and correlates: A 30-year meta-analytic review. *Journal of Affective Disorders, 227*, 483-493. https://doi.org/10.1016/j.jad.2017.11.048 [Abstract]
Xu, J., Whelan, E., O'Brien, A., & O'Hora, D. (2024). Does self-view mode generate more videoconferencing fatigue in women than men? An experiment using EEG signals. *Cyberpsychology, Behavior, and Social Networking, 27*(6), 426-430. https://doi.org/10.1089/cyber.2023.0577 [Abstract]
