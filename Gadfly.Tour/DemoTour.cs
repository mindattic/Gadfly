using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gadfly.Tour;

/// <summary>
/// The stages of the demo tour (docs/TOUR.md). Each stage drives the real server as a real user would (HTTP pages
/// and forms, the platform API, the SessionHub) and then checks the outcome against the API and the database.
/// A stage whose feature hasn't landed yet throws <see cref="NotBuiltException"/> and prints SKIP with its ticket,
/// detected by probing (a hub method, a route, a table with rows), so the tour grows as the product does.
/// </summary>
public sealed class DemoTour(TourOptions o, ServerProcess server)
{
    private const string Org = "Gadfly Demo Academy";
    private const string MathExam = "Demo: Understanding multiplication";
    private const string StatsExam = "Demo: Interaction effects in regression";
    private static readonly string[] People = ["demo.owner", "demo.teacher", "demo.examiner", "demo.reviewer", "demo.student"];

    private readonly TourDb _db = new(o.ConnectionString);
    private readonly Checklist _list = new();
    private readonly Dictionary<string, BrowserUser> _users = new(StringComparer.Ordinal);
    private HttpClient _api = null!;
    private bool Mock => !o.Live;

    private Guid _orgId, _mathExamId, _mathAssignmentId, _studentId, _sessionId;
    private HubConnection? _hub;
    private JsonElement _step;
    private bool _verificationBuilt;
    private Guid? _lastResponseId;

