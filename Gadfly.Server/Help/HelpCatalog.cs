using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Help;

/// <summary>Who a help topic is written for. A reader sees the topics of every audience they belong to.</summary>
public enum HelpAudience
{
    /// <summary>Anyone, signed in or not: a student whose microphone will not work may never get past the sign-in page.</summary>
    Public,
    /// <summary>Students, and the staff who support them.</summary>
    Student,
    /// <summary>People who grade: examiners and second reviewers, and instructors, admins and owners.</summary>
    Grader,
    /// <summary>Instructors, admins and owners.</summary>
    Instructor,
    /// <summary>Organization admins and owners.</summary>
    Admin,
}

/// <summary>The groups the help home page lists topics under, in this order.</summary>
public enum HelpSectionId { StartHere, TakingAnExam, FixingProblems, PrivacyAndResults, TeachingAndGrading, RunningYourOrganization }

/// <summary>
/// One help topic (GAD-T292). <see cref="File"/> is the Markdown file under docs/guides that holds its text: the
/// guides are the single source, the app only renders them. A topic with <see cref="PendingTicket"/> set is a stub
/// for a feature another ticket is still building; the page says so and the final docs pass fills it in.
/// </summary>
public sealed record HelpTopic(string Slug, string File, HelpAudience Audience, HelpSectionId Section, string Title, string Summary,
    string Keywords = "", string? PendingTicket = null)
{
    public bool IsStub => PendingTicket is not null;
}

/// <summary>Who is reading help: the audiences whose topics they may open.</summary>
public sealed record HelpViewer(bool SignedIn, IReadOnlySet<HelpAudience> Audiences)
{
    public static readonly HelpViewer Anonymous = new(false, new HashSet<HelpAudience> { HelpAudience.Public });
    /// <summary>Platform staff: every topic.</summary>
    public static readonly HelpViewer Everything = new(true, new HashSet<HelpAudience>(Enum.GetValues<HelpAudience>()));

    public bool CanRead(HelpTopic topic) => Audiences.Contains(topic.Audience);

    /// <summary>True when the reader is staff (anything beyond a student), which changes how the home page greets them.</summary>
    public bool IsStaff => Audiences.Contains(HelpAudience.Grader);

    /// <summary>
    /// The reader for a role in the active organization. A student reads the public and student topics only. Staff read
    /// their own topics and also the student ones, because they are the people a student asks for help.
    /// </summary>
    public static HelpViewer For(bool signedIn, string? role, bool isSuperAdmin)
    {
        if (!signedIn) return Anonymous;
        if (isSuperAdmin) return Everything;
        var set = new HashSet<HelpAudience> { HelpAudience.Public };
        switch (role)
        {
            case OrgRoles.Student:
                set.Add(HelpAudience.Student);
                break;
            case OrgRoles.Examiner or OrgRoles.SecondReviewer:
                set.Add(HelpAudience.Student); set.Add(HelpAudience.Grader);
                break;
            case OrgRoles.Instructor:
                set.Add(HelpAudience.Student); set.Add(HelpAudience.Grader); set.Add(HelpAudience.Instructor);
                break;
            case OrgRoles.Owner or OrgRoles.Admin:
                set.Add(HelpAudience.Student); set.Add(HelpAudience.Grader); set.Add(HelpAudience.Instructor); set.Add(HelpAudience.Admin);
                break;
        }
        return new HelpViewer(true, set);
    }

    public static HelpViewer For(ITenantContext tenant) => For(tenant.UserId is not null, tenant.Role, tenant.IsSuperAdmin);
}

/// <summary>The slugs of the help topics, for <c>&lt;HelpLink Topic="..."/&gt;</c> and links in code.</summary>
public static class HelpTopics
{
    public const string QuickStartStudent = "quick-start-student";
    /// <summary>The student essentials in Korean (GAD-T544).</summary>
    public const string QuickStartStudentKorean = "quick-start-student-ko";
    /// <summary>The student essentials in Spanish (GAD-T613).</summary>
    public const string QuickStartStudentSpanish = "quick-start-student-es";
    public const string QuickStartInstructor = "quick-start-instructor";
    public const string QuickStartGrader = "quick-start-grader";
    public const string QuickStartAdmin = "quick-start-admin";
    public const string HowTheExamWorks = "how-the-exam-works";
    public const string BeforeYourExam = "before-your-exam";
    public const string GuidedTours = "guided-tours";

