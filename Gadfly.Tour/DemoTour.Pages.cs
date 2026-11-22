using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gadfly.Tour;

/// <summary>
/// The tour's stages for the pages and downloads added after the core workflow (GAD-T398, under GAD-T319): the admin area,
/// public site and help, the practice interview, opening questions and rubric approval, system readiness, transcripts,
/// PDF reports and certificates with their verification, anonymous second marking, separate reconciler and approver,
/// panel calibration, the bias-probe cost screen, research consent and export, and the analytics fairness views.
/// <para>
/// Many of these are interactive Blazor pages whose buttons the tour cannot press over plain HTTP. Where that is so, a stage
/// checks only what the server really renders (the page answers 200 for the right people, refuses the wrong ones, carries its
/// title and key text, and agrees with the database) and says so in its checklist line; it never claims a deeper check.
/// </para>
/// </summary>
public sealed partial class DemoTour
{
    private const string Suffix = " · Gadfly Exams";

    /// <summary>Stages that need only the seeded demo and signed-in users (run before the exam starts).</summary>
    private async Task RunPageStagesAsync(bool seeded, bool signedIn)
    {
        await _list.RunAsync("Public site pages answer with their titles (GAD-T308)", PublicSiteAsync);
        await _list.RunAsync("System readiness: /healthz/ready and the system status page (GAD-T302)", ReadinessAsync);
        if (!signedIn)
        {
            _list.Blocked("Help, admin, practice and assessment page stages", "sign-in failed");
            return;
        }
        await _list.RunAsync("Help centre: home, search, topics and role limits", HelpPagesAsync);
        await _list.RunAsync("Admin pages for the owner, refused to the student", AdminPagesAsync);
        await _list.RunAsync("LMS connection, plan and usage pages (/org/lti, /org/plan, /org/usage)", OrgPagesAsync);
        await _list.RunAsync("Practice interview opens and stores nothing (GAD-T294)", PracticeAsync);
        if (seeded) await _list.RunAsync("Opening questions and rubric approval on the assessment pages (GAD-T436, GAD-T437)", RubricApprovalAsync);
    }

    /// <summary>Stages that need the tour's graded, published session.</summary>
    private async Task RunAfterGradingStagesAsync()
    {
        await _list.RunAsync("Transcript downloads and /transcripts/verify (GAD-T295)", TranscriptsAsync);
        await _list.RunAsync("PDF reports and the completion certificate, verified (GAD-T311)", PdfAndCertificateAsync);
        await _list.RunAsync("Separate reconciler and approver (GAD-T444)", SeparateApproverAsync);
        await _list.RunAsync("Anonymous second marking pages (GAD-T428, GAD-T439)", SecondMarkingAsync);
        await _list.RunAsync("Panel calibration through the API and its page (GAD-T427, GAD-T469)", CalibrationAsync);
        await _list.RunAsync("Bias-probe cost screen, never run (GAD-T429)", BiasProbeScreenAsync);
        await _list.RunAsync("Research consent pages and the de-identified export (GAD-T451)", ResearchAsync);
        await _list.RunAsync("Analytics fairness views and CSV, owners only (GAD-T463)", FairnessAsync);
    }

    // ------------------------------------------------------------------ public site, readiness, help

    private async Task<string> PublicSiteAsync()
    {
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        (string Path, string? Title, string Text)[] pages =
        [
            ("/", null, "Exams that find out whether a student understands."),
            ("/how-it-works", "How it works" + Suffix, "How a Gadfly exam works"),
            ("/pricing", "Pricing and plans" + Suffix, "Pricing and plans"),
            ("/science", "The science behind Gadfly" + Suffix, "The science behind Gadfly"),
            ("/security", "Security" + Suffix, "Security"),
            ("/privacy", "Privacy notice" + Suffix, "Privacy notice"),
            ("/terms", "Terms of service" + Suffix, "Terms of service"),
            ("/accessibility", "Accessibility statement" + Suffix, "Accessibility statement"),
            ("/contact", "Request a demo" + Suffix, "Request a demo"),
        ];
        foreach (var (path, title, text) in pages)
            await ExpectPageAsync(anonymous, path, title, text);

        var (status, html, _) = await anonymous.GetAsync("/tour-no-such-page-" + Guid.NewGuid().ToString("N")[..6]);
        Expect.True(status == HttpStatusCode.NotFound, $"an unknown address answered {(int)status}, not 404");
        Expect.True(Decoded(html).Contains("There's no page at this address", StringComparison.Ordinal), "the 404 page is not the branded one");
        CheckEncoding("the 404 page", html);
        return $"{pages.Length} public pages answer 200 with their titles and headings; an unknown address gets the branded 404";
    }

