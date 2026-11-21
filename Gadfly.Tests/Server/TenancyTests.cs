using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Tenant isolation (GAD-T42/T73): the query filter, the write guard, append-only audit and immutable responses.</summary>
[TestFixture]
public class TenancyTests : ServerTest
{
    /// <summary>Gadfly tables a tenant may legitimately query across organizations (none hold another org's student data). Organizations and memberships live in the library's auth schema (GAD-T156).</summary>
    private static readonly HashSet<Type> UnfilteredByDesign =
    [
        typeof(OrganizationSettings), typeof(Job),
        // Not ITenantOwned (its organization is nullable) but it has its own organization filter, checked by
        // AuditEvents_OfOrgB_AreNotVisibleToOrgA (GAD-T109).
        typeof(AuditEvent),
    ];

    [Test]
    public void EveryTenantOwnedEntity_HasTheOrganizationFilter()
    {
        using var db = SystemDb();
        var owned = db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.That(owned, Is.Not.Empty);
        foreach (var t in owned)
            Assert.That(t.GetDeclaredQueryFilters(), Is.Not.Empty, t.ClrType.Name);
    }

    [Test]
    public void EveryGadflyTable_IsTenantOwned_OrDeliberatelyUnfiltered()
    {
        using var db = SystemDb();
        var unfiltered = db.Model.GetEntityTypes()
            .Where(t => t.GetSchema() == GadflyDbContext.Schema && !typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType).Except(UnfilteredByDesign).Select(t => t.Name).ToList();
        Assert.That(unfiltered, Is.Empty,
            "these tables carry organization data but no tenant filter: a request in one organization could read another's rows");
    }

    [Test]
    public async Task OrgA_CannotReadOrgB_ThroughTheExamChain()
    {
        var a = await Seed.EndedSessionAsync("answer from A");
        var b = await Seed.EndedSessionAsync("answer from B");
        await using var db = OrgDb(a.World.Org);
        {
            Assert.That(await db.SourceCollections.Select(x => x.OrganizationId).Distinct().ToListAsync(), Is.EqualTo(new[] { a.World.Org }));
            Assert.That(await db.SourceChunks.AnyAsync(x => x.OrganizationId == b.World.Org), Is.False);
            Assert.That(await db.SourceDocuments.AnyAsync(x => x.OrganizationId == b.World.Org), Is.False);
            Assert.That(await db.Concepts.AnyAsync(x => x.OrganizationId == b.World.Org), Is.False);
            Assert.That(await db.RubricPoints.AnyAsync(x => x.OrganizationId == b.World.Org), Is.False);
            Assert.That(await db.Exams.AnyAsync(x => x.Id == b.World.ExamId), Is.False);
            Assert.That(await db.ExamAssignments.AnyAsync(x => x.Id == b.World.AssignmentId), Is.False);
            Assert.That(await db.ExamSessions.AnyAsync(x => x.Id == b.SessionId), Is.False);
            Assert.That(await db.SessionTurns.AnyAsync(x => x.Id == b.TurnId), Is.False);
            Assert.That(await db.Judgments.AnyAsync(x => x.TurnId == b.TurnId), Is.False);
            Assert.That(await db.JudgeVotes.AnyAsync(x => x.OrganizationId == b.World.Org), Is.False);
            Assert.That(await db.SessionClaims.AnyAsync(x => x.SessionId == b.SessionId), Is.False);
            Assert.That(await db.ConceptMastery.AnyAsync(x => x.SessionId == b.SessionId), Is.False);
            Assert.That(await db.SessionReports.AnyAsync(x => x.SessionId == b.SessionId), Is.False);
            Assert.That(await db.GradeDecisions.AnyAsync(x => x.SessionId == b.SessionId), Is.False);
            Assert.That(await db.IntegrityEvents.AnyAsync(x => x.SessionId == b.SessionId), Is.False);
            // And A still sees its own.
            Assert.That(await db.ExamSessions.AnyAsync(x => x.Id == a.SessionId), Is.True);
            Assert.That(await db.SessionTurns.AnyAsync(x => x.Id == a.TurnId), Is.True);
        }
    }

