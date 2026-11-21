# MASTER PRODUCT DEVELOPMENT PROMPT

## AI-Powered Socratic Knowledge Assessment & Human-AI Grading Platform

> This is the owner's master plan (adopted 2026-11-20, GAD-T79). It is the priority for Gadfly. Where earlier
> plans ([SOCRATIC_PLATFORM.md](SOCRATIC_PLATFORM.md)) differ, this document wins. How the existing build maps to
> it, and the order the remaining work is done in, is in [MASTER-PLAN-STATUS.md](MASTER-PLAN-STATUS.md).

## Your Role

Act as a team of experts consisting of:

- Senior AI Systems Architect
- Educational Assessment and Psychometrics Expert
- Socratic Teaching Method Specialist
- Full-Stack Software Engineer
- Machine Learning Engineer
- UX/UI Designer
- Database Architect
- Education Technology Product Manager
- Information Security and Student Privacy Specialist

Your mission is to design and build a commercial-grade AI-powered educational assessment platform for schools, universities, professional certification institutions, and corporate training organizations.

This platform must fundamentally rethink how human knowledge and understanding are evaluated in the age of generative AI.

Do not build a conventional quiz application or a generic chatbot.

Build an intelligent assessment system that evaluates whether a student genuinely understands a subject through adaptive Socratic dialogue, verified response records, evidence-based grading, and human oversight.

## PART 1 — PRODUCT VISION

### 1.1 The Problem

Traditional educational assessments primarily evaluate students through:

- Multiple-choice questions
- Written examinations
- Memorized definitions
- Standardized questions
- Static problem-solving exercises
- Essays and assignments

These methods can measure important skills, but they may not consistently distinguish genuine conceptual understanding from memorization, rehearsed explanations, or AI-generated answers.

With generative AI becoming widely accessible, the ability to produce a correct answer no longer necessarily demonstrates independent understanding.

Educational institutions need additional assessment methods that evaluate how students think, reason, explain, and apply knowledge.

### 1.2 The Solution

Create an AI-powered assessment platform based on the Socratic Method.

Instead of simply checking whether a student provides the correct answer, the platform should continuously investigate:

- Why does the student believe the answer is correct?
- Can the student explain the underlying principles?
- Can the student defend their reasoning?
- Can the student recognize incorrect assumptions?
- Can the student apply the same knowledge in a different context?
- Can the student identify contradictions?
- Can the student distinguish what they know from what they do not know?

The system should adapt its questions dynamically according to the student's responses.

It should collect evidence of understanding, rather than relying exclusively on final answers.

#### Core Product Philosophy

Knowing the correct answer is not the same as understanding why it is correct.

The platform must be designed to evaluate that difference.

### 1.3 Commercial Vision

The long-term goal is to provide the platform as a scalable SaaS solution to:

- K–12 educational institutions
- Colleges and universities
- Online learning providers
- Professional certification organizations
- Corporate training departments
- Continuing education institutions

The product should support institutional subscriptions, per-student licensing, assessment-based pricing, and future API integrations.

## PART 2 — SOCRATIC AI EXAMINATION ENGINE

### 2.1 Core Examination Behavior

The AI must behave like an intelligent academic examiner, not a conventional chatbot.

The examination should consist of an adaptive conversation between the AI and the student.

The AI asks an initial question and analyzes the response.

Based on that analysis, the AI selects the next question that is most useful for determining the student's understanding.

The questioning process should investigate knowledge progressively.

The AI must not automatically assume that a correct initial answer demonstrates genuine understanding.

Likewise, an incorrect initial answer should not automatically establish that the student lacks understanding.

The AI must gather sufficient evidence before making an assessment.

### 2.2 Example Examination

Subject: Mathematics

Topic: Multiplication

> **AI Examiner:** What is the relationship between multiplication and addition?
>
> **Student:** Multiplication is repeated addition.
>
> **AI Examiner:** Can you explain what multiplication represents using an example?
>
> **Student:** Three bags with four apples each means four plus four plus four.
>
> **AI Examiner:** What does the number three represent in your example?
>
> **Student:** The number of bags.
>
> **AI Examiner:** What does the number four represent?
>
> **Student:** The number of apples in each bag.
>
> **AI Examiner:** Suppose the number of bags doubles while the number of apples per bag stays the same. What happens to the total number of apples, and why?

The AI should continue probing the student's understanding through appropriate examples, applications, and variations.

Questions must be mathematically and conceptually valid.

The system should distinguish between elementary multiplication as equal groups and more advanced interpretations of multiplication, such as scaling and proportional relationships.

### 2.3 Adaptive Questioning Strategies

Implement the following questioning strategies.

**A. Conceptual Probing** — Investigate whether the student understands the underlying concept.
Example: "Can you explain why that relationship exists?"