    private async Task<string> ReadinessAsync()
    {
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        var (status, body) = await anonymous.GetJsonAsync("/healthz/ready");
        if (status == HttpStatusCode.NotFound) throw new NotBuiltException("GAD-T302", "/healthz/ready answered 404");
        Expect.True(status is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable && body.ValueKind == JsonValueKind.Object,
            $"/healthz/ready answered {(int)status} without its JSON");
        var state = Str(body, "status");
        Expect.True(status == HttpStatusCode.OK ? state == "ready" : state == "unready", $"/healthz/ready answered {(int)status} but says \"{state}\"");
        var checks = body.TryGetProperty("checks", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().ToList() : [];
        Expect.True(checks.Count > 0, "/healthz/ready lists no checks");
        var names = checks.Select(x => Str(x, "name")).ToList();
        Expect.True(names.Contains("database") && names.Contains("migrations"), $"/healthz/ready doesn't check the database and migrations ({string.Join(", ", names)})");
        var failing = checks.Where(x => Str(x, "state") == "failing").Select(x => Str(x, "name")).ToList();
        Expect.True(failing.Count == 0, $"the freshly started tour server is not ready: {string.Join(", ", failing)} failing");
        // Data-free: names and states only, nothing else on each check.
        Expect.True(checks.All(x => x.EnumerateObject().All(p => p.Name is "name" or "state")), "/healthz/ready carries more than each check's name and state");
        var raw = body.GetRawText();
        Expect.True(!raw.Contains("demo.", StringComparison.Ordinal) && !raw.Contains("Exception", StringComparison.Ordinal), "/healthz/ready carries user names or error text");

        var owner = _users.GetValueOrDefault("demo.owner");
        var page = "";
        if (owner is not null)
        {
            var html = await ExpectPageAsync(owner, "/admin/status", "System status" + Suffix, "System status");
            var text = Decoded(html);
            Expect.True(text.Contains("Checks", StringComparison.Ordinal) && text.Contains("/healthz/ready", StringComparison.Ordinal),
                "the system status page doesn't show its checks or mention /healthz/ready");
            Expect.True(text.Contains("Ready: everything an exam needs is working.", StringComparison.Ordinal),
                "the system status page doesn't say Ready although /healthz/ready does");
            Expect.True(text.Contains("App, AI and voice", StringComparison.Ordinal) && text.Contains("Database and background jobs", StringComparison.Ordinal),
                "the system status page lacks its app/AI/voice or database/jobs section");
            await ExpectRefusedAsync("demo.student", "/admin/status");
            page = "; the owner's system status page says Ready with the same checks; the student is refused";
        }
        return $"/healthz/ready {(int)status} \"{state}\" with {checks.Count} checks ({string.Join(", ", names)}), names and states only{page}";
    }

    private async Task<string> HelpPagesAsync()
    {
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        await ExpectPageAsync(anonymous, "/help", "Help" + Suffix, "Help");
        var search = await ExpectPageAsync(anonymous, "/help/search?q=microphone", "microphone · Help search" + Suffix, "Search help");
        Expect.True(search.Contains("troubleshooting-microphone", StringComparison.Ordinal), "a help search for \"microphone\" doesn't find the microphone topic");
        var topic = await ExpectPageAsync(anonymous, "/help/how-the-exam-works", null, null);
        Expect.True(!Decoded(topic).Contains("Topic not found", StringComparison.Ordinal) && !Decoded(topic).Contains("Sign in to read this topic", StringComparison.Ordinal),
            "the public topic how-the-exam-works is not shown to an anonymous visitor");
        var locked = Decoded((await anonymous.GetAsync("/help/student-guide")).Body);
        Expect.True(locked.Contains("Sign in to read this topic", StringComparison.Ordinal), "the student guide is shown to an anonymous visitor");
        var missing = Decoded((await anonymous.GetAsync("/help/no-such-topic-tour")).Body);
        Expect.True(missing.Contains("Topic not found", StringComparison.Ordinal), "an unknown help topic doesn't say it was not found");

        var student = _users["demo.student"];
        var forStudent = Decoded((await student.GetAsync("/help/practice-interview")).Body);
        Expect.True(!forStudent.Contains("This topic is for another role", StringComparison.Ordinal) && !forStudent.Contains("Topic not found", StringComparison.Ordinal),
            "the student can't read the practice-interview topic");
        var adminTopic = Decoded((await student.GetAsync("/help/admin-guide")).Body);
        Expect.True(adminTopic.Contains("This topic is for another role", StringComparison.Ordinal), "the student can read the admin guide");
        var ownerTopic = Decoded((await _users["demo.owner"].GetAsync("/help/admin-guide")).Body);
        Expect.True(!ownerTopic.Contains("This topic is for another role", StringComparison.Ordinal), "the owner can't read the admin guide");
        return "help home, search (finds the microphone topic) and a public topic for anonymous visitors; the student guide asks to sign in; " +
               "an unknown topic says not found; the student reads student topics but not the admin guide, the owner reads it";
    }

    // ------------------------------------------------------------------ admin area

    private async Task<string> AdminPagesAsync()
    {
        var owner = _users["demo.owner"];
        var dashboard = Decoded(await ExpectPageAsync(owner, "/admin", "Dashboard" + Suffix, "Dashboard"));
        Expect.True(dashboard.Contains("Needs attention", StringComparison.Ordinal) && dashboard.Contains("Quick actions", StringComparison.Ordinal),
            "the dashboard lacks its Needs attention or Quick actions section");
        var students = Decoded(await ExpectPageAsync(owner, "/admin/students", "Students" + Suffix, "Students"));
        Expect.True(students.Contains("demo.student", StringComparison.Ordinal), "the students directory doesn't list demo.student");
        var studentId = Expect.Value(await UserIdAsync("demo.student"), "no demo.student account");
        var profile = Decoded(await ExpectPageAsync(owner, $"/admin/students/{studentId:D}", null, null));
        Expect.True(profile.Contains("demo.student", StringComparison.Ordinal), "the student's profile page doesn't name the student");

        var exams = await ExpectPageAsync(owner, "/admin/exams", "Exams" + Suffix, "Exams");
        Expect.True(Decoded(exams).Contains(MathExam, StringComparison.Ordinal), "the exams manager doesn't list the multiplication exam");
        Expect.True(exams.Contains($"admin/exams/{_mathExamId:D}/preview", StringComparison.OrdinalIgnoreCase) || exams.Contains($"admin/exams/{_mathExamId}/preview", StringComparison.OrdinalIgnoreCase),
            "the exams manager has no preview link for the multiplication exam");
        // The preview runs the practice panel after the page connects (OnAfterRender), so over HTTP only the page itself is checked.
        await ExpectPageAsync(owner, $"/admin/exams/{_mathExamId:D}/preview", "Preview" + Suffix, "Preview");
        var (exportStatus, exportBytes, _, _) = await owner.DownloadAsync($"/admin/exams/{_mathExamId:D}/export.json");
        Expect.True(exportStatus == HttpStatusCode.OK && Encoding.UTF8.GetString(exportBytes).Contains(MathExam, StringComparison.Ordinal),
            $"the exam definition export answered {(int)exportStatus} without the exam");

        await ExpectPageAsync(owner, "/admin/assignments", "Assignments" + Suffix, "Assignments");
        await ExpectPageAsync(owner, "/admin/live", "Live sessions" + Suffix, "Live sessions");
        var audit = await ExpectPageAsync(owner, "/admin/audit", "Audit log" + Suffix, "Audit log");
        Expect.True(audit.Contains("demo.seeded", StringComparison.Ordinal), "the audit log page doesn't show the demo.seeded events");
        var (auditCsvStatus, auditCsv, _, _) = await owner.DownloadAsync("/admin/audit/export.csv");
        Expect.True(auditCsvStatus == HttpStatusCode.OK && Encoding.UTF8.GetString(auditCsv).Contains("demo.seeded", StringComparison.Ordinal),
            $"the audit CSV export answered {(int)auditCsvStatus} without the demo.seeded events");
        var email = Decoded(await ExpectPageAsync(owner, "/admin/email-preview", "Email preview" + Suffix, "Email preview"));
        Expect.True(email.Contains("Recent sends", StringComparison.Ordinal), "the email preview page lacks its Recent sends list");

        string[] adminOnly = ["/admin", "/admin/students", "/admin/exams", "/admin/assignments", "/admin/live", "/admin/email-preview"];
        foreach (var path in adminOnly) await ExpectRefusedAsync("demo.student", path);
        foreach (var path in new[] { "/admin", "/admin/students" }) await ExpectRefusedAsync("demo.examiner", path);
        var (studentAudit, _, _, _) = await _users["demo.student"].DownloadAsync("/admin/audit/export.csv");
        Expect.True(studentAudit != HttpStatusCode.OK, "the student could download the audit log");
        return "dashboard, students (lists demo.student) and a student profile, exams manager with its preview link, the preview page and the definition export, " +
               "assignments, live sessions, audit log (demo.seeded shown and exported), email preview; the student and examiner are refused " +
               "(preview, live and assignment actions are interactive: only the pages are checked)";
    }

    private async Task<string> OrgPagesAsync()
    {
        var owner = _users["demo.owner"];
        var lti = Decoded(await ExpectPageAsync(owner, "/org/lti", "LMS connection" + Suffix, "LMS connection (LTI 1.3)"));
        Expect.True(lti.Contains("Step 1: Gadfly's addresses, to enter in your LMS", StringComparison.Ordinal) && lti.Contains("Connections", StringComparison.Ordinal),
            "the LMS connection page lacks the tool's addresses or the connections list");
        var plan = Decoded(await ExpectPageAsync(owner, "/org/plan", "Plan & usage" + Suffix, "Plan & usage"));
        Expect.True(plan.Contains(" plan", StringComparison.Ordinal) && plan.Contains("Usage export", StringComparison.Ordinal), "the plan page doesn't show the plan or the usage export");
        await ExpectPageAsync(owner, "/org/usage", "Usage" + Suffix, "Usage");
        var settings = Decoded(await ExpectPageAsync(owner, "/org/settings", null, null));
        Expect.True(settings.Contains("whoever reconciled a grade", StringComparison.Ordinal), "the organization settings don't offer the reconciler-approver rule");
        foreach (var path in new[] { "/org/lti", "/org/plan", "/org/usage", "/org/settings" }) await ExpectRefusedAsync("demo.student", path);
        await ExpectRefusedAsync("demo.teacher", "/org/lti");
        return "LMS connection (addresses, connections), plan & usage with its export, usage and settings (reconciler-approver rule offered) for the owner; " +
               "refused to the student, LTI refused to the teacher (adding a connection is interactive and not driven)";
    }

    // ------------------------------------------------------------------ practice interview, opening questions, rubric approval

    private async Task<string> PracticeAsync()
    {
        var student = _users["demo.student"];
        const string countSql = """
            SELECT (SELECT COUNT(*) FROM gadfly.ExamSessions WHERE StudentUserId = @s)
                 + (SELECT COUNT(*) FROM gadfly.AuditEvents WHERE ActorUserId = @s)
                 + (SELECT COUNT(*) FROM gadfly.IntegrityEvents i JOIN gadfly.ExamSessions x ON x.Id = i.SessionId WHERE x.StudentUserId = @s)
            """;
        var before = await _db.CountAsync(countSql, ("@s", _studentId), ("@org", _orgId));
        var (myStatus, myExams, _) = await student.GetAsync("/my-exams");
        Expect.True(myStatus == HttpStatusCode.OK && myExams.Contains("my-exams/practice", StringComparison.Ordinal), "My exams doesn't link to the practice interview");
        var page = Decoded(await ExpectPageAsync(student, "/my-exams/practice", "Meet your examiner (practice)" + Suffix, "Meet your examiner"));
        Expect.True(page.Contains("Practice", StringComparison.Ordinal), "the practice page isn't marked as practice");
        var after = await _db.CountAsync(countSql, ("@s", _studentId), ("@org", _orgId));
        Expect.True(after == before, $"opening the practice interview stored {after - before} row(s) (sessions, audit events or integrity events)");
        return "linked from My exams; the page opens for the student and no session, audit event or integrity event was written; " +
               "the spoken practice itself needs a browser with a microphone and is not driven";
    }

    private async Task<string> RubricApprovalAsync()
    {
        var teacher = _users["demo.teacher"];
        var exam = (await _db.RowsAsync("""
            SELECT e.PolicyJson, e.AssessmentMode, e.Status, c.ConceptMapVersion
            FROM gadfly.Exams e JOIN gadfly.SourceCollections c ON c.Id = e.CollectionId WHERE e.Id = @e
            """, ("@e", _mathExamId))).Single();
        var version = Convert.ToInt32(exam["ConceptMapVersion"]);
        int? approved = null;
        using (var policy = JsonDocument.Parse((string)exam["PolicyJson"]!))
            if (policy.RootElement.TryGetProperty("rubricApproval", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("conceptMapVersion", out var v))
                approved = v.GetInt32();

        var objectives = Decoded(await ExpectPageAsync(teacher, $"/assessments/{_mathExamId:D}/objectives", null, null));
        Expect.True(objectives.Contains("Fixed opening question", StringComparison.Ordinal) || objectives.Contains("Opening question:", StringComparison.Ordinal),
            "the objectives step doesn't show each objective's opening question");
        var review = Decoded(await ExpectPageAsync(teacher, $"/assessments/{_mathExamId:D}/review", null, null));
        Expect.True(review.Contains("Concept map and rubric", StringComparison.Ordinal), "the review step has no concept map and rubric approval panel");
        var mode = exam["AssessmentMode"]?.ToString();
        string state;
        if (approved == version)
        {
            Expect.True(review.Contains($"Version {version} approved by", StringComparison.Ordinal), $"version {version} is approved in the database but the page doesn't say so");
            state = $"version {version} approved";
        }
        else if (approved is { } old)
        {
            Expect.True(review.Contains($"Version {old} was approved by", StringComparison.Ordinal), $"the approval of version {old} is stale (now {version}) but the page doesn't say so");
            state = $"approval of version {old} stale (now {version})";
        }
        else if (mode == "Examination")
        {
            Expect.True(review.Contains("published before approval was required", StringComparison.Ordinal),
                "the published examination has no approval but the page doesn't say it was published before approval was required");
            Expect.True(review.Contains($"Approve version {version}", StringComparison.Ordinal), $"the page offers no button to approve version {version}");
            state = $"published before approval was required; Approve version {version} offered";
        }
        else state = $"{mode} mode: approval optional";
        await ExpectRefusedPageTextAsync("demo.student", $"/assessments/{_mathExamId:D}/review", "Concept map and rubric");
        return $"the objectives step shows the opening question per objective; the review step's approval panel agrees with the database ({state}). " +
               "Drafting/approving an opening question and approving the rubric are interactive buttons and are not driven; the demo exams are " +
               "seeded already published, so the tour never meets the publish gate";
    }

    // ------------------------------------------------------------------ transcripts, reports, certificates

    private static readonly string[] TranscriptFormats = ["json", "pdf", "docx", "html", "txt", "vtt", "srt"];

    private async Task<string> TranscriptsAsync()
    {
        var teacher = _users["demo.teacher"];
        string? jsonCode = null;
        byte[] json = [];
        foreach (var f in TranscriptFormats)
        {
            var (status, bytes, type, headers) = await teacher.DownloadAsync($"/transcripts/{_sessionId:D}/{f}");
            Expect.True(status == HttpStatusCode.OK && bytes.Length > 0, $"the {f} transcript answered {(int)status}");
            Expect.True(headers.TryGetValue("X-Transcript-Code", out var code) && code.Length > 0, $"the {f} transcript has no verification code header");
            switch (f)
            {
                case "pdf": Expect.True(StartsWith(bytes, "%PDF"), "the PDF transcript is not a PDF"); break;
                case "docx": Expect.True(StartsWith(bytes, "PK"), "the Word transcript is not a .docx (zip)"); break;
                case "vtt": Expect.True(Encoding.UTF8.GetString(bytes).TrimStart('﻿').StartsWith("WEBVTT", StringComparison.Ordinal), "the WebVTT transcript has no WEBVTT header"); break;
                case "html": Expect.True(Encoding.UTF8.GetString(bytes).Contains(WebUtility.HtmlEncode(MathExam), StringComparison.Ordinal) || Encoding.UTF8.GetString(bytes).Contains(MathExam, StringComparison.Ordinal), "the print-ready transcript doesn't name the exam"); break;
                case "json":
                    jsonCode = code; json = bytes;
                    Expect.True(type.Contains("json", StringComparison.OrdinalIgnoreCase), $"the JSON transcript came as {type}");
                    using (JsonDocument.Parse(bytes)) { }
                    break;
            }
        }
        var (bad, _, _, _) = await teacher.DownloadAsync($"/transcripts/{_sessionId:D}/exe");
        Expect.True(bad == HttpStatusCode.NotFound, $"an unknown transcript format answered {(int)bad}");

        // The student's copy, once the result is released, names no reviewer.
        var (sStatus, sBytes, _, _) = await _users["demo.student"].DownloadAsync($"/transcripts/{_sessionId:D}/json");
        Expect.True(sStatus == HttpStatusCode.OK, $"the student's transcript answered {(int)sStatus} after publication");
        var studentText = Encoding.UTF8.GetString(sBytes);
        Expect.True(!studentText.Contains("demo.examiner", StringComparison.Ordinal) && !studentText.Contains("demo.reviewer", StringComparison.Ordinal),
            "the student's transcript names a reviewer");
        var issued = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.AuditEvents WHERE SessionId = @s AND Action LIKE 'transcript.%'", ("@s", _sessionId));
        Expect.True(issued >= TranscriptFormats.Length + 1, $"only {issued} transcript audit events for {TranscriptFormats.Length + 1} downloads");

        // Anyone can check it: by code, by the JSON file, and an edited file or a made-up code are caught.
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        var (vStatus, blank, _) = await anonymous.GetAsync("/transcripts/verify");
        Expect.True(vStatus == HttpStatusCode.OK && blank.Contains("Check a Gadfly transcript or certificate", StringComparison.Ordinal), $"/transcripts/verify answered {(int)vStatus}");
        var byCode = Decoded((await anonymous.GetAsync($"/transcripts/verify?code={Uri.EscapeDataString(jsonCode!)}")).Body);
        Expect.True(byCode.Contains("Genuine and unchanged.", StringComparison.Ordinal), $"the code {jsonCode} doesn't verify as genuine and unchanged");
        Expect.True(!byCode.Contains(MathExam, StringComparison.Ordinal), "the verify page shows what the transcript is about");
        var byFile = Decoded(await PostVerifyFileAsync(anonymous, json, "transcript.json"));
        Expect.True(byFile.Contains("Genuine and unchanged.", StringComparison.Ordinal), "the downloaded JSON file doesn't verify as genuine and unchanged");
        var tampered = Decoded(await PostVerifyFileAsync(anonymous, Tamper(json), "transcript-edited.json"));
        Expect.True(tampered.Contains("Changed after it was issued.", StringComparison.Ordinal), "an edited JSON file is not reported as changed after it was issued");
        var madeUp = Decoded((await anonymous.GetAsync("/transcripts/verify?code=AAAA-BBBB-CCCC-DDDD")).Body);
        Expect.True(madeUp.Contains("Not on record.", StringComparison.Ordinal) || madeUp.Contains("Not readable.", StringComparison.Ordinal), "a made-up code is not refused");
        return $"{TranscriptFormats.Length} formats ({string.Join(", ", TranscriptFormats)}) download with their code (PDF, .docx, WebVTT and JSON checked by content), " +
               $"an unknown format is 404, the student's copy names no reviewer, {issued} downloads audited; /transcripts/verify: the code {jsonCode} and the JSON file " +
               "are genuine and unchanged, an edited file is caught, a made-up code is refused, and the page never shows the content";
    }

    private async Task<string> PdfAndCertificateAsync()
    {
        var teacher = _users["demo.teacher"];
        var student = _users["demo.student"];
        var (r1, report, _, _) = await teacher.DownloadAsync($"/grading/{_sessionId:D}/report.pdf");
        Expect.True(r1 == HttpStatusCode.OK && StartsWith(report, "%PDF"), $"the reviewer's PDF report answered {(int)r1}");
        var (r2, mine, _, _) = await student.DownloadAsync($"/my-results/{_sessionId:D}/report.pdf");
        Expect.True(r2 == HttpStatusCode.OK && StartsWith(mine, "%PDF"), $"the student's PDF report answered {(int)r2}");
        var (r3, _, _, _) = await student.DownloadAsync($"/grading/{_sessionId:D}/report.pdf");
        Expect.True(r3 != HttpStatusCode.OK, "the student could download the reviewer's PDF report");
        var (r4, summary, _, _) = await teacher.DownloadAsync($"/grading/exam/{_mathExamId:D}/class-summary.pdf");
        Expect.True(r4 == HttpStatusCode.OK && StartsWith(summary, "%PDF"), $"the class summary PDF answered {(int)r4}");
        var (r5, _, _, _) = await student.DownloadAsync($"/grading/exam/{_mathExamId:D}/class-summary.pdf");
        Expect.True(r5 != HttpStatusCode.OK, "the student could download the class summary");
        var page = Decoded(await ExpectPageAsync(teacher, $"/grading/{_sessionId:D}/report", "Assessment report" + Suffix, "Assessment report"));
        Expect.True(page.Contains("reviewer view", StringComparison.Ordinal), "the assessment report page is not the reviewer view");

        var (c1, certificate, _, headers) = await student.DownloadAsync($"/certificates/{_sessionId:D}/pdf");
        Expect.True(c1 == HttpStatusCode.OK && StartsWith(certificate, "%PDF"), $"the student's completion certificate answered {(int)c1}");
        var code = headers.GetValueOrDefault("X-Certificate-Code") ?? "";
        Expect.True(code.Length > 0, "the certificate has no verification code header");
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        var verified = Decoded((await anonymous.GetAsync($"/transcripts/verify?code={Uri.EscapeDataString(code)}")).Body);
        Expect.True(verified.Contains("Genuine and unchanged.", StringComparison.Ordinal) && verified.Contains("certificate", StringComparison.Ordinal),
            $"the certificate code {code} doesn't verify as a genuine certificate");
        var (c2, _, _, _) = await anonymous.DownloadAsync($"/certificates/{_sessionId:D}/pdf");
        Expect.True(c2 != HttpStatusCode.OK, "an anonymous visitor could download the certificate");
        return $"reviewer and student PDF reports, class summary PDF (student refused both reviewer downloads), report page; the student's certificate PDF " +
               $"verifies by its code {code} on /transcripts/verify; anonymous refused the certificate";
    }

    // ------------------------------------------------------------------ grading: separation, second marking, calibration, bias

    private async Task<string> SeparateApproverAsync()
    {
        EnsureGraded();
        var row = (await _db.RowsAsync("""
            SELECT ru.UserName AS Reconciler, su.UserName AS Second,
                   (SELECT TOP 1 au.UserName FROM gadfly.ApprovalRecords a JOIN auth.AuthUsers au ON au.Id = a.ActorUserId
                    WHERE a.SessionId = @s AND a.Action = 'Finalize' ORDER BY a.At) AS Approver
            FROM gadfly.GradeReconciliations r JOIN gadfly.GradeDisagreements d ON d.Id = r.DisagreementId
            JOIN auth.AuthUsers ru ON ru.Id = r.ReviewerUserId LEFT JOIN auth.AuthUsers su ON su.Id = r.SecondReviewerUserId
            WHERE d.SessionId = @s
            """, ("@s", _sessionId))).FirstOrDefault() ?? throw new TourAssertException("blocked: no reconciliation recorded for the tour's session");
        var approver = row["Approver"] as string ?? throw new TourAssertException("blocked: the grade was never approved");
        Expect.True(approver != (string)row["Reconciler"]! && approver != row["Second"] as string,
            $"{approver} approved a grade they reconciled (reconciler {row["Reconciler"]}, second reviewer {row["Second"] ?? "none"})");
        var waived = await AuditCountAsync("grade.reconciler-approver.waived");
        var refused = await AuditCountAsync("grade.reconciler-approver.refused");
        Expect.True(waived == 0 && refused == 0, $"the reconciler-approver rule recorded {waived} waiver(s) and {refused} refusal(s) although the approver did not reconcile");
        var setting = await _db.ScalarAsync<string>("SELECT TOP 1 CAST(ApproverSeparation AS nvarchar(40)) FROM gadfly.OrganizationSettings WHERE OrganizationId = @o", ("@o", _orgId));
        return $"reconciled by {row["Reconciler"]}, approved by {approver}: different people, no waiver or refusal recorded (setting {setting ?? "default Automatic"}). " +
               "A reconciler trying to approve is not driven: the demo's second reviewer lacks the approve permission anyway";
    }

    private async Task<string> SecondMarkingAsync()
    {
        var code = $"Session {_sessionId.ToString("N").ToUpperInvariant()[..4]}-{_sessionId.ToString("N").ToUpperInvariant()[4..8]}";
        var reviewer = _users["demo.reviewer"];
        var queue = Decoded(await ExpectPageAsync(reviewer, $"/grading/second-mark/{_mathExamId:D}", "Second marking" + Suffix, "Second marking"));
        Expect.True(queue.Contains("You mark each session blind", StringComparison.Ordinal), "the second marker's queue doesn't open for demo.reviewer");
        Expect.True(!queue.Contains("demo.student", StringComparison.Ordinal), "the second-marking queue names the student");
        var doubleMarking = !queue.Contains("This exam is not set for double marking.", StringComparison.Ordinal);
        if (doubleMarking) Expect.True(queue.Contains(code, StringComparison.Ordinal), $"the queue doesn't list the tour's session by its code {code}");

        var sheet = Decoded(await ExpectPageAsync(reviewer, $"/grading/second-mark/session/{_sessionId:D}", "Second marking" + Suffix, "Second marking"));
        Expect.True(!sheet.Contains("demo.student", StringComparison.Ordinal), "the second-marking sheet names the student");
        var refusedSheet = sheet.Contains("not set for double marking", StringComparison.Ordinal)
            || sheet.Contains("cannot mark it blind", StringComparison.Ordinal) || sheet.Contains("cannot also be its second marker", StringComparison.Ordinal);
        // demo.reviewer reconciled this grade and saw it, so a blind second mark must be refused whatever the setting.
        Expect.True(refusedSheet, "demo.reviewer, who has seen this session's grades, was given a blind second-marking sheet");

        var examinerSheet = Decoded((await _users["demo.examiner"].GetAsync($"/grading/second-mark/session/{_sessionId:D}")).Body);
        Expect.True(!examinerSheet.Contains("Blind second mark.", StringComparison.Ordinal), "the first examiner was given a second-marking sheet for their own session");
        var ownerQueue = Decoded((await _users["demo.owner"].GetAsync($"/grading/second-mark/{_mathExamId:D}")).Body);
        Expect.True(ownerQueue.Contains("your role does not include grading", StringComparison.Ordinal), "the owner (who does not grade) was shown the second-marking queue");
        return $"queue opens for the second reviewer and never names the student ({(doubleMarking ? $"lists {code}" : "double marking is off for the demo exam")}); " +
               "the sheet is refused to the reviewer who saw the grades and to the first examiner; the owner is refused. " +
               "Turning double marking on and recording marks are interactive and are not driven";
    }

    private async Task<string> CalibrationAsync()
    {
        var owner = _users["demo.owner"];
        var statsExamId = Expect.Value(await _db.ScalarAsync<Guid?>("SELECT Id FROM gadfly.Exams WHERE OrganizationId = @o AND Title = @t", ("@o", _orgId), ("@t", StatsExam)),
            "no statistics exam");
        // 30 double-marked answers (the minimum) on the statistics exam, which the tour never examines on, so the gate it may set touches nothing.
        var items = Enumerable.Range(0, 30).Select(i => new { itemId = $"tour-{i + 1}", panelLevel = 1 + i % 5, humanALevel = 1 + i % 5, humanBLevel = 1 + (i + (i % 7 == 0 ? 1 : 0)) % 5 }).ToList();
        var token = "/grading/actions/antiforgery";
        var (teacherPost, _) = await _users["demo.teacher"].PostJsonAsync($"/grading/calibration/{statsExamId:D}", new { panel = "tour", items }, token);
        Expect.True(teacherPost == HttpStatusCode.Forbidden, $"the teacher (no policy approval permission) recording a calibration answered {(int)teacherPost}, not 403");
        var (status, reply) = await owner.PostJsonAsync($"/grading/calibration/{statsExamId:D}", new { panel = "Tour calibration", items }, token);
        if (status == HttpStatusCode.NotFound) throw new NotBuiltException("GAD-T427", "/grading/calibration is not mapped");
        Expect.True(status == HttpStatusCode.OK, $"recording a calibration answered {(int)status}: {Str(reply, "message")}");
        if (Mock)
            Expect.True(reply.TryGetProperty("accepted", out var acc) && !acc.GetBoolean(), "a calibration recorded on the mock panel was accepted");

        var (getStatus, cal) = await owner.GetJsonAsync($"/grading/calibration/{statsExamId:D}");
        Expect.True(getStatus == HttpStatusCode.OK, $"reading the calibration answered {(int)getStatus}");
        var prompt = cal.TryGetProperty("gradingPromptVersion", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : 0;
        Expect.True(prompt >= 1, "the calibration doesn't record the grading prompt-set version");
        Expect.True(cal.TryGetProperty("expired", out var ex) && !ex.GetBoolean(), "a calibration recorded a moment ago is already expired");
        var page = Decoded(await ExpectPageAsync(owner, $"/grading/panel/{statsExamId:D}", "Panel calibration" + Suffix, "Panel calibration"));
        Expect.True(page.Contains("Current calibration", StringComparison.Ordinal) && page.Contains($"version {prompt} when recorded", StringComparison.Ordinal),
            $"the calibration page doesn't show the recording's grading prompt version {prompt}");
        await ExpectPageAsync(owner, "/grading/panel", "Panel calibration" + Suffix, "Panel calibration");
        await ExpectPageAsync(owner, $"/grading/self-preference/{_mathExamId:D}", "Self-preference check" + Suffix, "Self-preference check");
        await ExpectRefusedPageTextAsync("demo.student", $"/grading/panel/{statsExamId:D}", "Current calibration");
        return $"the teacher is refused (403); the owner records 30 answers on the statistics exam ({(Mock ? "not accepted: mock panel" : $"accepted {reply.GetProperty("accepted")}")}); " +
               $"read back with grading prompt version {prompt}, not expired; the calibration page shows it; the index and self-preference pages render";
    }

    private async Task<string> BiasProbeScreenAsync()
    {
        var owner = _users["demo.owner"];
        const string runsSql = "SELECT (SELECT COUNT(*) FROM gadfly.Jobs WHERE OrganizationId = @o AND Kind = 'grading.bias-probe') + (SELECT COUNT(*) FROM gadfly.AuditEvents WHERE OrganizationId = @o AND Action = 'panel.bias-probe.requested')";
        var before = await _db.CountAsync(runsSql, ("@o", _orgId));
        var page = Decoded(await ExpectPageAsync(owner, $"/grading/bias/{_mathExamId:D}", "Bias probes" + Suffix, "Bias probes"));
        Expect.True(page.Contains("Panel probe", StringComparison.Ordinal), "the bias-probe page has no panel probe section");
        Expect.True(page.Contains("The run starts only when you start it", StringComparison.Ordinal), "the bias-probe page doesn't say runs start only by hand");
        string cost;
        if (page.Contains("nothing to probe", StringComparison.Ordinal)) cost = "no graded answer to probe yet";
        else
        {
            Expect.True(page.Contains("Estimated provider cost", StringComparison.Ordinal), "the bias-probe page shows no cost estimate before a run");
            if (Mock) Expect.True(page.Contains("none (mock panel)", StringComparison.Ordinal), "in mock mode the cost screen doesn't say the mock panel costs nothing");
            cost = Mock ? "estimated cost: none (mock panel)" : "estimated cost shown";
        }
        var after = await _db.CountAsync(runsSql, ("@o", _orgId));
        Expect.True(after == before, "opening the bias-probe page started a probe run");
        return $"the owner's cost screen renders ({cost}); the tour never confirms a run and none was queued";
    }

    // ------------------------------------------------------------------ research, fairness

    private async Task<string> ResearchAsync()
    {
        var owner = _users["demo.owner"];
        var admin = Decoded(await ExpectPageAsync(owner, "/org/research", "Research" + Suffix, "Research"));
        Expect.True(admin.Contains("Research consent", StringComparison.Ordinal) && admin.Contains("Study arms", StringComparison.Ordinal),
            "the research page lacks its consent or study arms section");
        var mine = Decoded(await ExpectPageAsync(_users["demo.student"], "/my-research", "My research choice" + Suffix, "My research choice"));
        var running = !mine.Contains("isn't running any research", StringComparison.Ordinal);
        await ExpectRefusedAsync("demo.student", "/org/research");

        var (status, zip, _, _) = await owner.DownloadAsync("/admin/research/export.zip");
        string export;
        if (status == HttpStatusCode.NotFound)
        {
            Expect.True(Encoding.UTF8.GetString(zip).Contains("Research is not set up", StringComparison.Ordinal), "the research export answered 404 without saying research is not set up");
            export = "export refused with \"Research is not set up\" (research is off in the demo)";
        }
        else
        {
            Expect.True(status == HttpStatusCode.OK && StartsWith(zip, "PK"), $"the research export answered {(int)status}");
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var studentId = _studentId.ToString();
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = await reader.ReadToEndAsync();
                Expect.True(!text.Contains("demo.student", StringComparison.OrdinalIgnoreCase) && !text.Contains(studentId, StringComparison.OrdinalIgnoreCase),
                    $"the research export's {entry.FullName} identifies the student");
            }
            Expect.True(await _db.CountAsync("SELECT COUNT(*) FROM gadfly.AuditEvents WHERE OrganizationId = @o AND Action = 'research.exported'", ("@o", _orgId)) >= 1,
                "the research export was not audited");
            export = $"export downloaded ({archive.Entries.Count} files), no student name or id in it, audited";
        }
        foreach (var who in new[] { "demo.teacher", "demo.student" })
        {
            var (s, _, _, _) = await _users[who].DownloadAsync("/admin/research/export.zip");
            Expect.True(s != HttpStatusCode.OK, $"{who} could download the research export");
        }
        return $"owner's research page (consent, study arms); the student's research choice page ({(running ? "research offered" : "says no research is running")}); {export}; " +
               "teacher and student refused the export. Giving or withdrawing consent is interactive and is not driven";
    }

    private async Task<string> FairnessAsync()
    {
        var owner = _users["demo.owner"];
        var teacher = _users["demo.teacher"];
        var ownerPage = Decoded((await owner.GetAsync("/analytics")).Body);
        Expect.True(ownerPage.Contains("Fairness by group", StringComparison.Ordinal), "the owner's analytics page has no fairness section");
        var teacherPage = Decoded((await teacher.GetAsync("/analytics")).Body);
        Expect.True(teacherPage.Contains("Analytics", StringComparison.Ordinal) && !teacherPage.Contains("Fairness by group", StringComparison.Ordinal),
            "the teacher (who grades blind) is shown the fairness section");

        var (s1, ownerCsv, _, _) = await owner.DownloadAsync($"/analytics/export.csv?exam={_mathExamId:D}");
        var csv = Encoding.UTF8.GetString(ownerCsv);
        Expect.True(s1 == HttpStatusCode.OK && csv.Contains("section,completion", StringComparison.Ordinal), $"the owner's analytics CSV answered {(int)s1}");
        string[] sections = ["fairness_score_gaps", "fairness_path_drivers", "fairness_typed_vs_spoken"];
        var missing = sections.Where(s => !csv.Contains("section," + s, StringComparison.Ordinal)).ToList();
        Expect.True(missing.Count == 0, $"the owner's CSV lacks the fairness sections {string.Join(", ", missing)}");
        Expect.True(!csv.Contains("demo.student", StringComparison.Ordinal), "the analytics CSV names a student");
        var (s2, teacherCsv, _, _) = await teacher.DownloadAsync($"/analytics/export.csv?exam={_mathExamId:D}");
        Expect.True(s2 == HttpStatusCode.OK && !Encoding.UTF8.GetString(teacherCsv).Contains("section,fairness_", StringComparison.Ordinal),
            "the teacher's analytics CSV carries the fairness sections");
        await ExpectPageAsync(teacher, "/analytics/speech-reference", "Reference transcripts" + Suffix, "Reference transcripts");
        return $"the owner's page and CSV carry the fairness views ({string.Join(", ", sections)}), naming no student; the teacher's carry none; the speech reference page renders";
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>GETs a page and checks it answers 200 at that address, its title and heading (when given), and no mis-encoded text.</summary>
    private static async Task<string> ExpectPageAsync(BrowserUser user, string path, string? title, string? heading)
    {
        var (status, html, final) = await user.GetAsync(path);
        var wanted = path.Split('?')[0];
        Expect.True(status == HttpStatusCode.OK, $"{user.UserName}: {path} answered {(int)status}");
        Expect.True(string.Equals(final.TrimEnd('/'), wanted.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || wanted == "/",
            $"{user.UserName}: {path} ended at {final}");
        var m = TitleTag().Match(html);
        var actual = m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
        Expect.True(actual.Length > 0, $"{path} has no <title>");
        if (title is not null) Expect.True(actual == title, $"{path}: title \"{actual}\", expected \"{title}\"");
        if (heading is not null)
        {
            var h1 = H1Tag().Match(html);
            Expect.True(h1.Success && Decoded(Regex.Replace(h1.Groups[1].Value, "<[^>]+>", " ")).Contains(heading, StringComparison.Ordinal),
                $"{path}: the heading is not \"{heading}\"");
        }
        CheckEncoding(path, html);
        return html;
    }

    /// <summary>The page is refused to <paramref name="who"/>: 403/404, a redirect elsewhere, or an access-denied page.</summary>
    private async Task ExpectRefusedAsync(string who, string path)
    {
        var (status, _, final) = await _users[who].GetAsync(path);
        var wanted = path.Split('?')[0];
        Expect.True(status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    || !string.Equals(final.TrimEnd('/'), wanted.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                    || final.Contains("denied", StringComparison.OrdinalIgnoreCase),
            $"{who} reached {path} ({(int)status} at {final})");
    }

    /// <summary>For pages any member may open that hide their content by role: the text must not be shown to <paramref name="who"/>.</summary>
    private async Task ExpectRefusedPageTextAsync(string who, string path, string text)
    {
        var (status, html, _) = await _users[who].GetAsync(path);
        Expect.True(status != HttpStatusCode.OK || !Decoded(html).Contains(text, StringComparison.Ordinal), $"{who} was shown \"{text}\" on {path}");
    }

    private static void CheckEncoding(string where, string html) =>
        Expect.True(!html.Contains("Â·", StringComparison.Ordinal) && !html.Contains("â€", StringComparison.Ordinal) && !html.Contains('�'),
            $"{where} shows mis-encoded characters");

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static bool StartsWith(byte[] bytes, string ascii) =>
        bytes.Length >= ascii.Length && Encoding.ASCII.GetString(bytes, 0, ascii.Length) == ascii;

    private static async Task<string> PostVerifyFileAsync(BrowserUser user, byte[] json, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(json);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", name);
        var (status, body) = await user.PostFormAsync("/transcripts/verify", form);
        Expect.True(status == HttpStatusCode.OK, $"posting {name} to /transcripts/verify answered {(int)status}");
        return body;
    }

    /// <summary>The JSON download with one word of the transcript changed (the first long string gets " (edited)").</summary>
    private static byte[] Tamper(byte[] json)
    {
        var root = JsonNode.Parse(json) ?? throw new TourAssertException("the JSON transcript is empty");
        var transcript = root["transcript"] ?? root;
        if (!EditFirstLongString(transcript)) throw new TourAssertException("the JSON transcript has no text to edit");
        return Encoding.UTF8.GetBytes(root.ToJsonString());

        static bool EditFirstLongString(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj.ToList())
                    {
                        if (child is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 20) { obj[key] = s + " (edited)"; return true; }
                        if (child is not null && EditFirstLongString(child)) return true;
                    }
                    return false;
                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 20) { arr[i] = s + " (edited)"; return true; }
                        if (arr[i] is { } child && EditFirstLongString(child)) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }
    }

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<h1\b[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H1Tag();
}
