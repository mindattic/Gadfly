using System.Linq.Expressions;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MindAttic.Authentication.Data;
using MindAttic.Authentication.Entities;

namespace Gadfly.Server.Data;

/// <summary>
/// The platform's database: every Gadfly table lives in the <c>gadfly</c> schema (the
/// MindAttic.Authentication tables join in their own <c>auth</c> schema from GAD-T41).
/// Enums are stored as their names; foreign keys never cascade (see Entities.cs).
/// </summary>
public sealed class GadflyDbContext : DbContext, IAuthDataContext
{
    public const string Schema = "gadfly";

    private readonly ITenantContext? _tenant;

    public GadflyDbContext(DbContextOptions<GadflyDbContext> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    // Tenant isolation (GAD-T42). Read by the global query filters below on every query: system work
    // (no request) sees every organization; a request sees only its active organization, and a request
    // with no organization (signed out, or no membership) sees no tenant-owned rows at all.
    internal bool TenantBypass => _tenant is null || _tenant.IsSystem;
    internal Guid? TenantOrganizationId => _tenant?.OrganizationId;

    // MindAttic.Authentication's tables, in its own "auth" schema in the same database (GAD-T41).
    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();
    public DbSet<AuthUserMfa> AuthUserMfa => Set<AuthUserMfa>();
    public DbSet<AuthRecoveryCode> AuthRecoveryCodes => Set<AuthRecoveryCode>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthLoginThrottle> AuthLoginThrottles => Set<AuthLoginThrottle>();
    public DbSet<AuthAuditLog> AuthAuditLog => Set<AuthAuditLog>();
    public DbSet<AuthPasswordHistory> AuthPasswordHistory => Set<AuthPasswordHistory>();
    public DbSet<AuthPasswordResetToken> AuthPasswordResetTokens => Set<AuthPasswordResetToken>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
    public DbSet<SourceCollection> SourceCollections => Set<SourceCollection>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<SourceChunk> SourceChunks => Set<SourceChunk>();
    public DbSet<Concept> Concepts => Set<Concept>();
    public DbSet<ConceptRelation> ConceptRelations => Set<ConceptRelation>();
    public DbSet<ConceptCitation> ConceptCitations => Set<ConceptCitation>();
    public DbSet<RubricPoint> RubricPoints => Set<RubricPoint>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamAssignment> ExamAssignments => Set<ExamAssignment>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<SessionTurn> SessionTurns => Set<SessionTurn>();
    public DbSet<SessionClaim> SessionClaims => Set<SessionClaim>();
    public DbSet<Judgment> Judgments => Set<Judgment>();
    public DbSet<JudgeVoteRecord> JudgeVotes => Set<JudgeVoteRecord>();
    public DbSet<ConceptMasteryRecord> ConceptMastery => Set<ConceptMasteryRecord>();
    public DbSet<IntegrityEvent> IntegrityEvents => Set<IntegrityEvent>();
    public DbSet<SessionReport> SessionReports => Set<SessionReport>();
    public DbSet<GradeDecision> GradeDecisions => Set<GradeDecision>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<UsageEntry> UsageEntries => Set<UsageEntry>();
    public DbSet<Job> Jobs => Set<Job>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<ProcessingStatus>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<RubricPointKind>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<ExamStatus>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<ExamMode>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<IntegrityLevel>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<SessionState>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<IntegritySeverity>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<DeliveryStatus>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<UsageKind>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<JobState>().HaveConversion<string>().HaveMaxLength(32);
        c.Properties<decimal>().HavePrecision(18, 6);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
        b.ApplyMindAtticAuthConfiguration();   // the library's tables in the "auth" schema

        b.Entity<Organization>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.HasIndex(x => x.Slug).IsUnique();
        });
        b.Entity<OrganizationMember>(e =>
        {
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<OrganizationSettings>().HasKey(x => x.OrganizationId);

        b.Entity<SourceCollection>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasIndex(x => new { x.OrganizationId, x.Title });
            e.HasMany(x => x.Documents).WithOne(x => x.Collection).HasForeignKey(x => x.CollectionId);
        });
        b.Entity<SourceDocument>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(400);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageRef).HasMaxLength(400);
            e.HasIndex(x => new { x.CollectionId, x.Sha256 }).IsUnique();
        });
        b.Entity<SourceChunk>(e =>
        {
            e.Property(x => x.Location).HasMaxLength(300);
            e.Property(x => x.EmbeddingModel).HasMaxLength(100);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId);
            e.HasOne<SourceCollection>().WithMany().HasForeignKey(x => x.CollectionId);
            e.HasIndex(x => new { x.DocumentId, x.Ordinal }).IsUnique();
            e.HasIndex(x => x.CollectionId);
        });

        b.Entity<Concept>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<SourceCollection>().WithMany().HasForeignKey(x => x.CollectionId);
            e.HasIndex(x => new { x.CollectionId, x.Key }).IsUnique();
            e.HasMany(x => x.Citations).WithOne().HasForeignKey(x => x.ConceptId);
            e.HasMany(x => x.RubricPoints).WithOne().HasForeignKey(x => x.ConceptId);
        });
        b.Entity<ConceptRelation>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(60);
            e.HasOne<Concept>().WithMany().HasForeignKey(x => x.FromConceptId);
            e.HasOne<Concept>().WithMany().HasForeignKey(x => x.ToConceptId);
            e.HasIndex(x => new { x.FromConceptId, x.ToConceptId, x.Kind }).IsUnique();
        });
        b.Entity<ConceptCitation>(e => e.HasOne<SourceChunk>().WithMany().HasForeignKey(x => x.ChunkId));
        b.Entity<RubricPoint>(e => e.HasIndex(x => new { x.ConceptId, x.RubricVersion }));

        b.Entity<Exam>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.VoiceId).HasMaxLength(100);
            e.Property(x => x.AvatarId).HasMaxLength(100);
            e.Property(x => x.PromptSetId).HasMaxLength(60);
            e.HasOne<SourceCollection>().WithMany().HasForeignKey(x => x.CollectionId);
            e.HasIndex(x => new { x.OrganizationId, x.Status });
        });
        b.Entity<ExamAssignment>(e =>
        {
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId);
            e.HasIndex(x => new { x.ExamId, x.StudentUserId }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.StudentUserId });
        });
        b.Entity<ExamSession>(e =>
        {
            e.Property(x => x.EndReason).HasMaxLength(32);
            e.Property(x => x.PromptSetSha256).HasMaxLength(64);
            e.Property(x => x.ConsentTextSha256).HasMaxLength(64);
            e.Property(x => x.ClientInfo).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Assignment).WithMany().HasForeignKey(x => x.AssignmentId);
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId);
            e.HasIndex(x => new { x.AssignmentId, x.Attempt }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.State });
            e.HasMany(x => x.Turns).WithOne(x => x.Session).HasForeignKey(x => x.SessionId);
        });
        b.Entity<SessionTurn>(e =>
        {
            e.Property(x => x.ConceptKey).HasMaxLength(120);
            e.Property(x => x.Move).HasMaxLength(32);
            e.Property(x => x.AnswerMode).HasMaxLength(16);
            e.Property(x => x.AudioRef).HasMaxLength(400);
            e.HasIndex(x => new { x.SessionId, x.Index }).IsUnique();
        });
        b.Entity<SessionClaim>(e =>
        {
            e.Property(x => x.ConceptKey).HasMaxLength(120);
            e.HasOne<ExamSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasOne<SessionTurn>().WithMany().HasForeignKey(x => x.TurnId);
            e.HasOne<SessionClaim>().WithMany().HasForeignKey(x => x.ContradictsClaimId);
            e.HasIndex(x => new { x.SessionId, x.ConceptKey });
        });
        b.Entity<Judgment>(e =>
        {
            e.HasOne<SessionTurn>().WithOne().HasForeignKey<Judgment>(x => x.TurnId);
            e.HasMany(x => x.Votes).WithOne().HasForeignKey(x => x.JudgmentId);
        });
        b.Entity<JudgeVoteRecord>(e =>
        {
            e.Property(x => x.ProviderId).HasMaxLength(60);
            e.Property(x => x.Model).HasMaxLength(120);
        });
        b.Entity<ConceptMasteryRecord>(e =>
        {
            e.Property(x => x.ConceptKey).HasMaxLength(120);
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasOne<ExamSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.ConceptKey }).IsUnique();
        });
        b.Entity<IntegrityEvent>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.Type).HasMaxLength(60);
            e.HasOne<ExamSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.StartedAt });
        });
        b.Entity<SessionReport>(e =>
        {
            e.Property(x => x.Recommendation).HasMaxLength(40);
            e.HasOne<ExamSession>().WithOne().HasForeignKey<SessionReport>(x => x.SessionId);
        });
        b.Entity<GradeDecision>(e =>
        {
            e.Property(x => x.Grade).HasMaxLength(40);
            e.HasOne<ExamSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.DecidedAt });
        });

        b.Entity<ApiKey>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Prefix).HasMaxLength(16);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.Property(x => x.Scopes).HasMaxLength(400);
            e.HasIndex(x => x.KeyHash).IsUnique();
        });
        b.Entity<Webhook>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Events).HasMaxLength(400);
        });
        b.Entity<WebhookDelivery>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(60);
            e.HasOne<Webhook>().WithMany().HasForeignKey(x => x.WebhookId);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        b.Entity<UsageEntry>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(60);
            e.Property(x => x.Model).HasMaxLength(120);
            e.Property(x => x.Purpose).HasMaxLength(60);
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
            e.HasIndex(x => x.SessionId);
        });
        b.Entity<Job>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(60);
            e.Property(x => x.LockedBy).HasMaxLength(120);
            e.Property(x => x.ProgressMessage).HasMaxLength(400);
            e.HasIndex(x => new { x.State, x.RunAfter });
        });

        // Never cascade in Gadfly's schema: deletions are explicit, audited jobs (retention, data-subject
        // deletion). The library's own auth-schema relationships keep the behavior it configured.
        foreach (var fk in b.Model.GetEntityTypes().Where(t => t.GetSchema() == Schema).SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        // Tenant isolation: every ITenantOwned entity gets the organization filter.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
            b.Entity(type.ClrType).HasQueryFilter(TenantFilter(type.ClrType));
    }

    private LambdaExpression TenantFilter(Type entity)
    {
        // e => TenantBypass || e.OrganizationId == TenantOrganizationId
        var e = Expression.Parameter(entity, "e");
        var ctx = Expression.Constant(this);
        var bypass = Expression.Property(ctx, nameof(TenantBypass));
        var orgId = Expression.Convert(Expression.Property(e, nameof(ITenantOwned.OrganizationId)), typeof(Guid?));
        var current = Expression.Property(ctx, nameof(TenantOrganizationId));
        return Expression.Lambda(Expression.OrElse(bypass, Expression.Equal(orgId, current)), e);
    }

    /// <summary>
    /// Write guard (GAD-T42): inside a request, a tenant-owned row can only be written into the caller's
    /// active organization, whatever the code above it got wrong.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void GuardTenantWrites()
    {
        if (TenantBypass) return;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (TenantOrganizationId is null || entry.Entity.OrganizationId != TenantOrganizationId)
                throw new InvalidOperationException(
                    $"Refused to write a {entry.Entity.GetType().Name} outside the caller's organization.");
        }
    }
}