**B. Causal Reasoning** — Determine whether the student understands cause-and-effect relationships where applicable.
Example: "What causes this outcome?"

**C. Counterexample Testing** — Present examples that challenge incomplete explanations.
Example: "Would your explanation still hold in this situation?"

**D. Scenario Variation** — Change relevant conditions to determine whether the student can reason consistently.
Example: "What changes if we double one variable while keeping the others constant?"

**E. Knowledge Transfer** — Introduce unfamiliar contexts requiring the same underlying concept.
Example: "How would you apply this principle to a different problem?"

**F. Contradiction Detection** — Identify inconsistencies between responses.
Example: "Earlier, you explained the relationship one way. How does that fit with your current explanation?"

**G. Assumption Testing** — Investigate the assumptions underlying a conclusion.
Example: "What assumptions are necessary for your explanation to hold?"

**H. Independent Explanation** — Ask students to reconstruct explanations using their own reasoning.
Example: "Can you explain this concept without repeating the textbook definition?"

### 2.4 Adaptive Question Selection

Do not simply generate random follow-up questions.

Develop a question-selection mechanism that considers:

- Learning objectives
- Current evidence of understanding
- Identified misconceptions
- Unresolved uncertainties
- Previous questions
- Difficulty level
- Remaining examination time
- Information value of potential follow-up questions

Prioritize questions that help distinguish competing explanations of the student's knowledge.

Avoid unnecessary repetition.

The examination should continue until sufficient evidence is collected or a configured stopping condition is reached.

## PART 3 — ASSESSMENT MODE AND LEARNING MODE

The platform must provide two separate operating modes.

### 3.1 Examination Mode

This mode is designed for formal assessments.

The AI must:

- Remain academically neutral.
- Avoid revealing correct answers.
- Avoid leading students toward a solution.
- Ask questions that reveal independently demonstrated knowledge.
- Evaluate responses against predefined learning objectives.
- Record assessment evidence.
- Identify uncertainty.
- Follow established examination rules.
- Stop when appropriate evidence or configured limits are reached.

#### Critical Rule

The AI must not teach students during formal examinations unless explicitly permitted by the institution.

The AI should distinguish between asking a neutral clarification question and providing instructional assistance.

### 3.2 Learning Mode

This mode is designed for tutoring and conceptual development.

The AI should:

- Identify misconceptions.
- Provide guided explanations.
- Offer hints.
- Introduce educational examples.
- Encourage independent reasoning.
- Help students discover mistakes.
- Reassess understanding after instruction.

Knowledge demonstrated after instructional assistance must be identified separately from independently demonstrated examination knowledge.

#### Fundamental Distinction

Examination Mode evaluates understanding.

Learning Mode develops understanding.

The platform must not mix evidence between these modes without explicit institutional rules.

## PART 4 — ACCURATE TRANSCRIPT AND RESPONSE VERIFICATION ENGINE

### 4.1 Core Principle

A student must never receive an inaccurate grade because the AI misunderstood, mistranscribed, or incorrectly interpreted their response.

Accurate recording and verification of student responses are fundamental requirements.

The platform must maintain a complete and auditable examination transcript.

### 4.2 Transcript Requirements

Every examination interaction must record:

- Examination session ID
- Student ID
- Learning objective
- Question ID
- Exact AI question
- Student's original response
- Response modality
- Timestamp
- Speech recognition output, if applicable
- AI interpretation, when applicable
- Clarification requests
- Student confirmation or correction
- Final verified response
- Assessment evidence
- Verification status

The original response must be preserved.

The AI must never silently rewrite or overwrite a student's original answer.

Corrections and interpretations must be stored separately.

### 4.3 Two Types of Verification

The system must distinguish between two different verification processes.

#### Type A: Transcription Verification

Determine whether the recorded text accurately represents what the student said.

This is especially important for voice examinations.

Example:

Student speaks:

"The coefficient changes depending on another variable."

AI displays:

I recorded your answer as:

"The coefficient changes depending on another variable."

Is this what you said?

Options:

- Yes, this is correct.
- No, I want to correct it.

If the student rejects the transcript, they must be allowed to correct the text or repeat the response.

The system must preserve the original and corrected versions.

#### Type B: Meaning Verification

Determine whether the AI correctly understands the intended meaning of the student's response.

Example:

Student:

"I think these variables influence each other."

AI:

"You said the variables influence each other. Can you explain what you mean by that?"

The AI should allow the student to clarify independently.

It must not introduce a correct technical explanation and ask the student to confirm it.

### 4.4 Avoid Leading Confirmation Questions

The AI must not accidentally teach students the correct answer while verifying their responses.

#### Prohibited Example

Student:

"The two variables affect each other."

AI:

"Do you mean that the effect of one independent variable depends on the value of another independent variable?"

This may reveal the conceptual answer.

#### Acceptable Example

AI:

"When you say the variables affect each other, what relationship are you describing?"

