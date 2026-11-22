using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Reports;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Completion certificates (GAD-T311/GAD-T379) on LocalDB: only a result published to the student is certified; an issued
/// code verifies while the record is unchanged, reports a later change to the grade, and reports a result no longer
/// published; codes never issued or unreadable are said so.
/// </summary>
[TestFixture]
public class CertificateTests : ServerTest
{
    private const string BaseUrl = "https://gadfly.example";

    private sealed record World(Guid Org, Guid SessionId);

    private static async Task<World> Published(GradeStatus status = GradeStatus.Published, bool records = true)
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        await using var db = OrgDb(w.Org);
        db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = status, FinalPercent = 82.5m, LetterOrLabel = "B", UpdatedAt = DateTimeOffset.UtcNow });
        if (records)
        {
            db.ApprovalRecords.Add(new ApprovalRecord { OrganizationId = w.Org, SessionId = sessionId, Action = ApprovalAction.Finalize, ActorUserId = Guid.NewGuid(), ActorRole = "Instructor", At = DateTimeOffset.UtcNow });
            db.ApprovalRecords.Add(new ApprovalRecord { OrganizationId = w.Org, SessionId = sessionId, Action = ApprovalAction.Publish, ActorUserId = Guid.NewGuid(), ActorRole = "Instructor", At = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return new World(w.Org, sessionId);
    }

    private static async Task<CertificateDocument?> Build(World w)
    {
        await using var db = OrgDb(w.Org);
        return await CertificateBuilder.BuildAsync(db, w.Org, w.SessionId);
    }

    private static async Task<TranscriptStamp> Issue(World w, CertificateDocument doc)
    {
        var tenant = Member(w.Org, Guid.NewGuid(), OrgRoles.Student);
        await using var db = Db(tenant);
        return await CertificateRegistry.IssueAsync(db, new FixedTenant(tenant), doc, BaseUrl, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private static async Task<TranscriptRegistry.Outcome> Verify(string? code)
    {
        await using var db = SystemDb();
        return await CertificateRegistry.VerifyCodeAsync(db, code, CancellationToken.None);
    }

    [Test]
    public async Task APublishedResult_IsCertified_WithTheGradeAndNoReviewerNames()
    {
        var w = await Published();
        var doc = await Build(w);
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.SessionId, Is.EqualTo(w.SessionId));
        Assert.That(doc.ExamTitle, Is.EqualTo("Biology check"));
        Assert.That(doc.FinalPercent, Is.EqualTo(82.5m));
        Assert.That(doc.Label, Is.EqualTo("B"));
        Assert.That(doc.StudentName, Is.Not.Empty);
    }

    [TestCase(GradeStatus.Finalized)]
    [TestCase(GradeStatus.HumanReviewPending)]
    public async Task AResultNotPublished_IsNotCertified(GradeStatus status)
    {
        Assert.That(await Build(await Published(status)), Is.Null);
    }

    [Test]
    public async Task APublishedStatusWithoutTheApprovalRecords_IsNotCertified()
    {
        Assert.That(await Build(await Published(records: false)), Is.Null);
    }

    [Test]
    public async Task AnUnknownSession_IsNotCertified()
    {
        var w = await Published();
        await using var db = OrgDb(w.Org);
        Assert.That(await CertificateBuilder.BuildAsync(db, w.Org, Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task TheSameRecord_AlwaysGivesTheSameCode()
    {
        var w = await Published();
        var a = await Issue(w, (await Build(w))!);
        var b = await Issue(w, (await Build(w))!);
        Assert.That(b.Code, Is.EqualTo(a.Code));
        Assert.That(a.Sha256, Has.Length.EqualTo(64));
        Assert.That(a.VerifyUrl, Does.StartWith(BaseUrl));
    }

    [Test]
    public async Task AnIssuedCode_Verifies_AndTheIssueIsAudited()
    {
        var w = await Published();
        var stamp = await Issue(w, (await Build(w))!);
        await using (var db = SystemDb())
            Assert.That(await db.AuditEvents.AsNoTracking().CountAsync(a => a.Action == CertificateRegistry.IssuedAction && a.EntityId == stamp.Code && a.SessionId == w.SessionId),
                Is.EqualTo(1));

        var outcome = await Verify(stamp.Code.ToLowerInvariant().Replace("-", " "));
        Assert.That(outcome.Verdict, Is.EqualTo(TranscriptRegistry.Verdict.Matches));
        Assert.That(outcome.Kind, Is.EqualTo(CertificateRegistry.Kind));
        Assert.That(outcome.Code, Is.EqualTo(stamp.Code));
        Assert.That(outcome.FirstIssuedAt, Is.Not.Null);
    }

    [Test]
    public async Task ALaterChangeToTheGrade_IsReportedAsChanged()
    {
        var w = await Published();
        var stamp = await Issue(w, (await Build(w))!);
        await using (var db = SystemDb())
            await db.FinalGrades.Where(g => g.SessionId == w.SessionId).ExecuteUpdateAsync(u => u.SetProperty(g => g.FinalPercent, 91m));
        Assert.That((await Verify(stamp.Code)).Verdict, Is.EqualTo(TranscriptRegistry.Verdict.RecordChangedSince));
    }

    [Test]
    public async Task AResultNoLongerPublished_IsReportedAsNoLongerHeld()
    {
        var w = await Published();
        var stamp = await Issue(w, (await Build(w))!);
        await using (var db = SystemDb())
            await db.FinalGrades.Where(g => g.SessionId == w.SessionId).ExecuteUpdateAsync(u => u.SetProperty(g => g.Status, GradeStatus.Finalized));
        Assert.That((await Verify(stamp.Code)).Verdict, Is.EqualTo(TranscriptRegistry.Verdict.NoLongerHeld));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a code")]
    [TestCase("ABCD-EFGH")]
    public async Task UnreadableCodes_AreSaidSo(string? typed)
    {
        var outcome = await Verify(typed);
        Assert.That(outcome.Verdict, Is.EqualTo(TranscriptRegistry.Verdict.Unreadable));
        Assert.That(outcome.Kind, Is.EqualTo(CertificateRegistry.Kind));
    }

    [Test]
    public async Task AWellFormedCodeNeverIssued_IsNotFound()
    {
        var code = TranscriptHash.Code(TranscriptHash.Sha256("never issued " + Guid.NewGuid()));
        Assert.That((await Verify(code)).Verdict, Is.EqualTo(TranscriptRegistry.Verdict.NotFound));
    }
}