    public async Task<Checklist> RunAsync()
    {
        _api = new HttpClient(BrowserUser.CreateHandler(), disposeHandler: true) { BaseAddress = new Uri(o.BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(120) };
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        try
        {
            if (!await _list.RunAsync("Server is up (/healthz)", ServerUpAsync))
            {
                _list.Blocked("Every other stage", "the server is not answering");
                return _list;
            }
            var seeded = await _list.RunAsync("Demo institution and assessments seeded (GAD-T103)", SeededAsync);
            var signedIn = await _list.RunAsync("Sign-in with antiforgery, every role", SignInAsync);
            await _list.RunAsync("MOCK banner on every page (GAD-T100)", BannerAsync);
            await _list.RunAsync("Access control (anonymous, student, API key)", AccessControlAsync);
            if (seeded && signedIn) await _list.RunAsync("Teacher setup check (sources, exams, assessment setup)", TeacherSetupAsync);
            await _list.RunAsync("Source material processed through the API", ApiSourceProcessingAsync);

            if (!(seeded && signedIn && await _list.RunAsync("Student starts the exam over the SessionHub", StartExamAsync)))
            {
                _list.Blocked("Student exam and grading stages", "the exam could not start");
                return _list;
            }
            await _list.RunAsync("A good answer is judged", GoodAnswerAsync);
            await _list.RunAsync("Ambiguous answer gets a neutral clarification (GAD-T87)", ClarificationAsync);
            await _list.RunAsync("Typo correction keeps the original (GAD-T88)", TypoCorrectionAsync);
            await _list.RunAsync("Substantive revision is a new response event (GAD-T88)", RevisionAsync);
            await _list.RunAsync("\"I don't know\" is recorded honestly", DontKnowAsync);
            await _list.RunAsync("End of exam: session ended and report written", EndExamAsync);
            await _list.RunAsync(Mock ? "Mock results are labelled, student text is not" : "Live results come from real providers", LabelsAsync);
            await _list.RunAsync("AI preliminary grading per objective (GAD-T94)", AiGradingAsync);
            await _list.RunAsync("Blind human grading (GAD-T95)", () => GradingFeatureAsync("GAD-T95", "HumanObjectiveGrades", "a blind human grade"));
            await _list.RunAsync("Deliberate disagreement detected (GAD-T96)", () => GradingFeatureAsync("GAD-T96", "GradeDisagreements", "a disagreement"));
            await _list.RunAsync("Reconciliation (GAD-T97)", () => GradingFeatureAsync("GAD-T97", "GradeReconciliations", "a reconciliation", joinDisagreement: true));
            await _list.RunAsync("Approval and publication (GAD-T98)", () => GradingFeatureAsync("GAD-T98", "ApprovalRecords", "an approval record"));
            await _list.RunAsync("Student sees the published result (GAD-T98)", StudentResultAsync);
            await _list.RunAsync("Appeal and amendment (GAD-T98)", () => GradingFeatureAsync("GAD-T98", "GradeAppeals", "an appeal"));
            await _list.RunAsync("Report export (review export; assessment report GAD-T99)", ReportExportAsync);
            await _list.RunAsync("Review pages show the session", ReviewPagesAsync);
            await _list.RunAsync("Platform API reports the attempt and result", ApiResultAsync);
            await _list.RunAsync("Analytics endpoints (GAD-T102)", AnalyticsAsync);
            await _list.RunAsync("Audit trail", AuditAsync);
        }
        finally
        {
            if (_hub is not null) await _hub.DisposeAsync();
            foreach (var u in _users.Values) u.Dispose();
            _api.Dispose();
        }
        return _list;
    }

    // ------------------------------------------------------------------ setup stages

    private async Task<string> ServerUpAsync()
    {
        var ok = await TourDb.WaitAsync(async () =>
        {
            if (server.HasExited) throw new TourAssertException($"The server exited during startup; see {server.LogPath}.");
            try { return (await _api.GetAsync("healthz")).StatusCode == HttpStatusCode.OK; }
            catch (HttpRequestException) { return false; }
        }, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(2));
        Expect.True(ok, "GET /healthz never answered 200 within 3 minutes" + (o.Url is null ? $" (server log: {server.LogPath})" : ""));
        return $"{o.BaseUrl}/healthz answered 200";
    }

    private async Task<string> SeededAsync()
    {
        var timeout = o.Live ? TimeSpan.FromMinutes(20) : TimeSpan.FromMinutes(3);
        var ready = await TourDb.WaitAsync(async () =>
            await _db.CountAsync("SELECT COUNT(*) FROM gadfly.Exams e JOIN gadfly.Organizations o ON o.Id = e.OrganizationId WHERE o.Name = @o AND e.Title IN (@a, @b)",
                ("@o", Org), ("@a", MathExam), ("@b", StatsExam)) == 2, timeout, TimeSpan.FromSeconds(2));
        if (!ready && await _db.ScalarAsync<string>("SELECT TOP 1 Error FROM gadfly.Jobs WHERE State = 'Failed' ORDER BY CreatedAt DESC") is { } jobError)
            throw new TourAssertException($"The source job failed: {jobError}");
        Expect.True(ready, $"The two demo assessments did not appear within {timeout.TotalMinutes:0} minutes (is the server started with --seed-demo?).");

        _orgId = Expect.Value(await _db.ScalarAsync<Guid?>("SELECT Id FROM gadfly.Organizations WHERE Name = @o", ("@o", Org)), "no demo organization");
        var members = await _db.RowsAsync("""
            SELECT u.UserName, m.Role FROM gadfly.OrganizationMembers m JOIN auth.AuthUsers u ON u.Id = m.UserId
            WHERE m.OrganizationId = @org AND m.IsActive = 1
            """, ("@org", _orgId));
        var roles = members.ToDictionary(r => (string)r["UserName"]!, r => (string)r["Role"]!, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, role) in new[] { ("demo.owner", "Owner"), ("demo.teacher", "Instructor"), ("demo.examiner", "Examiner"), ("demo.reviewer", "SecondReviewer"), ("demo.student", "Student") })
            Expect.True(roles.TryGetValue(name, out var r) && r == role, $"{name} should be an active {role} (found {roles.GetValueOrDefault(name) ?? "nothing"}).");
        _studentId = Expect.Value(await _db.ScalarAsync<Guid?>("SELECT Id FROM auth.AuthUsers WHERE UserName = 'demo.student'"), "no demo.student account");

        var courses = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.Courses WHERE OrganizationId = @org", ("@org", _orgId));
        Expect.True(courses >= 2, $"expected 2 courses, found {courses}");
        var notReady = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.SourceCollections WHERE OrganizationId = @org AND Title LIKE 'Demo notes:%' AND Status <> 'Ready'", ("@org", _orgId));
        Expect.True(notReady == 0, $"{notReady} demo source collection(s) are not Ready");

        _mathExamId = Expect.Value(await _db.ScalarAsync<Guid?>("SELECT Id FROM gadfly.Exams WHERE OrganizationId = @org AND Title = @t", ("@org", _orgId), ("@t", MathExam)), "no multiplication exam");
        var exams = await _db.RowsAsync("""
            SELECT e.Title, e.Status, e.Subject, e.CourseId,
                   (SELECT COUNT(*) FROM gadfly.LearningObjectives l WHERE l.ExamId = e.Id) AS Objectives,
                   (SELECT COUNT(*) FROM gadfly.RubricCriteria c JOIN gadfly.LearningObjectives l ON l.Id = c.ObjectiveId WHERE l.ExamId = e.Id) AS Criteria,
                   (SELECT COUNT(*) FROM gadfly.RubricLevelDescriptors d JOIN gadfly.RubricCriteria c ON c.Id = d.CriterionId JOIN gadfly.LearningObjectives l ON l.Id = c.ObjectiveId WHERE l.ExamId = e.Id) AS Levels,
                   (SELECT COUNT(*) FROM gadfly.LearningObjectives l WHERE l.ExamId = e.Id AND NOT EXISTS
                        (SELECT 1 FROM gadfly.Concepts k WHERE k.CollectionId = e.CollectionId AND k.[Key] = l.ConceptKey)) AS Ungrounded,
                   p.AiWeight, p.HumanWeight, p.AdditionalReviewAt, p.MandatoryReconciliationAt, p.BlindHumanGrading, p.LockedAt,
                   (SELECT COUNT(*) FROM gadfly.ExamAssignments a WHERE a.ExamId = e.Id AND a.StudentUserId = @student) AS Assigned
            FROM gadfly.Exams e LEFT JOIN gadfly.GradingPolicies p ON p.ExamId = e.Id
            WHERE e.OrganizationId = @org AND e.Title IN (@a, @b)
            """, ("@org", _orgId), ("@student", _studentId), ("@a", MathExam), ("@b", StatsExam));
        foreach (var e in exams)
        {
            var title = (string)e["Title"]!;
            Expect.True((string)e["Status"]! == "Published" && e["CourseId"] is not null && ((string)e["Subject"]!).Length > 0, $"{title}: not published in a course with a subject");
            var objectives = (int)e["Objectives"]!;
            Expect.True(objectives == (title == MathExam ? 3 : 1), $"{title}: {objectives} objectives");
            Expect.True((int)e["Criteria"]! == objectives * 3 && (int)e["Levels"]! == objectives * 3 * 6, $"{title}: {e["Criteria"]} criteria, {e["Levels"]} level descriptors");
            Expect.True((int)e["Ungrounded"]! == 0, $"{title}: {e["Ungrounded"]} objective(s) not grounded in a concept of the notes");
            Expect.True(e["AiWeight"] is decimal ai && ai == 0.4m && (decimal)e["HumanWeight"]! == 0.6m
                && (decimal)e["AdditionalReviewAt"]! == 10m && (decimal)e["MandatoryReconciliationAt"]! == 20m
                && (bool)e["BlindHumanGrading"]! && e["LockedAt"] is not null, $"{title}: grading policy is not 40/60, 10/20, blind, locked");
            Expect.True((int)e["Assigned"]! == 1, $"{title}: not assigned to demo.student");
        }
        var statsMax = await _db.ScalarAsync<decimal>("SELECT l.MaxScore FROM gadfly.LearningObjectives l JOIN gadfly.Exams e ON e.Id = l.ExamId WHERE e.Title = @t AND e.OrganizationId = @org",
            ("@t", StatsExam), ("@org", _orgId));
        Expect.True(statsMax == 20m, $"the statistics objective's maximum score is {statsMax}, not 20 (Part 7.2)");
        _mathAssignmentId = Expect.Value(await _db.ScalarAsync<Guid?>("SELECT Id FROM gadfly.ExamAssignments WHERE ExamId = @e AND StudentUserId = @s",
            ("@e", _mathExamId), ("@s", _studentId)), "no assignment");
        var seededEvents = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.AuditEvents WHERE OrganizationId = @org AND Action = 'demo.seeded'", ("@org", _orgId));
        Expect.True(seededEvents >= 2, $"expected demo.seeded audit events, found {seededEvents}");
        return $"5 roles, {courses} courses, 2 assessments (3 + 1 objectives, 12 criteria, 72 level descriptors), policy 40/60 with thresholds 10/20, notes processed";
    }