The student must provide the explanation independently.

### 4.5 Confidence-Based Verification

Implement a response verification mechanism that detects when clarification is necessary.

Potential triggers include:

- Low speech recognition confidence
- Unrecognized technical terminology
- Multiple plausible interpretations
- Incomplete sentences
- Audio quality problems
- Ambiguous references
- Potential transcription errors
- Student requests for correction

Do not interrupt students unnecessarily.

Verification should occur when uncertainty could materially affect grading.

### 4.6 Transcript Verification Status

Each response should have an explicit verification state.

Recommended states:

- Captured
- Verification Required
- Awaiting Student Confirmation
- Confirmed
- Corrected
- Clarified
- Unresolved
- Flagged for Human Review

Distinguish technical transcript verification from conceptual understanding.

Student confirmation that a transcript is accurate must never be interpreted as confirmation that the academic answer is correct.

### 4.7 Correction Integrity

The system must distinguish between:

- Correcting an inaccurate transcription.
- Clarifying an ambiguous meaning.
- Changing the substance of an original answer.

These actions have different assessment implications.

A legitimate transcription correction should not reduce the student's score.

A substantive answer revision must be recorded as a new response event and evaluated according to the institution's examination rules.

Do not silently replace earlier assessment evidence.

## PART 5 — KNOWLEDGE EVALUATION FRAMEWORK

### 5.1 Multidimensional Assessment

Evaluate understanding across the following dimensions.

| Dimension | Assessment Purpose |
|---|---|
| Factual Knowledge | Accuracy of relevant facts |
| Conceptual Understanding | Understanding of underlying principles |
| Logical Reasoning | Ability to construct valid explanations |
| Application | Ability to use knowledge in practical situations |
| Knowledge Transfer | Ability to apply knowledge in unfamiliar contexts |
| Misconception Recognition | Ability to recognize incorrect assumptions |
| Consistency | Coherence across related responses |
| Metacognition | Awareness of knowledge limitations |

The platform should support subject-specific assessment dimensions.

Not every dimension must carry equal weight.

Educators must be able to configure assessment rubrics.

### 5.2 Evidence-Based Evaluation

Scores must be supported by actual student responses.

For each learning objective, record:

- Relevant questions
- Student answers
- Supporting evidence
- Evidence contradicting demonstrated mastery
- Misconceptions
- Scoring rubric
- Preliminary evaluation
- Remaining uncertainty

The AI must never fabricate evidence.

### 5.3 Understanding Levels

Support a configurable mastery framework.

Example:

- **Level 0 — Insufficient Evidence:** The examination does not provide enough reliable evidence to determine understanding.
- **Level 1 — Recognition:** The student recognizes relevant concepts but cannot reliably explain them.
- **Level 2 — Basic Explanation:** The student explains familiar concepts with partial accuracy.
- **Level 3 — Conceptual Understanding:** The student provides accurate explanations and demonstrates an understanding of underlying relationships.
- **Level 4 — Application:** The student applies concepts to relevant problems and explains the reasoning.
- **Level 5 — Transfer and Critical Reasoning:** The student applies knowledge to unfamiliar situations, evaluates assumptions, and defends conclusions.

These levels must be operationalized through subject-specific rubrics.

Insufficient evidence must not automatically be treated as zero understanding.

### 5.4 Assessment Confidence

The system should report uncertainty separately from the assigned score.

Do not produce arbitrary confidence percentages.

Confidence measures must be based on defined evidence criteria and validated methods.

If sufficient evidence is unavailable, the system should request another appropriate question, flag the objective for review, or mark the assessment as inconclusive.

## PART 6 — AI EXAMINATION CONTROL AND STOPPING ENGINE

### 6.1 Purpose

The system must determine when enough evidence has been gathered to evaluate a learning objective.

The AI should not continue asking questions indefinitely.

### 6.2 Stopping Criteria

Consider:

- Coverage of required learning objectives
- Quantity and quality of evidence
- Consistency of demonstrated reasoning
- Successful application to unfamiliar scenarios
- Remaining uncertainty
- Maximum question count
- Examination time limits
- Student accessibility accommodations

### 6.3 Avoid Premature Conclusions

One correct answer should not automatically establish mastery.

One incorrect answer should not automatically establish a lack of understanding.

The AI should seek corroborating evidence.

### 6.4 Inconclusive Assessments

If the system cannot reliably determine understanding within the permitted examination limits, it must report:

**Insufficient Evidence for Reliable Assessment**

The platform should then follow an institution-defined procedure, such as human review or additional examination.

## PART 7 — AI PRELIMINARY GRADING ENGINE

### 7.1 AI Scoring Responsibilities

At the end of the examination, the AI generates preliminary scores.

The AI must evaluate each learning objective using an established rubric.

Each score should include:

- Learning objective
- Maximum score
- Assigned AI score
- Evidence references
- Scoring explanation
- Identified misconceptions
- Unresolved ambiguities
- Review flags

### 7.2 Example AI Evaluation

Subject: Statistics

Learning Objective: Understand interaction effects in regression.

Maximum Score: 20

AI Score: 15

Evidence:

The student correctly explained that the relationship between an independent variable and the outcome can depend on another variable.

However, the student demonstrated difficulty interpreting interaction coefficients in an unfamiliar scenario.

Evaluation:

Partial understanding demonstrated.

Review Status:

Pending Human Evaluation.

### 7.3 AI Scoring Rules

The AI must:

- Follow the approved rubric.
- Use verified evidence.
- Explain scoring decisions.
- Identify insufficient evidence.
- Flag unresolved transcript ambiguity.
- Avoid unsupported conclusions.
- Maintain consistency across examinations.
- Support later auditing and recalculation.

AI-generated grades must remain preliminary until the required human review process is completed.

## PART 8 — MANDATORY HUMAN GRADING ENGINE

### 8.1 Human Oversight Requirement

The AI must not independently issue the final academic grade.

A qualified human examiner must participate in the grading process.

The platform should provide a dedicated human grading dashboard.

### 8.2 Human Reviewer Interface

The reviewer must be able to access:

- Complete examination transcript
- Verified responses
- Original audio, where available and authorized
- Clarification history
- Student corrections
- Learning objectives
- Approved scoring rubrics
- Relevant assessment evidence
- AI-generated scores
- AI explanations
- Unresolved issues

### 8.3 Independent Human Evaluation

Support a blind grading option.

In this mode:

1. The human examiner reviews the transcript.
2. The human examiner assigns scores independently.
3. The human submits their evaluation.
4. The system reveals the AI's preliminary evaluation.
5. The system compares the two assessments.

This design helps reduce the risk of human reviewers being influenced by AI-generated scores.

### 8.4 Human Evaluation Data

Record:

- Reviewer ID
- Learning objective
- Human score
- Supporting evidence
- Reviewer comments
- Review timestamp
- Review status
- Any subsequent score adjustments

The system must retain a complete audit history.

## PART 9 — HUMAN-AI COMBINED GRADING ENGINE

### 9.1 Weighted Grading Model

The platform must support configurable AI-human grading weights.

Example institutional policy:

- AI Weight: 40%
- Human Weight: 60%

Formula:

Final Score = (AI Score × AI Weight) + (Human Score × Human Weight)

Where the weights sum to 1.

### 9.2 Example

AI Score: 85/100

Human Score: 90/100

Calculation:

(85 × 0.40) + (90 × 0.60)

= 34 + 54

= 88

Combined Score: 88/100

### 9.3 Configurable Weighting

Institutions should be able to configure grading policies.

Examples:

| AI Weight | Human Weight |
|---|---|
| 20% | 80% |
| 30% | 70% |
| 40% | 60% |
| 50% | 50% |

Weights must be approved before an examination begins.

Changing grading policies after examination completion must require authorized approval and an audit record.

Both evaluators must use compatible scoring scales and the same approved learning objectives.

### 9.4 Per-Objective Score Calculation

Where appropriate, calculate scores individually for each learning objective before aggregating the examination result.

For example:

| Learning Objective | AI Score | Human Score | Combined Score |
|---|---|---|---|
| Conceptual Understanding | 80 | 90 | 86 |
| Logical Reasoning | 75 | 85 | 81 |
| Application | 90 | 80 | 84 |
| Knowledge Transfer | 70 | 80 | 76 |

The example uses 40% AI and 60% human weighting.

The final examination grade should also respect the institution's configured learning-objective weights.

## PART 10 — SCORING DISAGREEMENT AND RECONCILIATION ENGINE

### 10.1 Detect Significant Disagreements

The system must identify substantial differences between AI and human evaluations.

Example:

AI Score: 90

Human Score: 65

Difference: 25 points

The system should automatically flag the evaluation.

### 10.2 Configurable Thresholds

Allow institutions to configure disagreement thresholds.

For example:

- Difference below 10 points: standard review
- Difference of 10–19 points: additional review flag
- Difference of 20 points or more: mandatory reconciliation

These values are illustrative defaults, not validated educational standards.

Institutions must be able to adjust them.

### 10.3 Reconciliation Workflow

When significant disagreement occurs:

1. Display the AI and human scoring rationales.
2. Identify which rubric criteria produced different evaluations.
3. Present the supporting transcript evidence.
4. Allow the reviewer to inspect original responses.
5. Determine whether an error or ambiguity occurred.
6. Allow an authorized second reviewer if necessary.
7. Document the resolution.
8. Recalculate or approve the final score.

### 10.4 Prevent Automatic Finalization

If unresolved discrepancies exceed the institutional threshold, the platform must not automatically finalize the examination.

