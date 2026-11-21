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
public sealed class GadflyDbContext : DbContext, IOrganizationDataContext
{
    public const string Schema = "gadfly";

    private readonly ITenantContext? _tenant;

    public GadflyDbContext(DbContextOptions<GadflyDbContext> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    // Tenant isolation (GAD-T42). Read by the global query filters below on every query: system work
    // (a scope opened with CreateSystemScope, or a context built without a tenant: design time, tests)
    // sees every organization; anything else sees only its organization, and a caller with no
    // organization (signed out, no membership, no request inside a circuit) sees no tenant-owned rows.
    // A missing HttpContext is never system work (GAD-T73).
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

    // The library's opt-in organization tables (M.AUTH-T34/T35, GAD-T156), in the same "auth" schema.
    public DbSet<AuthOrganization> AuthOrganizations => Set<AuthOrganization>();
    public DbSet<AuthMembership> AuthMemberships => Set<AuthMembership>();
    public DbSet<AuthInvitation> AuthInvitations => Set<AuthInvitation>();
    public DbSet<AuthOrganizationAudit> AuthOrganizationAudit => Set<AuthOrganizationAudit>();
    /// <summary>Gadfly's names for the library's organization and membership sets (same sets).</summary>
    public DbSet<AuthOrganization> Organizations => AuthOrganizations;
    public DbSet<AuthMembership> OrganizationMembers => AuthMemberships;

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

    // Master-plan assessment model (docs/MASTER-PLAN.md Part 17.4).
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<LearningObjective> LearningObjectives => Set<LearningObjective>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<RubricLevelDescriptor> RubricLevelDescriptors => Set<RubricLevelDescriptor>();
    public DbSet<GradingPolicy> GradingPolicies => Set<GradingPolicy>();
    public DbSet<GradingPolicyChange> GradingPolicyChanges => Set<GradingPolicyChange>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<StudentResponse> StudentResponses => Set<StudentResponse>();
    public DbSet<TranscriptVersion> TranscriptVersions => Set<TranscriptVersion>();
    public DbSet<ResponseVerification> ResponseVerifications => Set<ResponseVerification>();
    public DbSet<ClarificationEvent> ClarificationEvents => Set<ClarificationEvent>();
    public DbSet<AssessmentEvidence> AssessmentEvidence => Set<AssessmentEvidence>();
    public DbSet<AiObjectiveGrade> AiObjectiveGrades => Set<AiObjectiveGrade>();
    public DbSet<HumanObjectiveGrade> HumanObjectiveGrades => Set<HumanObjectiveGrade>();
    public DbSet<GradeDisagreement> GradeDisagreements => Set<GradeDisagreement>();
    public DbSet<GradeReconciliation> GradeReconciliations => Set<GradeReconciliation>();
    public DbSet<FinalGrade> FinalGrades => Set<FinalGrade>();
    public DbSet<ApprovalRecord> ApprovalRecords => Set<ApprovalRecord>();
    public DbSet<GradeAppeal> GradeAppeals => Set<GradeAppeal>();

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
        c.Properties<Gadfly.Assessment.MasteryLevel>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<Gadfly.Assessment.AssessmentDimension>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<Gadfly.Assessment.AssessmentMode>().HaveConversion<string>().HaveMaxLength(20);
        c.Properties<Gadfly.Assessment.VerificationState>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<Gadfly.Assessment.CorrectionKind>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<Gadfly.Assessment.GradeStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<Gadfly.Assessment.DisagreementBand>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<InconclusiveProcedure>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<ResponseKind>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<ClarificationType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<EvidenceStance>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<ApprovalAction>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<AppealState>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
        b.ApplyMindAtticAuthConfiguration(organizations: true);   // the library's tables (accounts and organizations) in the "auth" schema

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

        // ---- master-plan assessment model
        b.Entity<Course>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.HasIndex(x => new { x.OrganizationId, x.Title });
        });
        b.Entity<Exam>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId);
        });
        b.Entity<SourceCollection>(e => e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId));
        b.Entity<LearningObjective>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.ConceptKey).HasMaxLength(120);
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId);
            e.HasIndex(x => new { x.ExamId, x.Key }).IsUnique();
            e.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.ObjectiveId);
        });
        b.Entity<RubricCriterion>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Levels).WithOne().HasForeignKey(x => x.CriterionId);
        });
        b.Entity<RubricLevelDescriptor>(e => e.HasIndex(x => new { x.CriterionId, x.Level }).IsUnique());
        b.Entity<GradingPolicy>(e =>
        {
            e.HasKey(x => x.ExamId);
            e.HasOne<Exam>().WithOne().HasForeignKey<GradingPolicy>(x => x.ExamId);
        });
        b.Entity<GradingPolicyChange>(e =>
        {
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId);
            e.Property(x => x.Reason).HasMaxLength(1000);
        });
        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.EntityType).HasMaxLength(80);
            e.Property(x => x.EntityId).HasMaxLength(80);
            e.Property(x => x.ActorRole).HasMaxLength(40);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.OrganizationId, x.At });
            e.HasIndex(x => x.SessionId);
        });
        b.Entity<StudentResponse>(e =>
        {
            e.Property(x => x.Modality).HasMaxLength(16);
            e.HasOne<ExamSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasOne<SessionTurn>().WithMany().HasForeignKey(x => x.TurnId);
            e.HasOne<StudentResponse>().WithMany().HasForeignKey(x => x.RevisesResponseId);
            e.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.ResponseId);
        });
        b.Entity<TranscriptVersion>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasIndex(x => new { x.ResponseId, x.Version }).IsUnique();
        });
        b.Entity<ResponseVerification>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<StudentResponse>().WithMany().HasForeignKey(x => x.ResponseId);
        });
        b.Entity<ClarificationEvent>(e =>
        {
            e.HasOne<StudentResponse>().WithMany().HasForeignKey(x => x.ResponseId);
            e.HasIndex(x => x.SessionId);
        });
        b.Entity<AssessmentEvidence>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasOne<LearningObjective>().WithMany().HasForeignKey(x => x.ObjectiveId);
            e.HasOne<StudentResponse>().WithMany().HasForeignKey(x => x.ResponseId);
            e.HasIndex(x => new { x.SessionId, x.ObjectiveId });
        });
        b.Entity<AiObjectiveGrade>(e =>
        {
            e.Property(x => x.Confidence).HasMaxLength(40);
            e.HasOne<LearningObjective>().WithMany().HasForeignKey(x => x.ObjectiveId);
            e.HasIndex(x => new { x.SessionId, x.ObjectiveId });
        });
        b.Entity<HumanObjectiveGrade>(e =>
        {
            e.Property(x => x.ReviewerRole).HasMaxLength(40);
            e.HasOne<LearningObjective>().WithMany().HasForeignKey(x => x.ObjectiveId);
            e.HasIndex(x => new { x.SessionId, x.ObjectiveId });
        });
        b.Entity<GradeDisagreement>(e =>
        {
            e.HasOne<LearningObjective>().WithMany().HasForeignKey(x => x.ObjectiveId);
            e.HasIndex(x => x.SessionId);
        });
        b.Entity<GradeReconciliation>(e =>
        {
            e.Property(x => x.Cause).HasMaxLength(60);
            e.HasOne<GradeDisagreement>().WithMany().HasForeignKey(x => x.DisagreementId);
        });
        b.Entity<FinalGrade>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.Property(x => x.LetterOrLabel).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<ExamSession>().WithOne().HasForeignKey<FinalGrade>(x => x.SessionId);
            e.HasIndex(x => new { x.OrganizationId, x.Status });
        });
        b.Entity<ApprovalRecord>(e =>
        {
            e.Property(x => x.ActorRole).HasMaxLength(40);
            e.HasIndex(x => x.SessionId);
        });
        b.Entity<GradeAppeal>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(4000);
            e.HasIndex(x => x.SessionId);
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
        // The audit log is append-only for everyone, system work included (GAD-T84).
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Audit events are append-only; they cannot be changed or deleted.");
        // A response's original text is immutable (master plan Part 4.2).
        foreach (var entry in ChangeTracker.Entries<StudentResponse>())
            if (entry.State == EntityState.Modified && entry.Property(x => x.OriginalText).IsModified)
                throw new InvalidOperationException("A student's original response cannot be rewritten.");

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