    private async Task<string> SignInAsync()
    {
        foreach (var name in People)
        {
            var user = new BrowserUser(o.BaseUrl, name, o.Verbose);
            try { await user.SignInAsync(o.Password); }
            catch (TourAssertException ex) { throw new TourAssertException($"{name}: {ex.Message}"); }
            _users[name] = user;
        }
        return $"{string.Join(", ", People)} signed in through the form (antiforgery token posted)";
    }

    private async Task<string> BannerAsync()
    {
        var user = _users.GetValueOrDefault("demo.teacher") ?? throw new TourAssertException("demo.teacher is not signed in");
        var (status, html, _) = await user.GetAsync("/");
        Expect.True(status == HttpStatusCode.OK, $"GET / answered {(int)status}");
        var hasBanner = html.Contains("mock-banner", StringComparison.Ordinal);
        if (Mock)
        {
            if (!hasBanner) throw new NotBuiltException("GAD-T100", "MockBanner exists but MainLayout doesn't render it yet");
            Expect.True(html.Contains("MOCK MODE", StringComparison.Ordinal), "the banner doesn't say MOCK MODE");
            var (_, studentHtml, _) = await _users["demo.student"].GetAsync("/my-exams");
            Expect.True(studentHtml.Contains("mock-banner", StringComparison.Ordinal), "the student's page has no MOCK banner");
            return "teacher and student pages carry the MOCK MODE banner";
        }
        Expect.True(!hasBanner, "a MOCK banner is shown although the panel is live");
        return "no MOCK banner in live mode";
    }

    private async Task<string> AccessControlAsync()
    {
        using var anonymous = new BrowserUser(o.BaseUrl, "anonymous", o.Verbose);
        var (_, _, path) = await anonymous.GetAsync("/review");
        Expect.True(path.StartsWith("/login", StringComparison.OrdinalIgnoreCase), $"anonymous /review ended at {path}, not the sign-in page");

        if (_users.TryGetValue("demo.student", out var student))
        {
            var (status, _, studentPath) = await student.GetAsync("/review");
            Expect.True(status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound || !studentPath.StartsWith("/review", StringComparison.OrdinalIgnoreCase)
                || studentPath.Contains("denied", StringComparison.OrdinalIgnoreCase),
                $"a student reached the review page ({(int)status} at {studentPath})");
        }

        using var noKey = new HttpClient(BrowserUser.CreateHandler()) { BaseAddress = new Uri(o.BaseUrl + "/") };
        var unauth = await noKey.GetAsync("api/v1/collections");
        Expect.True(unauth.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Redirect, $"the API without a key answered {(int)unauth.StatusCode}");
        var withKey = await _api.GetAsync("api/v1/collections");
        Expect.True(withKey.StatusCode == HttpStatusCode.OK, $"the API with the demo key answered {(int)withKey.StatusCode}");
        return "anonymous sent to sign-in, student kept out of review, API refuses no key and accepts the demo key";
    }