The result should remain:

**Pending Grade Reconciliation**

Only an authorized reviewer may resolve the disagreement.

## PART 11 — FINAL GRADE APPROVAL

### 11.1 Complete Examination Workflow

Implement the following workflow:

- **Stage 1 — Assessment Setup:** The educator creates the examination and configures learning objectives, scoring rubrics, and institutional policies.
- **Stage 2 — Student Examination:** The AI conducts adaptive Socratic questioning.
- **Stage 3 — Response Verification:** The system verifies ambiguous responses and records student corrections.
- **Stage 4 — Evidence Collection:** The system organizes verified responses into an assessment evidence record.
- **Stage 5 — AI Preliminary Grading:** The AI generates rubric-based scores.
- **Stage 6 — Independent Human Grading:** The human evaluator assigns scores.
- **Stage 7 — Score Comparison:** The system identifies agreement and disagreement between evaluations.
- **Stage 8 — Reconciliation:** Significant discrepancies are reviewed and resolved.
- **Stage 9 — Combined Score Calculation:** The system calculates the grade using approved weighting policies.
- **Stage 10 — Human Approval:** An authorized examiner approves the final result.
- **Stage 11 — Student Reporting:** The student receives the approved grade and appropriate feedback.

### 11.2 Grade Status Management

Support explicit statuses:

- Examination In Progress
- Examination Completed
- Transcript Verification Pending
- AI Grading Pending
- Human Review Pending
- Reconciliation Required
- Awaiting Final Approval
- Finalized
- Published
- Under Appeal
- Amended

Final grades must never be released without the required approval.

## PART 12 — TEACHER AND INSTITUTION DASHBOARD

### 12.1 Assessment Creation

Educators must be able to:

- Create courses.
- Define subject areas.
- Upload approved learning materials.
- Establish learning objectives.
- Create examination rubrics.
- Configure difficulty.
- Set question limits.
- Configure examination duration.
- Select assessment and learning modes.
- Configure AI-human grading weights.
- Set disagreement thresholds.
- Assign examinations.

### 12.2 Examination Review

The dashboard must allow educators to:

- Review student transcripts.
- Inspect response verification histories.
- Listen to authorized audio recordings.
- Review AI reasoning evidence and scoring explanations.
- Assign human scores.
- Reconcile grading differences.
- Approve final results.
- Review student appeals.

### 12.3 Educational Analytics

Provide analytics showing:

- Student mastery by learning objective
- Recurring misconceptions
- Class-wide knowledge gaps
- Assessment completion rates
- AI-human scoring agreement
- Average grading differences
- Frequency of transcript corrections
- Examination reliability indicators

Do not interpret raw AI-human agreement as proof of scoring validity.

## PART 13 — STUDENT EXAMINATION EXPERIENCE

### 13.1 Conversational Interface

Build a clean, accessible examination interface.

The student should see:

- Examination title
- Subject and topic
- Current AI question
- Response input
- Clarification requests
- Transcript confirmation when needed
- Examination progress
- Relevant session controls

Questions should appear one at a time.

### 13.2 Text-Based Examination

The MVP must support typed responses.

Students must be able to:

- Type explanations.
- Submit responses.
- Clarify ambiguous wording.
- Review system interpretation requests.
- Correct technical recording errors.
- Continue the examination.

### 13.3 Future Voice Examination

Design the architecture to support voice examinations.

Future capabilities should include:

- Speech recognition
- Audio recording where permitted
- Speaker identification where necessary
- Real-time transcript display
- Technical terminology recognition
- Transcript correction
- Voice-based Socratic questioning

Voice interaction should be an optional feature rather than a requirement for the MVP.

## PART 14 — KNOWLEDGE MANAGEMENT AND COURSE CONTENT

### 14.1 Course Material Ingestion

Educators should be able to upload:

- Textbooks
- Lecture notes
- PDF documents
- Course outlines
- Study guides
- Learning objectives
- Assessment rubrics

### 14.2 Source-Grounded Question Generation

Use approved course materials to ground examination questions.

The system must:

- Identify relevant learning objectives.
- Retrieve supporting content.
- Generate questions aligned with approved materials.
- Avoid unsupported factual claims.
- Detect conflicts between sources where possible.
- Preserve references to the source material.

### 14.3 Retrieval-Augmented Generation

Implement a retrieval-augmented generation architecture where appropriate.

The AI should retrieve relevant educational content before generating or evaluating subject-specific questions.

The system must distinguish institutional source material from student-submitted content.

Student answers must never be treated as authoritative source documents.

## PART 15 — FAIRNESS, RELIABILITY, AND ASSESSMENT VALIDITY

### 15.1 Fairness

The platform must not unfairly penalize students for:

- Accent
- Non-native language fluency when not being assessed
- Speech recognition errors
- Communication differences unrelated to the learning objective
- Accessibility accommodations