    public const string StudentGuide = "student-guide";
    public const string HowTheExaminerAsks = "how-the-examiner-asks-questions";
    public const string EyeContact = "eye-contact-and-the-screen-fade";
    public const string Accommodations = "accommodations";
    public const string PracticeInterview = "practice-interview";
    /// <summary>The get-ready page and the student's languages (GAD-T592), and its Korean version.</summary>
    public const string GetReady = "get-ready-and-languages";
    public const string GetReadyKorean = "get-ready-and-languages-ko";
    public const string GetReadySpanish = "get-ready-and-languages-es";
    /// <summary>Eye contact and gentle reminders in Korean (GAD-T592).</summary>
    public const string EyeContactKorean = "eye-contact-ko";
    /// <summary>Eye contact and gentle reminders in Spanish (GAD-T613).</summary>
    public const string EyeContactSpanish = "eye-contact-es";
    /// <summary>What happens when a student leaves the exam window (GAD-T592), and its Korean version.</summary>
    public const string LeavingTheWindow = "leaving-the-exam-window";
    public const string LeavingTheWindowKorean = "leaving-the-exam-window-ko";
    public const string LeavingTheWindowSpanish = "leaving-the-exam-window-es";
    /// <summary>For staff: exam languages, translated texts, second-language speakers, grading and fairness by language (GAD-T592).</summary>
    public const string ExamLanguages = "exams-in-other-languages";
    /// <summary>For staff: attempts marked Needs instructor review and how to decide them (GAD-T592).</summary>
    public const string MarkedForReview = "attempts-marked-for-review";

    public const string Microphone = "troubleshooting-microphone";
    public const string Camera = "troubleshooting-camera";
    public const string Browser = "troubleshooting-browser";
    public const string Faq = "faq";

    public const string Privacy = "privacy";
    public const string GradingAndResults = "grading-and-results";
    public const string Appeals = "appeals";
    public const string TranscriptDownloads = "transcript-downloads";

    public const string InstructorGuide = "instructor-guide";
    public const string GradingGuide = "grading-guide";
    public const string IntegrityNotes = "integrity-notes";

    public const string AdminGuide = "admin-guide";
    public const string AdminDashboard = "admin-dashboard";
    public const string AdminStudents = "admin-students";
    public const string AdminExams = "admin-exams";
    public const string AdminAssignments = "admin-assignments";
    public const string AdminLiveSessions = "admin-live-sessions";
    public const string AdminAuditLog = "admin-audit-log";
    public const string AdminSystemStatus = "admin-system-status";
    public const string LtiSetup = "lti-setup";
}

/// <summary>
/// The list of help topics, in the order they are shown (GAD-T292). Adding a topic is one line here plus its
/// Markdown file in docs/guides; a test checks the two stay in step.
/// </summary>
public static class HelpCatalog
{
    public static readonly IReadOnlyList<(HelpSectionId Id, string Title, string Blurb)> Sections =
    [
        (HelpSectionId.StartHere, "Start here", "The short version for your role, and what a Gadfly exam is."),
        (HelpSectionId.TakingAnExam, "Taking an exam", "What happens in the exam room, from the first question to the last."),
        (HelpSectionId.FixingProblems, "Fixing problems", "Microphone, camera and browser trouble, and quick answers."),
        (HelpSectionId.PrivacyAndResults, "Privacy and results", "What is kept about you, how your grade is decided, and what you can do about it."),
        (HelpSectionId.TeachingAndGrading, "Teaching and grading", "Building exams from your material, reading sessions and grading them."),
        (HelpSectionId.RunningYourOrganization, "Running your organization", "People, settings, integrations and the admin pages."),
    ];