    [Test]
    public async Task ExplicitlyAskingForOrgBsId_StillReturnsNothing()
    {
        var a = await Seed.EndedSessionAsync();
        var b = await Seed.EndedSessionAsync();
        await using var db = OrgDb(a.World.Org);
        Assert.That(await db.ExamSessions.FindAsync(b.SessionId), Is.Null);
        Assert.That(await db.ExamSessions.Where(s => s.OrganizationId == b.World.Org).CountAsync(), Is.Zero);
    }

    [Test]
    public async Task AssessmentTables_AreIsolated()
    {
        var a = await Seed.OrganizationAsync();
        var b = await Seed.OrganizationAsync();
        await using (var sys = SystemDb())
        {
            sys.Courses.Add(new Course { OrganizationId = b, Title = "B's course", CreatedAt = DateTimeOffset.UtcNow });
            sys.Courses.Add(new Course { OrganizationId = a, Title = "A's course", CreatedAt = DateTimeOffset.UtcNow });
            await sys.SaveChangesAsync();
        }
        await using var db = OrgDb(a);
        Assert.That(await db.Courses.Select(c => c.Title).ToListAsync(), Is.EqualTo(new[] { "A's course" }));
    }

    [Test]
    public async Task AuditEvents_OfOrgB_AreNotVisibleToOrgA()
    {
        var a = await Seed.OrganizationAsync();
        var b = await Seed.OrganizationAsync();
        await using (var sys = SystemDb())
        {
            sys.AuditEvents.Add(new AuditEvent { OrganizationId = b, Action = "grade.human.recorded", EntityType = "Test", At = DateTimeOffset.UtcNow, AfterJson = "{\"score\": 61}" });
            await sys.SaveChangesAsync();
        }
        await using var db = OrgDb(a);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.OrganizationId == b), Is.False,
            "org A's request context can read org B's audit trail (AuditEvent has no tenant filter)");
    }

    [Test]
    public async Task NobodySeesNoTenantRows()
    {
        var a = await Seed.EndedSessionAsync();
        await using var db = Db(TenantSnapshot.Nobody);
        Assert.That(await db.ExamSessions.AnyAsync(), Is.False);
        Assert.That(await db.Exams.AnyAsync(), Is.False);
        Assert.That(await db.SourceChunks.AnyAsync(), Is.False);
        await using var sys = SystemDb();
        Assert.That(await sys.ExamSessions.AnyAsync(s => s.Id == a.SessionId), Is.True, "system work sees everything");
    }

    [Test]
    public async Task IgnoreQueryFilters_IsTheOnlyWayAcross()
    {
        var a = await Seed.EndedSessionAsync();
        var b = await Seed.EndedSessionAsync();
        await using var db = OrgDb(a.World.Org);
        Assert.That(await db.ExamSessions.IgnoreQueryFilters().AnyAsync(s => s.Id == b.SessionId), Is.True);
    }

    [Test]
    public async Task WriteGuard_RefusesAddingIntoAnotherOrganization()
    {
        var a = await Seed.OrganizationAsync();
        var b = await Seed.OrganizationAsync();
        await using var db = OrgDb(a);
        db.Courses.Add(new Course { OrganizationId = b, Title = "smuggled", CreatedAt = DateTimeOffset.UtcNow });
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.That(ex!.Message, Does.Contain("outside the caller's organization"));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Test]
    public async Task WriteGuard_RefusesModifyingOrDeletingAnotherOrganizationsRow()
    {
        var a = await Seed.OrganizationAsync();
        var b = await Seed.OrganizationAsync();
        Guid courseId;
        await using (var sys = SystemDb())
        {
            var c = new Course { OrganizationId = b, Title = "B's", CreatedAt = DateTimeOffset.UtcNow };
            sys.Courses.Add(c);
            await sys.SaveChangesAsync();
            courseId = c.Id;
        }
        await using (var db = OrgDb(a))
        {
            var stolen = new Course { Id = courseId, OrganizationId = b, Title = "renamed" };
            db.Courses.Attach(stolen);
            db.Entry(stolen).Property(x => x.Title).IsModified = true;
            Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = OrgDb(a))
        {
            db.Courses.Remove(new Course { Id = courseId, OrganizationId = b });
            Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = OrgDb(a))
        {
            // Moving one of A's own rows into B is refused too.
            var mine = new Course { OrganizationId = a, Title = "A's", CreatedAt = DateTimeOffset.UtcNow };
            db.Courses.Add(mine);
            await db.SaveChangesAsync();
            mine.OrganizationId = b;
            Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using var check = SystemDb();
        Assert.That((await check.Courses.FindAsync(courseId))!.Title, Is.EqualTo("B's"));
    }

    [Test]
    public async Task WriteGuard_NobodyCanWriteNoTenantRow()
    {
        var a = await Seed.OrganizationAsync();
        await using var db = Db(TenantSnapshot.Nobody);
        db.Courses.Add(new Course { OrganizationId = a, Title = "x", CreatedAt = DateTimeOffset.UtcNow });
        Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task WriteGuard_AllowsTheOwnOrganization_AndSystemWork()
    {
        var a = await Seed.OrganizationAsync();
        await using (var db = OrgDb(a))
        {
            db.Courses.Add(new Course { OrganizationId = a, Title = "mine", CreatedAt = DateTimeOffset.UtcNow });
            Assert.DoesNotThrowAsync(() => db.SaveChangesAsync());
        }
        await using (var sys = SystemDb())
        {
            sys.Courses.Add(new Course { OrganizationId = a, Title = "system", CreatedAt = DateTimeOffset.UtcNow });
            Assert.DoesNotThrowAsync(() => sys.SaveChangesAsync());
        }
    }

    [Test]
    public async Task MemberTenant_IsScopedToTheActiveOrganization()
    {
        var a = await Seed.OrganizationAsync();
        var b = await Seed.OrganizationAsync();
        await using (var sys = SystemDb())
        {
            sys.Courses.Add(new Course { OrganizationId = b, Title = "B only", CreatedAt = DateTimeOffset.UtcNow });
            await sys.SaveChangesAsync();
        }
        // A person who belongs to both organizations, currently acting in A.
        var user = Guid.NewGuid();
        var tenant = new TenantSnapshot(false, user, a, OrgRoles.Instructor,
            [new Membership(a, OrgRoles.Instructor), new Membership(b, OrgRoles.Owner)], false);
        await using var db = Db(tenant);
        Assert.That(await db.Courses.AnyAsync(c => c.OrganizationId == b), Is.False);
    }

    [Test]
    public async Task AuditEvents_AreAppendOnly_EvenForSystemWork()
    {
        long id;
        await using (var sys = SystemDb())
        {
            var e = new AuditEvent { Action = "test.created", EntityType = "Test", At = DateTimeOffset.UtcNow };
            sys.AuditEvents.Add(e);
            await sys.SaveChangesAsync();
            id = e.Id;
            e.Reason = "tampered";
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => sys.SaveChangesAsync());
            Assert.That(ex!.Message, Does.Contain("append-only"));
        }
        await using (var sys = SystemDb())
        {
            sys.AuditEvents.Remove(await sys.AuditEvents.SingleAsync(x => x.Id == id));
            Assert.ThrowsAsync<InvalidOperationException>(() => sys.SaveChangesAsync());
            Assert.Throws<InvalidOperationException>(() => sys.SaveChanges());
        }
        await using var check = SystemDb();
        var stored = await check.AuditEvents.SingleAsync(x => x.Id == id);
        Assert.That(stored.Reason, Is.Null);
    }

    private static async Task<(Guid Org, Guid ResponseId)> ResponseAsync(string original)
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync();
        await using var db = OrgDb(w.Org);
        var r = new StudentResponse
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Sequence = 1, OriginalText = original, VerifiedText = original,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        db.StudentResponses.Add(r);
        await db.SaveChangesAsync();
        return (w.Org, r.Id);
    }

    [Test]
    public async Task StudentResponse_OriginalText_CannotBeRewritten()
    {
        var (org, id) = await ResponseAsync("The coeficient changes.");
        await using (var db = OrgDb(org))
        {
            var r = await db.StudentResponses.SingleAsync(x => x.Id == id);
            r.OriginalText = "The coefficient changes.";
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.That(ex!.Message, Does.Contain("original response cannot be rewritten"));
        }
        await using (var sys = SystemDb())
        {
            var r = await sys.StudentResponses.SingleAsync(x => x.Id == id);
            r.OriginalText = "system can't either";
            Assert.ThrowsAsync<InvalidOperationException>(() => sys.SaveChangesAsync());
        }
        await using var check = SystemDb();
        Assert.That((await check.StudentResponses.SingleAsync(x => x.Id == id)).OriginalText, Is.EqualTo("The coeficient changes."));
    }

    [Test]
    public async Task StudentResponse_VerifiedTextAndState_MayChange()
    {
        var (org, id) = await ResponseAsync("The coeficient changes.");
        await using (var db = OrgDb(org))
        {
            var r = await db.StudentResponses.SingleAsync(x => x.Id == id);
            r.VerifiedText = "The coefficient changes.";
            r.VerificationState = VerificationState.Corrected;
            Assert.DoesNotThrowAsync(() => db.SaveChangesAsync());
        }
        await using var check = SystemDb();
        var stored = await check.StudentResponses.SingleAsync(x => x.Id == id);
        Assert.That(stored.OriginalText, Is.EqualTo("The coeficient changes."));
        Assert.That(stored.VerifiedText, Is.EqualTo("The coefficient changes."));
        Assert.That(stored.VerificationState, Is.EqualTo(VerificationState.Corrected));
    }

    [Test]
    public void TenantSnapshot_FromPrincipal_PicksTheCookieOrgOnlyWhenAMember()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var user = Guid.NewGuid();
        System.Security.Claims.ClaimsPrincipal Principal(params (Guid Org, string Role)[] memberships) => new(new System.Security.Claims.ClaimsIdentity(
            memberships.Select(m => new System.Security.Claims.Claim(GadflyClaims.Membership, $"{m.Org:N}|{m.Role}"))
                .Append(new System.Security.Claims.Claim(MindAttic.Authentication.MaClaims.UserId, user.ToString())), "test"));

        var two = Principal((a, OrgRoles.Student), (b, OrgRoles.Instructor));
        Assert.That(TenantSnapshot.FromPrincipal(two, b.ToString("N")).OrganizationId, Is.EqualTo(b));
        Assert.That(TenantSnapshot.FromPrincipal(two, b.ToString("N")).Role, Is.EqualTo(OrgRoles.Instructor));
        Assert.That(TenantSnapshot.FromPrincipal(two, Guid.NewGuid().ToString("N")).OrganizationId, Is.Null, "a forged cookie with two memberships picks none");
        Assert.That(TenantSnapshot.FromPrincipal(two, null).OrganizationId, Is.Null);
        var one = Principal((a, OrgRoles.Student));
        Assert.That(TenantSnapshot.FromPrincipal(one, Guid.NewGuid().ToString("N")).OrganizationId, Is.EqualTo(a), "a single membership is used");
        Assert.That(TenantSnapshot.FromPrincipal(one, null).UserId, Is.EqualTo(user));
        Assert.That(TenantSnapshot.FromPrincipal(new System.Security.Claims.ClaimsPrincipal(), null), Is.EqualTo(TenantSnapshot.Nobody));
        Assert.That(TenantSnapshot.FromPrincipal(null, null).IsSystem, Is.False, "no caller is never system");
    }

    [Test]
    public void MembershipClaims_WithBadParts_AreIgnored()
    {
        var org = Guid.NewGuid();
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim(GadflyClaims.Membership, $"{org:N}|Student"),
            new System.Security.Claims.Claim(GadflyClaims.Membership, $"{org:N}|SuperUser"),
            new System.Security.Claims.Claim(GadflyClaims.Membership, "not-a-guid|Owner"),
            new System.Security.Claims.Claim(GadflyClaims.Membership, $"{org:N}|Owner|extra"),
        ], "test"));
        Assert.That(HttpTenantContext.Parse(principal), Is.EqualTo(new[] { new Membership(org, OrgRoles.Student) }));
    }

    [Test]
    public void ForOrganization_IsNeitherSystemNorAPerson()
    {
        var org = Guid.NewGuid();
        var t = TenantSnapshot.ForOrganization(org);
        Assert.That(t.IsSystem, Is.False);
        Assert.That(t.UserId, Is.Null);
        Assert.That(t.Role, Is.Null);
        Assert.That(t.OrganizationId, Is.EqualTo(org));
        Assert.That(TenantSnapshot.System.IsSystem, Is.True);
        Assert.That(TenantSnapshot.Nobody.IsSystem, Is.False);
        Assert.That(TenantSnapshot.Nobody.OrganizationId, Is.Null);
    }
}