The system should distinguish subject knowledge from the student's ability to express that knowledge.

### 15.2 Comparable Assessment Difficulty

Because students may receive different questions, the system must support methods for maintaining comparable assessment standards.

Use:

- Common learning objectives
- Standardized rubrics
- Calibrated question difficulty where possible
- Shared assessment anchors
- Educator-reviewed question sets
- Periodic evaluation of grading consistency

Adaptive questioning must not lead to arbitrary differences in grading difficulty.

### 15.3 Scoring Validation

Before use in high-stakes examinations, evaluate the system through structured studies.

Recommended validation activities include:

- Comparison against qualified human examiners
- Inter-rater agreement analysis
- Repeated scoring consistency tests
- Bias and subgroup performance analysis
- Question difficulty calibration
- Transcript verification accuracy testing
- Student response interpretation testing
- Review of false mastery and false deficiency classifications

Do not claim that an AI-generated score is objectively accurate without evidence.

### 15.4 Academic Integrity

The platform should support academic integrity without claiming to eliminate cheating.

Potential safeguards include:

- Institution-approved identity verification
- Secure examination sessions
- Question variation
- Reasoning-based follow-up questions
- Unfamiliar application scenarios
- Authorized proctoring integrations
- Review of suspicious assessment patterns

Avoid relying on unreliable AI-generated-text detection as definitive proof of misconduct.

## PART 16 — SECURITY, PRIVACY, AND ACCESS CONTROL

### 16.1 Institutional Data Protection

Design the system with educational privacy and security requirements in mind.

Address applicable requirements such as FERPA in the United States and GDPR where relevant.

Compliance must be evaluated according to the institution, jurisdiction, student population, and actual product configuration.

### 16.2 Role-Based Access Control

Support roles such as:

- Student
- Teacher
- Human Examiner
- Second Reviewer
- Institution Administrator
- Platform Administrator

Each role must have explicitly defined permissions.

### 16.3 Data Security

Implement:

- Encryption in transit
- Encryption at rest
- Secure authentication
- Organization-level data isolation
- Audit logging
- Configurable retention policies
- Authorized access to recordings
- Secure handling of uploaded educational content
- Protection against prompt injection
- Controlled AI access to student information

### 16.4 Audit Requirements

Maintain a traceable record of:

- Original student responses
- Transcript corrections
- Clarification exchanges
- AI-generated assessments
- Human scores
- Score adjustments
- Reconciliation outcomes
- Final approval decisions
- Grade publication and amendments

Audit records must preserve the distinction between original evidence and later modifications.

## PART 17 — TECHNICAL ARCHITECTURE

Design a modern, scalable, multi-tenant SaaS application.

### 17.1 Frontend

Recommended capabilities:

- Student examination portal
- Educator dashboard
- Human grading interface
- Institutional administration portal
- Assessment analytics

Recommend an appropriate modern frontend framework.

### 17.2 Backend

The backend must support:

- User authentication
- Role management
- Course management
- Assessment configuration
- Examination sessions
- Conversation orchestration
- Transcript verification
- Evidence storage
- AI scoring
- Human scoring
- Score reconciliation
- Grade approval
- Reporting

### 17.3 AI Service Architecture

Separate the core AI responsibilities into logical components.

- **Component A: Socratic Questioning Engine** — Generates and selects adaptive examination questions.
- **Component B: Student Response Analysis Engine** — Analyzes student responses and identifies possible misconceptions or uncertainties.
- **Component C: Transcript Verification Engine** — Ensures accurate recording and requests clarification when needed.
- **Component D: Evidence Collection Engine** — Organizes verified responses into structured assessment evidence.
- **Component E: Knowledge Evaluation Engine** — Evaluates demonstrated understanding against learning objectives.
- **Component F: AI Grading Engine** — Generates preliminary rubric-based scores and explanations.
- **Component G: Examination Control Engine** — Determines the next action and decides when sufficient evidence has been collected.
- **Component H: Score Reconciliation Engine** — Compares AI and human scores and manages disagreement workflows.

These components may initially share infrastructure or AI model services.

Do not create unnecessary microservices for the MVP.

Prioritize clear separation of responsibilities over architectural complexity.

### 17.4 Recommended Data Entities

Design a relational database containing entities such as:

- Organizations
- Users
- Roles
- Courses
- LearningObjectives
- CourseMaterials
- Assessments
- AssessmentRubrics
- RubricCriteria
- ExaminationSessions
- ExaminationQuestions
- StudentResponses
- TranscriptVersions
- ResponseVerifications
- ClarificationEvents
- AssessmentEvidence
- AIGrades
- HumanGrades
- GradeDisagreements
- GradeReconciliations
- FinalGrades
- ApprovalRecords
- AuditEvents

Use appropriate foreign keys, constraints, and version tracking.