/// <summary>Where the platform's SQL Server lives.</summary>
public static class GadflyDatabase
{
    /// <summary>Connection-string name: <c>ConnectionStrings:Gadfly</c> (env var <c>ConnectionStrings__Gadfly</c>).</summary>
    public const string ConnectionStringName = "Gadfly";

    /// <summary>
    /// Development default: the per-user SQL Server Express LocalDB instance. Production sets
    /// <c>ConnectionStrings__Gadfly</c> to Azure SQL (Active Directory Default auth, no secret in the string).
    /// </summary>
    public const string LocalDbConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=Gadfly;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";

    public static string Resolve(IConfiguration? configuration) =>
        configuration?.GetConnectionString(ConnectionStringName)
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__" + ConnectionStringName)
        ?? LocalDbConnectionString;

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", GadflyDbContext.Schema);
            sql.EnableRetryOnFailure();
        });
}

/// <summary>Design-time factory so <c>dotnet ef migrations add … --project Gadfly.Server</c> works without starting the host.</summary>
public sealed class GadflyDbContextFactory : IDesignTimeDbContextFactory<GadflyDbContext>
{
    public GadflyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<GadflyDbContext>();
        GadflyDatabase.Configure(builder, GadflyDatabase.Resolve(null));
        return new GadflyDbContext(builder.Options);
    }
}
