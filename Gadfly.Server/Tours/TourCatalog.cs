namespace Gadfly.Server.Tours;

/// <summary>
/// One step of a guided tour (GAD-T293). <see cref="Anchor"/> names the element the callout points at: a bare name such as
/// <c>dash-numbers</c> means the first visible element with <c>data-tour="dash-numbers"</c>; anything else is a CSS selector
/// (used for elements a page shares with other features, such as the admin menu). A null anchor is a centred step that needs
/// no element. A step whose element is not on the page (an empty list, a panel for another grading status) is skipped.
/// </summary>
public sealed record TourStep(string? Anchor, string Title, string Body);

/// <summary>
/// A guided tour of one page. <see cref="Version"/> is part of the key that remembers, per account in the browser, that the
/// tour was shown: raise it when the steps change enough that people should see the tour again. <see cref="Page"/> is the
/// address (no leading slash) the help topic links to for a replay; for a page that needs an id (one grading session, one
/// get-ready page) it is the list the reader picks one from, and <see cref="ReplayHint"/> says so.
/// </summary>
public sealed record TourDefinition(string Id, int Version, string Title, string Page, IReadOnlyList<TourStep> Steps,
    int StartDelayMs = 700, string? ReplayHint = null)
{
    /// <summary>The address that opens <see cref="Page"/> and starts this tour straight away.</summary>
    public string ReplayHref => Page + (Page.Contains('?') ? "&" : "?") + "tour=" + Id;
}

/// <summary>The ids of the tours, for <c>&lt;Tour Id="..."/&gt;</c>.</summary>
public static class TourIds
{
    public const string AdminDashboard = "admin-dashboard";
    public const string AdminDesk = "admin-desk";
    public const string AdminStudents = "admin-students";
    public const string AdminExams = "admin-exams";
    public const string AdminAssignments = "admin-assignments";
    public const string AdminLive = "admin-live";
    public const string GradingQueue = "grading-queue";
    public const string GradingSession = "grading-session";
    public const string ReviewSession = "review-session";
    public const string MyExams = "my-exams";
    public const string GetReady = "get-ready";
}

/// <summary>
/// Every guided tour, with its step text (GAD-T293). The browser side is wwwroot/js/tour.js, started by the
/// <c>&lt;Tour&gt;</c> component a page places next to its title. Tours never run in the exam room itself: the engine refuses
/// to start there, and no tour is defined for it.
/// </summary>
public static class TourCatalog
{
    /// <summary>The last step of every tour: where to find it again.</summary>
    private static readonly TourStep Replay = new(".tour-launch", "See this again",
        "Press Show me around whenever you want this tour again. Help lists the tours for every page.");