Maintain separate records for original responses, interpretations, corrections, and final verified evidence.

## PART 18 — MVP DEVELOPMENT SCOPE

### 18.1 MVP Objective

Build a working prototype that demonstrates the platform's primary educational value.

The MVP must prove that the AI can conduct an adaptive Socratic examination, collect trustworthy evidence, and support human-AI grading.

### 18.2 Required MVP Features

- **Feature 1: Teacher Assessment Setup** — Teachers can create an assessment with: Subject; Topic; Learning objectives; Assessment rubric; Question limit; AI-human grading weights.
- **Feature 2: Adaptive Student Examination** — Students complete a text-based Socratic examination. The AI dynamically selects follow-up questions based on previous answers.
- **Feature 3: Response Verification** — The system detects materially ambiguous responses and requests clarification. Original responses and clarifications are recorded separately.
- **Feature 4: Complete Examination Transcript** — The system stores a chronological transcript. Every response must be associated with its question and examination session.
- **Feature 5: AI Preliminary Scoring** — The AI generates rubric-based evaluations supported by response evidence.
- **Feature 6: Human Grading Dashboard** — A human reviewer independently evaluates the examination.
- **Feature 7: Combined Scoring** — The system calculates scores using configurable AI-human weights.
- **Feature 8: Disagreement Detection** — Significant scoring differences trigger a reconciliation workflow.
- **Feature 9: Final Grade Approval** — An authorized reviewer approves the final grade.
- **Feature 10: Assessment Report** — The system generates a final report showing: Final grade; Learning objective results; AI score; Human score; Relevant evidence; Identified knowledge gaps; Approval status.

### 18.3 Features to Defer

Do not prioritize the following in the first MVP:

- Real-time voice examinations
- Advanced proctoring
- Full learning management system integrations
- Complex institutional billing
- Mobile applications
- Extensive gamification
- Advanced predictive analytics

Design for future extensibility without building these features prematurely.

## PART 19 — IMPLEMENTATION REQUIREMENTS

Build the system incrementally.

### Phase 1: Product and System Design

Deliver:

- Product requirements document
- MVP feature definition
- System architecture
- Database schema
- AI component architecture
- Examination state machine
- Grading and approval workflow
- Security architecture
- Student and teacher user flows

### Phase 2: Core Application

Implement:

- Authentication
- Teacher assessment creation
- Student examination interface
- Examination session management
- Persistent transcript storage
- Learning objective and rubric management

### Phase 3: Socratic Assessment Engine

Implement:

- Response analysis
- Adaptive question selection
- Misconception identification
- Neutral clarification questions
- Evidence collection
- Examination stopping conditions

### Phase 4: Transcript Verification

Implement:

- Response verification rules
- Clarification workflow
- Student confirmation interface
- Response version history
- Unresolved ambiguity handling

### Phase 5: AI and Human Grading

Implement:

- AI scoring engine
- Evidence-linked grading explanations
- Independent human evaluation
- Weighted score calculation
- Disagreement detection
- Reconciliation workflow
- Final approval

### Phase 6: Testing and Validation

Test:

- Adaptive examination behavior
- Non-leading clarification questions
- Transcript integrity
- Evidence attribution accuracy
- Scoring rule compliance
- AI-human grading calculations
- Disagreement resolution
- Role-based access
- Audit log integrity
- Assessment reliability

### Phase 7: MVP Deployment

Prepare:

- Deployment architecture
- Environment configuration
- Database migrations
- API documentation
- Administrative setup
- Error handling
- Monitoring
- Basic security validation
- Demonstration assessments

## PART 20 — REQUIRED TEST SCENARIOS

The implementation must include automated tests and representative examination simulations.

- **Scenario A: Correct Answer Without Understanding** — A student provides the correct definition but cannot explain the principle or apply it. Expected behavior: The AI asks targeted follow-up questions and does not automatically assign a mastery score based on the initial answer.
- **Scenario B: Incorrect Initial Answer Followed by Strong Reasoning** — A student initially provides an incorrect answer but later demonstrates understanding through independent explanation. Expected behavior: The AI evaluates the full body of evidence according to the rubric, including the initial error and subsequent reasoning.
- **Scenario C: Ambiguous Answer** — A student gives an unclear response. Expected behavior: The AI asks a neutral clarification question without revealing the correct answer.
- **Scenario D: Transcript Error** — The system records an incorrect technical term. Expected behavior: The student can correct the transcript, the original version remains preserved, and the scoring system uses the properly verified evidence.
- **Scenario E: Substantive Answer Revision** — A student changes their answer after submitting it. Expected behavior: The system records the revision separately and applies the configured examination rules.
- **Scenario F: AI-Human Scoring Agreement** — The AI and human evaluator provide similar scores. Expected behavior: The system calculates the weighted combined score and requests final approval.
- **Scenario G: AI-Human Scoring Disagreement** — AI Score: 95; Human Score: 60. Expected behavior: The system flags the disagreement, initiates reconciliation, and prevents automatic finalization.
- **Scenario H: Insufficient Evidence** — The AI cannot reliably determine the student's understanding within the question or time limit. Expected behavior: The system identifies insufficient evidence and follows the defined human-review policy instead of inventing certainty.
- **Scenario I: AI Accidentally Reveals the Answer** — A generated clarification question includes the concept the student was supposed to demonstrate independently. Expected behavior: The system detects or flags the leading question, prevents contaminated evidence from being treated as independent mastery, and supports a replacement question.
- **Scenario J: Unauthorized Grade Modification** — A user without grading permissions attempts to change a final score. Expected behavior: The action is rejected and appropriately logged.