    private async Task<string> TeacherSetupAsync()
    {
        var teacher = _users["demo.teacher"];
        var (s1, sources, _) = await teacher.GetAsync("/sources");
        Expect.True(s1 == HttpStatusCode.OK && sources.Contains("Demo notes: Multiplication", StringComparison.Ordinal), "the source library doesn't list the demo notes");
        var (s2, exams, _) = await teacher.GetAsync("/exams");
        Expect.True(s2 == HttpStatusCode.OK && exams.Contains(MathExam, StringComparison.Ordinal), "the exam list doesn't show the multiplication assessment");

        var collections = await _api.GetFromJsonAsync<JsonElement>("api/v1/collections");
        var demo = collections.EnumerateArray().Where(c => c.GetProperty("title").GetString()!.StartsWith("Demo notes:", StringComparison.Ordinal)).ToList();
        Expect.True(demo.Count == 2 && demo.All(c => c.GetProperty("status").GetString() == "Ready" && c.GetProperty("conceptCount").GetInt32() > 0),
            "the API doesn't report both demo collections Ready with concepts");

        var (s3, setup, _) = await teacher.GetAsync("/assessments");
        var setupNote = s3 == HttpStatusCode.NotFound
            ? "assessment setup UI not built yet (GAD-T85)"
            : s3 == HttpStatusCode.OK
                ? setup.Contains(MathExam, StringComparison.Ordinal) ? "assessment setup lists the demo assessments" : "assessment setup page answers 200 (demo titles not listed on it)"
                : throw new TourAssertException($"/assessments answered {(int)s3}");
        return $"sources and exams pages list the demo; both collections Ready via the API; {setupNote}";
    }