    public static readonly IReadOnlyList<HelpTopic> Topics =
    [
        // Start here
        new(HelpTopics.QuickStartStudent, "quick-start-student.md", HelpAudience.Student, HelpSectionId.StartHere,
            "Quick start for students", "Sign in, get ready, and take your first spoken exam in five steps.",
            "getting started begin first time new student"),
        // The student essentials in Korean (GAD-T544): sign-in to result, the get-ready page, eye contact, what to do if something goes wrong.
        new(HelpTopics.QuickStartStudentKorean, "quick-start-student-ko.md", HelpAudience.Student, HelpSectionId.StartHere,
            "학생 빠른 시작(한국어)", "로그인부터 결과까지 다섯 단계, 시험 준비, 시선 맞추기, 문제가 생겼을 때 (Quick start for students, in Korean).",
            "korean 한국어 학생 시작 quick start getting started"),
        // The student essentials in Spanish (GAD-T613), the same as the Korean.
        new(HelpTopics.QuickStartStudentSpanish, "quick-start-student-es.md", HelpAudience.Student, HelpSectionId.StartHere,
            "Inicio rápido para estudiantes (español)", "Cinco pasos del inicio de sesión al resultado, la preparación, el contacto visual y qué hacer si algo falla (Quick start for students, in Spanish).",
            "spanish español estudiante inicio comenzar quick start getting started"),
        new(HelpTopics.QuickStartInstructor, "quick-start-instructor.md", HelpAudience.Instructor, HelpSectionId.StartHere,
            "Quick start for instructors", "From your notes to a graded exam: upload, build, assign, review, grade.",
            "getting started begin first time teacher"),
        new(HelpTopics.QuickStartGrader, "quick-start-grader.md", HelpAudience.Grader, HelpSectionId.StartHere,
            "Quick start for examiners and second reviewers", "Open the grading queue, score an attempt, and resolve a disagreement.",
            "getting started begin first time examiner second reviewer marker"),
        new(HelpTopics.QuickStartAdmin, "quick-start-admin.md", HelpAudience.Admin, HelpSectionId.StartHere,
            "Quick start for admins and owners", "Set up your organization, invite people, and run a first exam.",
            "getting started begin first time administrator owner setup"),
        new(HelpTopics.HowTheExamWorks, "how-the-exam-works.md", HelpAudience.Public, HelpSectionId.StartHere,
            "How a Gadfly exam works", "A spoken conversation with a talking examiner, on camera, graded by people.",
            "overview oral exam socratic avatar what is"),
        new(HelpTopics.BeforeYourExam, "before-your-exam.md", HelpAudience.Public, HelpSectionId.StartHere,
            "Before your exam: a checklist", "Ten minutes of preparation that prevents most problems on the day.",
            "prepare ready checklist setup room headset test"),
        new(HelpTopics.GuidedTours, "guided-tours.md", HelpAudience.Public, HelpSectionId.StartHere,
            "Guided tours", "Short walkthroughs of each main page, and how to replay them.", "tutorial walkthrough show me around tour replay help"),

        // Taking an exam
        new(HelpTopics.StudentGuide, "STUDENT_GUIDE.md", HelpAudience.Student, HelpSectionId.TakingAnExam,
            "Student guide", "Everything a student needs: preparing, the get-ready page, the exam room, time, results and your data.",
            "manual handbook my exams start resume attempts time limit"),
        new(HelpTopics.HowTheExaminerAsks, "how-the-examiner-asks-questions.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "How the examiner asks questions", "Why each question follows from your last answer, the kinds of question, and how to answer well.",
            "socratic follow-up probe clarify counterexample i don't know aporia read back"),
        new(HelpTopics.GetReady, "get-ready-and-languages.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Getting ready: the get-ready page and your languages", "Your languages, camera and microphone, voice calibration, choosing and customizing your examiner (voice, skin tone, hair, clothing, framing) and the free practice run.",
            "get ready language korean english primary second language esl camera microphone device calibration examiner male female customize personalize voice skin tone hair clothing framing practice notice"),
        new(HelpTopics.GetReadyKorean, "get-ready-and-languages-ko.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "시험 준비와 언어(한국어)", "사용 언어, 카메라와 마이크, 목소리 맞추기, 시험관 고르기와 꾸미기(목소리, 피부색, 머리 색, 옷차림, 화면 구도), 무료 연습 시험 (Getting ready, in Korean).",
            "korean 한국어 시험 준비 언어 카메라 마이크 목소리 맞추기 시험관 꾸미기 피부색 머리 색 옷차림 화면 구도 연습 get ready customize examiner"),
        new(HelpTopics.GetReadySpanish, "get-ready-and-languages-es.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Prepararse para el examen y sus idiomas (español)", "Sus idiomas, cámara y micrófono, ajuste de voz, elegir y personalizar la IA examinadora (voz, tono de piel, cabello, vestimenta, encuadre) y el examen de práctica gratuito (Getting ready, in Spanish).",
            "spanish español preparación idioma cámara micrófono ajuste de voz examinadora personalizar voz tono de piel cabello vestimenta encuadre práctica get ready customize examiner"),
        new(HelpTopics.EyeContact, "eye-contact-and-the-screen-fade.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Eye contact and gentle reminders", "Looking away to think is normal; only a long look off the screen while you speak gets a gentle reminder, never part of your grade.",
            "gaze looking away thinking dim fade screen examiner eyes camera reminder self-view"),
        new(HelpTopics.EyeContactKorean, "eye-contact-ko.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "시선 맞추기와 부드러운 알림(한국어)", "생각하느라 시선을 돌려도 괜찮아요. 알림은 언제 나오고 무엇을 뜻하지 않는지 (Eye contact, in Korean).",
            "korean 한국어 시선 맞추기 알림 카메라 eye contact gaze"),
        new(HelpTopics.EyeContactSpanish, "eye-contact-es.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Contacto visual y recordatorios amables (español)", "Apartar la mirada para pensar es normal: cuándo aparece un recordatorio y qué no significa (Eye contact, in Spanish).",
            "spanish español contacto visual mirada recordatorio cámara eye contact gaze"),
        new(HelpTopics.LeavingTheWindow, "leaving-the-exam-window.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Leaving the exam window", "The exam pauses and your words are kept; a person, never the computer, decides whether anything needs following up.",
            "window tab focus switch leave away pause absence review cancel explanation accommodation"),
        new(HelpTopics.LeavingTheWindowKorean, "leaving-the-exam-window-ko.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "시험 창을 벗어나면(한국어)", "시험이 일시 정지되고 하던 말은 남아요. 판단은 컴퓨터가 아니라 사람이 해요 (Leaving the exam window, in Korean).",
            "korean 한국어 시험 창 탭 일시 정지 검토 window focus"),
        new(HelpTopics.LeavingTheWindowSpanish, "leaving-the-exam-window-es.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Si sale de la ventana del examen (español)", "El examen se pausa y lo que dijo se conserva; una persona, nunca la computadora, decide si algo necesita seguimiento (Leaving the exam window, in Spanish).",
            "spanish español ventana pestaña pausa revisión window focus"),
        new(HelpTopics.Accommodations, "accommodations.md", HelpAudience.Public, HelpSectionId.TakingAnExam,
            "Accommodations", "Typed answers, eye-contact changes and other adjustments, and how to ask for them.",
            "accessibility disability typing typed extra time adjustment speech hearing"),
        new(HelpTopics.PracticeInterview, "practice-interview.md", HelpAudience.Student, HelpSectionId.TakingAnExam,
            "Practice interview: meet your examiner", "A five-minute ungraded practice with the talking examiner, from My exams: nothing is kept or counted.",
            "practice rehearsal meet your examiner try warm up first time nervous not graded attempt"),

        // Fixing problems
        new(HelpTopics.Microphone, "troubleshooting-microphone.md", HelpAudience.Public, HelpSectionId.FixingProblems,
            "Microphone problems", "The examiner cannot hear you, the meter does not move, or the wrong microphone is used: fixes by browser and system.",
            "mic audio sound not heard permission blocked headset bluetooth airpods mute input chrome edge safari firefox windows mac ipad iphone android chromebook"),
        new(HelpTopics.Camera, "troubleshooting-camera.md", HelpAudience.Public, HelpSectionId.FixingProblems,
            "Camera problems", "A black picture, a blocked camera or one that another app is using: fixes by browser and system.",
            "webcam video black picture permission blocked in use zoom teams chrome edge safari firefox windows mac ipad iphone android chromebook"),
        new(HelpTopics.Browser, "troubleshooting-browser.md", HelpAudience.Public, HelpSectionId.FixingProblems,
            "Browser and connection problems", "Which browsers work, what the get-ready checks mean, and what to do when the page stops responding.",
            "supported browser chrome edge safari firefox cookies javascript connection wifi reconnect reload frozen extension vpn firewall no sound examiner silent"),
        new(HelpTopics.Faq, "faq.md", HelpAudience.Public, HelpSectionId.FixingProblems,
            "Frequently asked questions", "Short answers to the questions students and staff ask most.",
            "questions answers common faq"),

        // Privacy and results
        new(HelpTopics.Privacy, "privacy-in-plain-words.md", HelpAudience.Public, HelpSectionId.PrivacyAndResults,
            "Privacy in plain words: what is and is not recorded", "What Gadfly keeps about an exam, what never leaves your device, how long it is kept and who sees it.",
            "recorded recording video audio stored data retention consent delete erase export download gdpr eu voice measurements"),
        new(HelpTopics.GradingAndResults, "grading-and-results.md", HelpAudience.Public, HelpSectionId.PrivacyAndResults,
            "How grading and results work", "Who decides your grade, what the AI does and does not do, and when you see your result.",
            "grade score mark result published report ai human examiner approve percentage"),
        new(HelpTopics.Appeals, "appeals.md", HelpAudience.Student, HelpSectionId.PrivacyAndResults,
            "Appeals", "What to do when you think a published result is wrong.", "appeal dispute disagree regrade amended complaint"),
        new(HelpTopics.TranscriptDownloads, "transcript-downloads.md", HelpAudience.Student, HelpSectionId.PrivacyAndResults,
            "Transcript downloads", "Your official transcript in PDF, Word and other formats, the copies staff get, and how anyone can check a copy is genuine.",
            "transcript download pdf word export copy verify verification code genuine certificate"),

        // Teaching and grading
        new(HelpTopics.InstructorGuide, "INSTRUCTOR_GUIDE.md", HelpAudience.Instructor, HelpSectionId.TeachingAndGrading,
            "Instructor guide", "The full reference: source material, assessments, assigning, reviewing sessions, grading and analytics.",
            "manual handbook sources upload concepts rubric assessment publish assign review analytics export"),
        new(HelpTopics.GradingGuide, "grading-guide.md", HelpAudience.Grader, HelpSectionId.TeachingAndGrading,
            "Grading guide", "The grading workflow step by step: blind grading, comparison, second review, approval and publishing.",
            "grading queue blind score objective reconcile second review approve publish amend appeal examiner"),
        new(HelpTopics.IntegrityNotes, "integrity-notes.md", HelpAudience.Instructor, HelpSectionId.TeachingAndGrading,
            "Reading integrity notes", "What each note means, how weak the evidence is, and why none of it is ever a verdict or part of a grade.",
            "integrity flags gaze camera voice measurements text style tab switch cheating proctoring low confidence"),
        new(HelpTopics.MarkedForReview, "attempts-marked-for-review.md", HelpAudience.Grader, HelpSectionId.TeachingAndGrading,
            "Attempts marked for instructor review", "Why an attempt is marked (window absences, uncalibrated Korean scores), where you see it, and how to clear it or approve with a note.",
            "needs instructor review flag marked held window absence explanation cross-lingual uncalibrated korean clear approve note publish"),
        new(HelpTopics.ExamLanguages, "exams-in-other-languages.md", HelpAudience.Grader, HelpSectionId.TeachingAndGrading,
            "Exams in English and Korean (for staff)", "Offering languages, approving translated exam texts, second-language speakers, grading a Korean attempt and fairness by language.",
            "language languages korean english translation machine translation approve draft esl second language primary language fairness calibration"),

        // Running your organization
        new(HelpTopics.AdminGuide, "ADMIN_GUIDE.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Admin guide", "The full reference: members and roles, sign-in, settings, retention, API keys, webhooks, usage, export and erasure.",
            "manual handbook members invite roles mfa settings retention api keys webhooks usage erase export organization"),
        new(HelpTopics.AdminDashboard, "admin-dashboard.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Admin dashboard", "What needs attention today, the last 7 and 30 days in numbers, and quick actions.",
            "dashboard home overview attention getting started checklist completion turnaround"),
        new(HelpTopics.AdminStudents, "admin-students.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Students directory", "Find, add, invite and import students, and manage each one from their profile.",
            "students directory profile import csv export invite deactivate reactivate password"),
        new(HelpTopics.AdminExams, "admin-exams.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Exams manager", "Every exam in the organization: settings, windows, preview, duplicate, export, import and archive.",
            "exams manager duplicate archive restore preview import export json window coverage"),
        new(HelpTopics.AdminAssignments, "admin-assignments.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Assignments console", "Who sits which exam: due dates, attempts, resets and accommodations, one at a time or many at once.",
            "assignments assign due date attempts reset accommodations extra time typed answers grid unassign"),
        new(HelpTopics.AdminLiveSessions, "admin-live-sessions.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Live sessions monitor", "Exams in progress right now, and how to add time, pause, end one or send the student a notice.",
            "live monitor in progress extend add time pause resume end notice session"),
        new(HelpTopics.AdminAuditLog, "admin-audit-log.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Audit log", "Who changed what, and when: filters, details, redacted events and exports.", "audit log history changes who export redacted"),
        new(HelpTopics.AdminSystemStatus, "admin-system-status.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "System status and jobs", "The health of the app, its providers and its background jobs.", "status health jobs providers version retry"),
        new(HelpTopics.LtiSetup, "lti-setup.md", HelpAudience.Admin, HelpSectionId.RunningYourOrganization,
            "Connecting your LMS (LTI 1.3)", "Set up Canvas, Moodle, Blackboard or Brightspace: exams added from the course, one-click student launch, grades back to the gradebook.",
            "lti lms canvas moodle blackboard brightspace d2l integration gradebook roster deep linking single sign-on"),
    ];

    private static readonly Dictionary<string, HelpTopic> BySlug = Topics.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HelpTopic> ByFile = Topics.ToDictionary(t => t.File, StringComparer.OrdinalIgnoreCase);

    public static HelpTopic? Find(string? slug) => slug is not null && BySlug.TryGetValue(slug.Trim(), out var t) ? t : null;

    /// <summary>The topic a guide file is shown as ("STUDENT_GUIDE.md" is the student guide), for links between guides.</summary>
    public static HelpTopic? FindByFile(string? file) => file is not null && ByFile.TryGetValue(file.Trim(), out var t) ? t : null;

    /// <summary>The topics a reader may open, in catalogue order.</summary>
    public static IEnumerable<HelpTopic> VisibleTo(HelpViewer viewer) => Topics.Where(viewer.CanRead);

    /// <summary>The first topic to send a reader to: the quick start for their role, or how the exam works.</summary>
    public static HelpTopic StartFor(HelpViewer viewer)
    {
        var slug = viewer.Audiences.Contains(HelpAudience.Admin) ? HelpTopics.QuickStartAdmin
            : viewer.Audiences.Contains(HelpAudience.Instructor) ? HelpTopics.QuickStartInstructor
            : viewer.Audiences.Contains(HelpAudience.Grader) ? HelpTopics.QuickStartGrader
            : viewer.Audiences.Contains(HelpAudience.Student) ? HelpTopics.QuickStartStudent
            : HelpTopics.HowTheExamWorks;
        return BySlug[slug];
    }

    public static string AudienceLabel(HelpAudience audience) => audience switch
    {
        HelpAudience.Public => "For everyone",
        HelpAudience.Student => "For students",
        HelpAudience.Grader => "For examiners and reviewers",
        HelpAudience.Instructor => "For instructors",
        HelpAudience.Admin => "For admins and owners",
        _ => "",
    };
}