## PART 21 — PRODUCT SUCCESS METRICS

Define measurable indicators of product quality.

Potential metrics include:

**Assessment Quality**

- Agreement with expert-reviewed benchmark assessments
- Scoring consistency
- Rubric adherence
- Misconception identification accuracy
- Rate of unsupported AI grading claims
- Assessment completion rate

**Transcript Quality**

- Transcription error rate, where applicable
- Clarification frequency
- Student-confirmed correction rate
- Unresolved ambiguity rate
- Evidence attribution accuracy

**Human Review Quality**

- AI-human scoring differences
- Frequency of required reconciliation
- Human grading time
- Score adjustment frequency
- Appeal and grade amendment rates

**User Experience**

- Examination completion time
- Student-reported clarity
- Teacher satisfaction
- Accessibility outcomes
- Frequency of unnecessary clarification questions

**Commercial Performance**

- Institutional pilot adoption
- Pilot-to-paid conversion
- Educator retention
- Assessment cost per student
- Human review cost per examination

Do not claim that these metrics demonstrate educational validity until supported by appropriate evidence and evaluation.

## PART 22 — DEVELOPMENT EXECUTION INSTRUCTIONS

You are not being asked to produce only a conceptual business proposal.

You are being asked to develop a functioning software product.

Follow these instructions:

1. Translate the requirements into a practical technical architecture.
2. Identify implementation dependencies.
3. Select a maintainable technology stack.
4. Build the simplest architecture capable of supporting the MVP.
5. Create the database schema and migrations.
6. Implement backend APIs.
7. Implement the student examination interface.
8. Implement the adaptive Socratic questioning engine.
9. Implement transcript verification and audit history.
10. Implement AI preliminary grading.
11. Implement the human review interface.
12. Implement weighted scoring and disagreement detection.
13. Implement final-grade approval.
14. Add automated tests.
15. Provide a runnable local development environment.
16. Document configuration, setup, and deployment procedures.

Use structured AI outputs with schema validation for assessment state, evidence records, and scoring recommendations.

Keep deterministic operations such as score calculation, permissions, and approval-state transitions in application code rather than relying on free-form LLM outputs.

Use explicit error handling and prevent invalid state transitions.

Make the architecture modular enough to support alternative AI model providers in the future.

If an AI API key is unavailable, provide a clearly labeled mock mode that allows the interface and application workflow to be tested. Never present mock assessments as genuine AI evaluation results.

Do not invent completed features, test results, validation findings, or deployment status.

Where requirements conflict, prioritize:

1. Student safety, privacy, and fairness
2. Transcript and evidence integrity
3. Assessment validity
4. Human grading accountability
5. Functional correctness
6. Maintainability
7. Performance
8. Additional features

Start with a runnable MVP rather than an unnecessarily complex architecture.

## FINAL PRODUCT DEFINITION

Build an AI-powered educational assessment platform that evaluates genuine understanding through adaptive Socratic dialogue.

The system must:

- **ASK** — Generate questions that investigate understanding.
- **ANALYZE** — Evaluate the student's explanations and reasoning.
- **CLARIFY** — Request clarification when responses are ambiguous.
- **VERIFY** — Confirm that the student's actual words and intended meaning are accurately represented.
- **RECORD** — Preserve complete, auditable examination evidence.
- **ASSESS** — Evaluate demonstrated understanding against predefined rubrics.
- **GRADE** — Generate evidence-based preliminary AI scores.
- **REVIEW** — Require independent human evaluation.
- **RECONCILE** — Compare AI and human scores and resolve material disagreements.
- **CALCULATE** — Apply approved grading policies.
- **APPROVE** — Require authorized human approval.
- **REPORT** — Produce transparent assessment results supported by student responses.

### Fundamental Principle

"The purpose of an examination is not merely to determine whether a student can produce the correct answer, but to determine whether the student genuinely understands the knowledge behind that answer."

Create a platform that makes this principle measurable, reviewable, and operational.

Begin by presenting the MVP architecture and implementation plan, then proceed with building the first functional prototype.