    private async Task<string> ApiSourceProcessingAsync()
    {
        var created = await _api.PostAsJsonAsync("api/v1/collections", new { title = $"Tour upload {DateTime.UtcNow:yyyyMMdd-HHmmss}" });
        Expect.True(created.StatusCode == HttpStatusCode.Created, $"create collection answered {(int)created.StatusCode}");
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var material = Path.Combine(ServerProcess.RepoRoot(), "Gadfly.Server", "Demo", "Material", "multiplication.md");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(material));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "files", "multiplication.md");
        var upload = await _api.PostAsync($"api/v1/collections/{id}/documents", form);
        Expect.True(upload.StatusCode == HttpStatusCode.OK, $"upload answered {(int)upload.StatusCode}: {await upload.Content.ReadAsStringAsync()}");

        var process = await _api.PostAsJsonAsync($"api/v1/collections/{id}/process", new { rebuildConceptMap = false });
        Expect.True(process.StatusCode == HttpStatusCode.Accepted, $"process answered {(int)process.StatusCode}");
        var jobId = (await process.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        string state = "";
        var done = await TourDb.WaitAsync(async () =>
        {
            var job = await _api.GetFromJsonAsync<JsonElement>($"api/v1/jobs/{jobId}");
            state = job.GetProperty("state").GetString() ?? "";
            if (state is "Failed" or "Cancelled") throw new TourAssertException($"the job ended {state}: {job.GetProperty("error")}");
            return state == "Succeeded";
        }, o.Live ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
        Expect.True(done, $"the processing job is still {state}");

        var map = await _api.GetFromJsonAsync<JsonElement>($"api/v1/collections/{id}/concepts");
        var titles = map.GetProperty("concepts").EnumerateArray().Select(c => c.GetProperty("title").GetString()!).ToList();
        Expect.True(titles.Count > 0, "no concepts were found");
        if (Mock)
            Expect.True(titles.Contains("Multiplication as equal groups") && titles.Contains("What each factor represents"),
                $"the mock concept map should follow the notes' headings, got: {string.Join(", ", titles)}");
        return $"uploaded, processed by job {jobId:N}, {titles.Count} concepts: {string.Join("; ", titles)}";
    }

    // ------------------------------------------------------------------ the student's exam

    private async Task<string> StartExamAsync()
    {
        _hub = _users["demo.student"].Hub();
        await _hub.StartAsync();
        var consent = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("Gadfly demo tour: the student consents to the recorded oral examination.")));
        _sessionId = await _hub.InvokeAsync<Guid>("Start", _mathAssignmentId, consent);
        _step = await InvokeAsync("Current", _sessionId);
        _verificationBuilt = _step.TryGetProperty("verification", out _);
        var question = Str(_step, "question");
        Expect.True(question.Length > 0, "the first step has no question");
        Expect.True(!question.Contains("[MOCK]", StringComparison.Ordinal), "a question shown to the student carries the [MOCK] diagnostics marker");
        var row = (await _db.RowsAsync("SELECT State, ConsentRecordedAt FROM gadfly.ExamSessions WHERE Id = @s", ("@s", _sessionId))).Single();
        Expect.True((string)row["State"]! == "Running" && row["ConsentRecordedAt"] is not null, $"the session is {row["State"]} (consent {(row["ConsentRecordedAt"] is null ? "missing" : "recorded")})");
        return $"session {_sessionId:N} running; first question: \"{question}\"";
    }

    private async Task<string> GoodAnswerAsync()
    {
        var (turnId, judged) = await AnswerAndSettleAsync(
            "Multiplication means adding equal groups. Three bags with four apples in each bag is four plus four plus four, which is twelve, " +
            "because every group has the same size, so repeated addition gives the total.");
        var j = Expect.NotNull(judged, "the answer has no judgment");
        var score = Convert.ToDouble(j["OverallScore"]);
        var votes = await _db.RowsAsync("SELECT ProviderId, Verdict FROM gadfly.JudgeVotes WHERE JudgmentId = @j", ("@j", j["Id"]));
        Expect.True(votes.Count > 0, "the judgment has no judge votes");
        if (Mock)
        {
            Expect.True(votes.All(v => (string)v["ProviderId"]! == "mock"), "a judge vote is not from the mock provider");
            Expect.True(((string)j["Summary"]!).StartsWith("[MOCK]", StringComparison.Ordinal), $"the judgment summary is not labelled: {j["Summary"]}");
            Expect.True(score >= 0.5, $"a strong answer scored only {score:0.00} (the mock should reward rubric overlap)");
        }
        return $"turn {turnId:N} judged {score:0.00} by {string.Join(", ", votes.Select(v => v["ProviderId"]).Distinct())}";
    }

    private async Task<string> ClarificationAsync()
    {
        EnsureRunning();
        var ambiguous = "It doubles because of the thing with the bags, kind of.";
        _step = await InvokeAsync("Answer", _sessionId, new { text = ambiguous, mode = "text" });
        if (!_step.TryGetProperty("verification", out var v))
        {
            if (await _db.CountAsync("SELECT COUNT(*) FROM gadfly.StudentResponses WHERE SessionId = @s", ("@s", _sessionId)) == 0)
                throw new NotBuiltException("GAD-T87", "answers are not captured as responses and no clarification is offered");
            throw new TourAssertException("responses are captured but the step carries no verification field");
        }
        _verificationBuilt = true;
        Expect.True(v.ValueKind == JsonValueKind.Object, $"no clarification was asked for \"{ambiguous}\"");
        var kind = Str(v, "kind");
        var prompt = Str(v, "prompt");
        Expect.True(kind == "meaning", $"expected a meaning clarification, got \"{kind}\"");
        Expect.True(!prompt.Contains("Do you mean", StringComparison.OrdinalIgnoreCase) && !prompt.Contains(" or ", StringComparison.OrdinalIgnoreCase),
            $"the clarification question leads or offers options: \"{prompt}\"");
        var responseId = v.GetProperty("responseId").GetGuid();
        var events = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.ClarificationEvents WHERE SessionId = @s AND ResponseId = @r", ("@s", _sessionId), ("@r", responseId));
        Expect.True(events == 1, $"{events} clarification events recorded for the response");

        _step = await InvokeAsync("Clarify", _sessionId, responseId,
            "I mean that if the number of bags doubles, the total number of apples doubles too, because each bag still has four apples.");
        await SettleVerificationAsync();
        var row = (await _db.RowsAsync("""
            SELECT r.VerificationState, r.OriginalText, c.StudentReply FROM gadfly.StudentResponses r
            JOIN gadfly.ClarificationEvents c ON c.ResponseId = r.Id WHERE r.Id = @r
            """, ("@r", responseId))).Single();
        Expect.True((string)row["OriginalText"]! == ambiguous, "the original answer text changed");
        Expect.True(row["StudentReply"] is string { Length: > 0 }, "the clarification reply was not stored");
        Expect.True((string)row["VerificationState"]! == "Clarified", $"the response is {row["VerificationState"]}, not Clarified");
        return $"asked \"{prompt}\"; reply stored beside the unchanged original; response Clarified";
    }

    private async Task<string> TypoCorrectionAsync()
    {
        EnsureRunning();
        var withTypo = "The three is the number of bags and the four is the number of aples in each bag.";
        var (turnId, _) = await AnswerAndSettleAsync(withTypo);
        var corrected = "The three is the number of bags and the four is the number of apples in each bag.";
        try
        {
            _step = await InvokeAsync("CorrectAnswer", _sessionId, corrected, false);
        }
        catch (MissingHubMethodException)
        {
            throw new NotBuiltException("GAD-T88", "the hub has no CorrectAnswer method");
        }
        await SettleVerificationAsync();
        var response = (await _db.RowsAsync("""
            SELECT TOP 1 r.Id, r.OriginalText, r.VerifiedText, r.VerificationState FROM gadfly.StudentResponses r
            WHERE r.SessionId = @s AND r.TurnId = @t ORDER BY r.Sequence
            """, ("@s", _sessionId), ("@t", turnId))).SingleOrDefault() ?? throw new TourAssertException("no response row for the corrected turn");
        Expect.True((string)response["OriginalText"]! == withTypo, "the original (with the typo) was overwritten");
        var versions = await _db.RowsAsync("SELECT Version, Text, CorrectionKind FROM gadfly.TranscriptVersions WHERE ResponseId = @r ORDER BY Version", ("@r", response["Id"]));
        Expect.True(versions.Any(x => x["CorrectionKind"] as string == "Transcription" && (string)x["Text"]! == corrected),
            $"no Transcription version with the corrected text ({versions.Count} versions)");
        Expect.True((string)response["VerifiedText"]! == corrected, "grading doesn't use the corrected text");
        return $"original kept, {versions.Count} versions, response {response["VerificationState"]}";
    }

    private async Task<string> RevisionAsync()
    {
        EnsureRunning();
        var revised = "I want to change my answer: the four is how many apples are in each bag and the three is how many bags there are, " +
                      "so four bags of three apples would be a different situation even though the total is the same.";
        try
        {
            _step = await InvokeAsync("ReviseAnswer", _sessionId, revised);
        }
        catch (MissingHubMethodException)
        {
            throw new NotBuiltException("GAD-T88", "the hub has no ReviseAnswer method");
        }
        await SettleVerificationAsync();
        var row = (await _db.RowsAsync("""
            SELECT TOP 1 Id, RevisesResponseId, OriginalText FROM gadfly.StudentResponses
            WHERE SessionId = @s AND Kind = 'SubstantiveRevision' ORDER BY SubmittedAt DESC
            """, ("@s", _sessionId))).SingleOrDefault() ?? throw new TourAssertException("no SubstantiveRevision response was recorded");
        Expect.True(row["RevisesResponseId"] is Guid, "the revision doesn't point at the response it revises");
        Expect.True((string)row["OriginalText"]! == revised, "the revision text differs from what the student sent");
        var original = await _db.ScalarAsync<string>("SELECT OriginalText FROM gadfly.StudentResponses WHERE Id = @r", ("@r", row["RevisesResponseId"]));
        Expect.True(original is { Length: > 0 } && original != revised, "the revised response lost its original text");
        return "recorded as a new response event pointing at the original, which is unchanged";
    }

    private async Task<string> DontKnowAsync()
    {
        EnsureRunning();
        var (turnId, judged) = await AnswerAndSettleAsync("I don't know.");
        var text = await _db.ScalarAsync<string>("SELECT AnswerText FROM gadfly.SessionTurns WHERE Id = @t", ("@t", turnId));
        Expect.True(text == "I don't know.", $"the turn recorded \"{text}\"");
        if (judged is not null)
            Expect.True(Convert.ToDouble(judged["OverallScore"]) <= 0.1, $"\"I don't know\" scored {judged["OverallScore"]}");
        return judged is null ? "recorded; not scored" : $"recorded; judged {Convert.ToDouble(judged["OverallScore"]):0.00} ({judged["Summary"]})";
    }

    private async Task<string> EndExamAsync()
    {
        if (StepState(_step) != "Ended")
            _step = await InvokeAsync("Finish", _sessionId);
        var ended = await TourDb.WaitAsync(async () =>
            await _db.CountAsync("SELECT COUNT(*) FROM gadfly.ExamSessions WHERE Id = @s AND State = 'Ended'", ("@s", _sessionId)) == 1
            && await _db.CountAsync("SELECT COUNT(*) FROM gadfly.SessionReports WHERE SessionId = @s", ("@s", _sessionId)) == 1,
            TimeSpan.FromSeconds(60));
        Expect.True(ended, "the session did not end with a report");
        var turns = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.SessionTurns WHERE SessionId = @s AND AnswerText IS NOT NULL", ("@s", _sessionId));
        var recommendation = await _db.ScalarAsync<string>("SELECT Recommendation FROM gadfly.SessionReports WHERE SessionId = @s", ("@s", _sessionId));
        return $"{turns} answered turns; report written (recommendation: {recommendation})";
    }

    private async Task<string> LabelsAsync()
    {
        var questions = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.SessionTurns WHERE SessionId = @s AND Question LIKE '%[[]MOCK]%'", ("@s", _sessionId));
        Expect.True(questions == 0, $"{questions} student-facing question(s) carry the [MOCK] marker");
        var providers = (await _db.RowsAsync("""
            SELECT DISTINCT v.ProviderId FROM gadfly.JudgeVotes v JOIN gadfly.Judgments j ON j.Id = v.JudgmentId
            JOIN gadfly.SessionTurns t ON t.Id = j.TurnId WHERE t.SessionId = @s
            """, ("@s", _sessionId))).Select(r => (string)r["ProviderId"]!).ToList();
        var report = await _db.ScalarAsync<string>("SELECT Markdown FROM gadfly.SessionReports WHERE SessionId = @s", ("@s", _sessionId)) ?? "";
        var aiGrades = await _db.RowsAsync("SELECT IsMock, Explanation FROM gadfly.AiObjectiveGrades WHERE SessionId = @s", ("@s", _sessionId));
        if (Mock)
        {
            Expect.True(providers.Count > 0 && providers.All(p => p == "mock"), $"judge providers: {string.Join(", ", providers)}");
            Expect.True(report.Contains("[MOCK]", StringComparison.Ordinal), "the session report doesn't carry the [MOCK] label");
            Expect.True(aiGrades.All(g => (bool)g["IsMock"]!), "an AI objective grade made in mock mode is not flagged IsMock");
            return $"judge votes all from \"mock\", report labelled, {aiGrades.Count} AI objective grade(s) flagged IsMock, questions unmarked";
        }
        Expect.True(providers.Count > 0 && providers.All(p => p != "mock"), "a live session has mock judge votes");
        Expect.True(aiGrades.All(g => !(bool)g["IsMock"]!), "a live session has mock AI grades");
        return $"judged by {string.Join(", ", providers)}";
    }

    // ------------------------------------------------------------------ grading (grows as GAD-T94..T99 land)

    private async Task<string> AiGradingAsync()
    {
        var rows = new List<Dictionary<string, object?>>();
        await TourDb.WaitAsync(async () =>
        {
            rows = await _db.RowsAsync("SELECT ObjectiveId, Level, Score, MaxScore, IsMock, Explanation, Confidence FROM gadfly.AiObjectiveGrades WHERE SessionId = @s", ("@s", _sessionId));
            return rows.Count > 0;
        }, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2));
        if (rows.Count == 0)
            throw new NotBuiltException("GAD-T94", "no AI objective grade was recorded within 60 s of the exam ending");
        var objectives = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.LearningObjectives WHERE ExamId = @e", ("@e", _mathExamId));
        Expect.True(rows.Count >= objectives, $"{rows.Count} AI grades for {objectives} objectives");
        foreach (var r in rows)
        {
            Expect.True((decimal)r["Score"]! >= 0 && (decimal)r["Score"]! <= (decimal)r["MaxScore"]!, $"an AI score {r["Score"]} is outside 0..{r["MaxScore"]}");
            if (Mock) Expect.True(((string)r["Explanation"]!).Contains("[MOCK]", StringComparison.Ordinal) || (bool)r["IsMock"]!, "a mock AI grade is unlabelled");
        }
        var status = await _db.ScalarAsync<string>("SELECT Status FROM gadfly.FinalGrades WHERE SessionId = @s", ("@s", _sessionId));
        return $"{rows.Count} objective grade(s): {string.Join("; ", rows.Select(r => $"level {r["Level"]}, {r["Score"]}/{r["MaxScore"]}"))}; grade status {status ?? "(none)"}";
    }

    /// <summary>
    /// The human-grading stages need an HTTP action the tour can drive as the examiner (blind grade, reconcile,
    /// approve, publish, appeal). Until one exists they SKIP with the ticket; once their rows appear for the tour's
    /// session (or the actions exist), the stage asserts them.
    /// </summary>
    private async Task<string> GradingFeatureAsync(string ticket, string table, string what, bool joinDisagreement = false)
    {
        if (!await _db.TableExistsAsync("gadfly", table)) throw new NotBuiltException(ticket, $"no {table} table");
        var count = joinDisagreement
            ? await _db.CountAsync($"SELECT COUNT(*) FROM gadfly.{table} x JOIN gadfly.GradeDisagreements d ON d.Id = x.DisagreementId WHERE d.SessionId = @s", ("@s", _sessionId))
            : await _db.CountAsync($"SELECT COUNT(*) FROM gadfly.{table} WHERE SessionId = @s", ("@s", _sessionId));
        if (count > 0) return $"{count} {table} row(s) for the session";
        var examiner = _users.GetValueOrDefault("demo.examiner");
        var page = examiner is null ? HttpStatusCode.NotFound : (await examiner.GetAsync("/grading")).Status;
        throw new NotBuiltException(ticket, page == HttpStatusCode.OK
            ? $"/grading exists, but there is no HTTP action the tour can drive to record {what} yet"
            : $"nothing records {what} yet");
    }

    private async Task<string> StudentResultAsync()
    {
        var published = await _db.CountAsync("SELECT COUNT(*) FROM gadfly.FinalGrades WHERE SessionId = @s AND Status IN ('Published', 'UnderAppeal', 'Amended')", ("@s", _sessionId));
        if (published == 0) throw new NotBuiltException("GAD-T98", "no grade is published for the session (approval and publication are not drivable yet)");
        var (status, html, _) = await _users["demo.student"].GetAsync("/my-exams");
        Expect.True(status == HttpStatusCode.OK && html.Contains(MathExam, StringComparison.Ordinal), "the student's exams page doesn't show the published result");
        return "the student's page shows the published result";
    }

    // ------------------------------------------------------------------ reports, review, API, analytics, audit

    private async Task<string> ReportExportAsync()
    {
        var teacher = _users["demo.teacher"];
        var (s1, md, _) = await teacher.GetAsync($"/review/{_sessionId:D}/report.md");
        Expect.True(s1 == HttpStatusCode.OK && md.Contains(MathExam, StringComparison.Ordinal), $"report.md answered {(int)s1}");
        if (Mock) Expect.True(md.Contains("[MOCK]", StringComparison.Ordinal), "the exported report is not labelled [MOCK]");
        var (s2, csv, _) = await teacher.GetAsync($"/review/{_sessionId:D}/turns.csv");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Expect.True(s2 == HttpStatusCode.OK && lines >= 2, $"turns.csv answered {(int)s2} with {lines} lines");
        var (s3, _, _) = await _users["demo.student"].GetAsync($"/review/{_sessionId:D}/report.md", follow: false);
        Expect.True(s3 != HttpStatusCode.OK, "a student could download the reviewer's report");
        return $"report.md ({md.Length} chars{(Mock ? ", labelled [MOCK]" : "")}) and turns.csv ({lines - 1} rows) exported; student refused; " +
               "the per-objective assessment report (GAD-T99) is not checked yet";
    }

    private async Task<string> ReviewPagesAsync()
    {
        var teacher = _users["demo.teacher"];
        var (s1, list, _) = await teacher.GetAsync("/review");
        Expect.True(s1 == HttpStatusCode.OK && list.Contains("demo.student", StringComparison.Ordinal), "the review list doesn't show demo.student's session");
        var (s2, page, _) = await teacher.GetAsync($"/review/{_sessionId:D}");
        Expect.True(s2 == HttpStatusCode.OK && page.Contains(MathExam, StringComparison.Ordinal), $"the session review page answered {(int)s2}");
        return "review list and session page render for the teacher";
    }

    private async Task<string> ApiResultAsync()
    {
        var attempts = await _api.GetFromJsonAsync<JsonElement>($"api/v1/assignments/{_mathAssignmentId}/sessions");
        Expect.True(attempts.EnumerateArray().Any(a => a.GetProperty("id").GetGuid() == _sessionId), "the attempt list doesn't include the session");
        var result = await _api.GetFromJsonAsync<JsonElement>($"api/v1/sessions/{_sessionId}/result");
        Expect.True(Str(result, "state") == "Ended", $"the API reports the session {Str(result, "state")}");
        Expect.True(result.TryGetProperty("report", out var report) && report.ValueKind == JsonValueKind.Object, "the API result has no report");
        return $"attempts and result available (recommendation {Str(result, "recommendation")})";
    }

    private async Task<string> AnalyticsAsync()
    {
        var teacher = _users["demo.teacher"];
        var (status, csv, _) = await teacher.GetAsync($"/analytics/export.csv?exam={_mathExamId}");
        if (status == HttpStatusCode.NotFound) throw new NotBuiltException("GAD-T102", "/analytics/export.csv is not mapped");
        Expect.True(status == HttpStatusCode.OK, $"/analytics/export.csv answered {(int)status}");
        var (pageStatus, _, _) = await teacher.GetAsync("/analytics");
        Expect.True(pageStatus == HttpStatusCode.OK, $"/analytics answered {(int)pageStatus}");
        var (studentStatus, _, studentPath) = await _users["demo.student"].GetAsync("/analytics/export.csv", follow: false);
        Expect.True(studentStatus != HttpStatusCode.OK, $"a student could download analytics ({studentPath})");
        return $"export.csv ({csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} lines) and /analytics answer the teacher; student refused";
    }

    private async Task<string> AuditAsync()
    {
        var rows = await _db.RowsAsync("""
            SELECT Action, COUNT(*) AS N FROM gadfly.AuditEvents
            WHERE OrganizationId = @org GROUP BY Action ORDER BY Action
            """, ("@org", _orgId));
        Expect.True(rows.Any(r => (string)r["Action"]! == "demo.seeded"), "no demo.seeded audit event");
        if (_verificationBuilt)
            Expect.True(rows.Any(r => ((string)r["Action"]!).StartsWith("response.", StringComparison.Ordinal)), "verification ran but wrote no response.* audit event");
        return string.Join(", ", rows.Select(r => $"{r["Action"]} x{r["N"]}"));
    }

    // ------------------------------------------------------------------ hub helpers

    private sealed class MissingHubMethodException(string method) : Exception($"The hub has no method {method}.");

    private async Task<JsonElement> InvokeAsync(string method, params object?[] args)
    {
        if (_hub is null) throw new TourAssertException("the student is not connected to the hub");
        try
        {
            var step = await _hub.InvokeCoreAsync<JsonElement>(method, args);
            if (o.Verbose) Console.WriteLine($"      hub {method} -> {Str(step, "question")}");
            return step;
        }
        catch (HubException ex) when (ex.Message.Contains("Unknown hub method", StringComparison.OrdinalIgnoreCase)
                                      || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new MissingHubMethodException(method);
        }
        catch (HubException ex)
        {
            throw new TourAssertException($"hub {method} refused: {ex.Message}");
        }
    }

    /// <summary>Answers the open question, settles any verification the server asks for, and returns the judged turn.</summary>
    private async Task<(Guid TurnId, Dictionary<string, object?>? Judgment)> AnswerAndSettleAsync(string text)
    {
        EnsureRunning();
        var index = _step.TryGetProperty("turnIndex", out var ti) && ti.ValueKind == JsonValueKind.Number ? ti.GetInt32() : -1;
        _step = await InvokeAsync("Answer", _sessionId, new { text, mode = "text" });
        await SettleVerificationAsync();
        var turn = (await _db.RowsAsync(index >= 0
                ? "SELECT Id, AnswerText FROM gadfly.SessionTurns WHERE SessionId = @s AND [Index] = @i"
                : "SELECT TOP 1 Id, AnswerText FROM gadfly.SessionTurns WHERE SessionId = @s AND AnswerText IS NOT NULL ORDER BY [Index] DESC",
            ("@s", _sessionId), ("@i", index))).SingleOrDefault() ?? throw new TourAssertException("the answered turn is not in the database");
        Expect.True(turn["AnswerText"] is string { Length: > 0 }, "the answer was not stored on the turn");
        var turnId = (Guid)turn["Id"]!;
        var judgment = (await _db.RowsAsync("SELECT TOP 1 Id, OverallScore, Summary FROM gadfly.Judgments WHERE TurnId = @t ORDER BY CreatedAt DESC", ("@t", turnId))).SingleOrDefault();
        return (turnId, judgment);
    }

    /// <summary>A verification request the stage didn't set out to test: confirm the transcript or restate plainly.</summary>
    private async Task SettleVerificationAsync()
    {
        for (var i = 0; i < 3 && _step.TryGetProperty("verification", out var v) && v.ValueKind == JsonValueKind.Object; i++)
        {
            var id = v.GetProperty("responseId").GetGuid();
            _lastResponseId = id;
            _step = Str(v, "kind") == "transcript"
                ? await InvokeAsync("ConfirmTranscript", _sessionId, id)
                : await InvokeAsync("Clarify", _sessionId, id, "I mean the number of groups times the number in each group gives the total, because the groups are equal.");
        }
    }

    private void EnsureRunning()
    {
        if (StepState(_step) == "Ended")
            throw new TourAssertException("the exam ended before this stage (the planner closed every concept); raise MaxTurns in the demo policy");
    }

    private static readonly string[] SessionStates = ["Created", "ConsentGiven", "Running", "Paused", "Ended", "Failed"];

    private static string StepState(JsonElement step) =>
        !step.TryGetProperty("state", out var s) ? ""
        : s.ValueKind == JsonValueKind.Number ? SessionStates.ElementAtOrDefault(s.GetInt32()) ?? s.ToString()
        : s.GetString() ?? "";

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