    public static readonly IReadOnlyList<TourDefinition> All =
    [
        new(TourIds.AdminDashboard, 1, "The dashboard", "admin",
        [
            new(null, "Your dashboard",
                "This page shows what needs you today and how exams have gone lately. Press Next to look around, or Esc to stop at any time."),
            new(".admin-nav", "The admin menu",
                "Every admin page is here, grouped into overview, people, exams, results and your organization's settings. You only see the pages your role can open."),
            new("dash-setup", "Getting started",
                "While your organization is new, this checklist shows the setup steps still to do. Each one links to the page where you do it."),
            new("dash-attention", "Needs attention",
                "Grading that is waiting, open appeals and problems to fix appear here as they happen, each with a link straight to it."),
            new("dash-numbers", "The last 7 and 30 days",
                "Exams taken, how many reached a proper end, the typical interview length, and how long grading takes from the end of the interview to approval."),
            new("dash-actions", "Quick actions",
                "Shortcuts to the jobs you do most: create an exam, add students, assign an exam, watch interviews live, grade, and invite staff."),
            new("dash-refresh", "Refresh",
                "The numbers are read when the page opens. Press Refresh to read them again."),
            Replay,
        ]),

        new(TourIds.AdminDesk, 1, "The admin desk", "admin/desk",
        [
            new(null, "The admin desk",
                "Exams, students and the assignments between them on one page, for setting things up quickly, testing and resetting."),
            new("desk-tabs", "Three tabs",
                "Exams: create an exam from your notes and publish it. Students: add student accounts. Student exams: assign exams, change attempts, see scores and reset attempts."),
            new(".desk-form", "Form first, list below",
                "Each tab starts with its form (New exam, Add a student or Assign an exam). Below it is the list, with the actions for each row at the end of the row."),
            Replay,
        ]),

        new(TourIds.AdminStudents, 1, "Students", "admin/students",
        [
            new(null, "Students",
                "Everyone who sits exams in your organization is listed here. Staff such as instructors and examiners are managed on Members."),
            new("students-add", "Add students",
                "Add one student with a user name and password, invite someone by email, or import a class list from a CSV file. An import shows you what will happen before anything is saved. Export CSV downloads the list as it is filtered."),
            new("students-filters", "Find a student",
                "Search by name, user name or email, and narrow the list by exam, status or accommodation. Choose the Invited status to see invitations not yet accepted."),
            new("students-table", "The list",
                "Click a name to open that student's profile. Greyed-out rows are deactivated accounts."),
            Replay,
        ]),

        new(TourIds.AdminExams, 1, "Exams", "admin/exams",
        [
            new(null, "Exams",
                "Every exam in your organization, with how many students have it, how many finished and how long they took."),
            new("exams-new", "Create or import",
                "New exam opens the exam builder. Import from a file brings in an exam exported from Gadfly: it arrives as a draft, and nothing is published or assigned."),
            new("exams-filters", "Find an exam",
                "Search by title, course or source material. Archived exams are hidden unless you choose them under Status."),
            new("exams-table", "Status and numbers",
                "Objectives are what the exam checks. Window shows when it can be taken. Click a title to edit the exam."),
            new("exams-actions", "Actions",
                "Open Actions on a row for its settings, objective coverage and window, to preview it as a student, duplicate or export it, see its history, or archive it. Archiving keeps every attempt and result, and can be undone."),
            Replay,
        ]),

        new(TourIds.AdminAssignments, 1, "Assignments", "admin/assignments",
        [
            new(null, "Assignments",
                "Who sits which exam, with opening and due dates, attempts and accommodations."),
            new(".people-head .row-actions", "Assign an exam",
                "Pick a published exam, then choose who sits it: students ticked on the grid, a list of user names or emails, or every active student. Dates, attempts and extra time are set here too."),
            new(".console-tabs", "List or grid",
                "The list shows one row per assignment. The grid shows students down the side and exams across the top, so gaps are easy to spot."),
            new(".people-filters", "Filter",
                "Narrow the list by student, exam, progress, accommodation or due date."),
            new(".table-scroll", "Select, then change",
                "Tick assignments to change them together: set or move dates, grant another attempt, reset an attempt, set accommodations, or unassign. Changes that need a reason keep it in the audit log."),
            Replay,
        ]),

        new(TourIds.AdminLive, 1, "Live sessions", "admin/live",
        [
            new(null, "Live sessions",
                "The exams in progress right now. The page updates on its own as students answer. No audio or video is ever shown or stored."),
            new(".live-card", "One card per exam",
                "The student and exam, the time left, the current question, how many objectives are covered, and whether their connection, microphone and camera are working."),
            new(".live-actions", "Step in when needed",
                "Add time, pause or resume, end the exam, or send a short written notice to the student's screen. Every action except a notice asks why; the reason goes in the audit log and on the session's review."),
            new(".live-head .row-actions", "Always up to date",
                "The time of the last update is shown here. Refresh reads everything again."),
            Replay,
        ]),

        new(TourIds.GradingQueue, 1, "Grading", "grading",
        [
            new(null, "Grading",
                "Every ended exam session, waiting to be graded or already graded. Nothing reaches a student until a final grade is approved and published."),
            new("grading-filters", "Filter the queue",
                "Show one status at a time. Include ended sessions not started also lists sessions where grading has not begun yet."),
            new("grading-table", "Open a session",
                "Start grading runs the AI's preliminary grading; Open takes you to the grading page. The Attention column flags disagreements to reconcile, open appeals and AI grading that failed."),
            Replay,
        ]),

        new(TourIds.GradingSession, 1, "Grading a session", "grading",
        [
            new("grading-summary", "Where this attempt stands",
                "The grading status, the combined grade, how the policy weighs the AI and human scores, and links to the report and transcript."),
            new("grading-evaluate", "Your evaluation",
                "For each objective choose a level and a score, and add a comment and the evidence you relied on. Then submit all objectives together. With blind grading, the AI's evaluation stays hidden until you submit."),
            new("grading-compare", "Score comparison",
                "Once both are in, the AI and human scores are compared objective by objective. A difference at or above the policy's threshold must be reconciled, with a written resolution, before the grade can be approved."),
            new("grading-approve", "Approve, then publish",
                "An instructor or admin approves the final grade and then publishes it to the student. Normally the person who entered the human score cannot also approve it."),
            new("grading-objectives", "Objectives and rubrics",
                "Each objective with its rubric, and the AI's preliminary evaluation once it may be shown."),
            new("grading-transcript", "Transcript",
                "Every response the student gave. Words the student corrected after speech recognition misheard them are marked; a correction is a recording fix, never a new answer."),
            Replay,
        ], ReplayHint: "Open any session from the grading queue, then press Show me around."),

        new(TourIds.ReviewSession, 1, "Reviewing a session", "review",
        [
            new(".review-summary", "The session at a glance",
                "When it started and ended, the AI's recommendation (hidden during blind grading), a link to the grading workspace, and downloads of the turns, report and transcript."),
            new(".integrity-summary", "Integrity notes",
                "Signals such as looking away or reading are low-confidence notes for you to weigh, never a verdict and never part of the grade. During blind grading they stay hidden until you have submitted your scores."),
            new(".timeline", "Timeline",
                "Every question and answer in order, with how the judging panel scored each answer and any integrity notes from that moment."),
            Replay,
        ], ReplayHint: "Open any session from the review list, then press Show me around."),

        new(TourIds.MyExams, 1, "My exams", "my-exams",
        [
            new(null, "My exams",
                "Every exam you have been given is here. Press Next to look around, or Esc to stop at any time."),
            new("my-exams-item", "Your exam",
                "Each card shows the exam, whether it is open, when it can be taken and how many attempts you have used. Your instructor's instructions appear here too."),
            new("my-exams-start", "Start or resume",
                "Start takes you to the get-ready page: a quick check of your camera and microphone, how the exam works, and a notice to agree to. The exam only begins when you press Begin exam there. Resume takes you back to an exam you left part-way."),
            new(".exam-item .result", "Your result",
                "When an exam has been graded and published, your grade and a link to the full report appear here."),
            new("my-exams-data", "Your data",
                "Download everything this organization keeps about you, as a file."),
            Replay,
        ]),

        // The get-ready page runs its system check (camera, microphone) on its own; the tour only points at it. It waits a
        // little longer before it starts so the check is already under way, and it never touches the camera or microphone.
        new(TourIds.GetReady, 1, "Getting ready", "my-exams",
        [
            new("ready-steps", "Three steps",
                "Work down the page: the system check, how the exam works, then the notice. The exam has not started yet, so take your time."),
            new("ready-check", "System check",
                "Your camera picture and the microphone meter show that both work. Every line needs a tick before Begin exam turns on. If a line is marked ✗, follow the hint under it, then press Check again."),
            new("ready-practice", "Try a practice question",
                "Answer out loud, then press the button to see the kind of follow-up question the examiner asks. Nothing is recorded."),
            new("ready-consent", "Agree and begin",
                "Read the notice, tick the box and press Begin exam. Your time starts when the examiner asks the first question, not when you press the button."),
            Replay,
        ], StartDelayMs: 2000, ReplayHint: "Press Start on an exam in My exams, then press Show me around on the get-ready page."),
    ];

    private static readonly Dictionary<string, TourDefinition> ById = All.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    public static TourDefinition? Find(string? id) => id is not null && ById.TryGetValue(id.Trim(), out var t) ? t : null;

    /// <summary>
    /// The shape tour.js reads. <paramref name="userKey"/> keeps one account's "already shown" separate from another's on
    /// a shared computer; it is the account's id, never a name or email.
    /// </summary>
    public static object ToClient(TourDefinition tour, string userKey, bool autoStart) => new
    {
        id = tour.Id,
        version = tour.Version,
        title = tour.Title,
        userKey,
        auto = autoStart,
        delay = tour.StartDelayMs,
        steps = tour.Steps.Select(s => new { anchor = s.Anchor, title = s.Title, body = s.Body }).ToArray(),
    };
}
