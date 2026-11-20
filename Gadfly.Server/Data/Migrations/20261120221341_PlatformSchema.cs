using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gadfly");

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    RunAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Progress = table.Column<double>(type: "float", nullable: false),
                    ProgressMessage = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationSettings",
                schema: "gadfly",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    MonthlyCostQuotaUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DefaultVoiceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultAvatarId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSettings", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "SourceCollections",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConceptMapVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCollections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageEntries",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    InputUnits = table.Column<double>(type: "float", nullable: false),
                    OutputUnits = table.Column<double>(type: "float", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Events = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Concepts",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    EditedByInstructor = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Concepts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Concepts_SourceCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "gadfly",
                        principalTable: "SourceCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IntegrityLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VoiceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AvatarId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptSetId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PromptSetVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_SourceCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "gadfly",
                        principalTable: "SourceCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_SourceCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "gadfly",
                        principalTable: "SourceCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WebhookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResponseCode = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalSchema: "gadfly",
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptRelations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptRelations_Concepts_FromConceptId",
                        column: x => x.FromConceptId,
                        principalSchema: "gadfly",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptRelations_Concepts_ToConceptId",
                        column: x => x.ToConceptId,
                        principalSchema: "gadfly",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RubricPoints",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RubricVersion = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditedByInstructor = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricPoints_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "gadfly",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamAssignments",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpensAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosesAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptsAllowed = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultsReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAssignments_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceChunks",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceChunks_SourceCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalSchema: "gadfly",
                        principalTable: "SourceCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceChunks_SourceDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "gadfly",
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSessions",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EndReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptSetSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConceptMapVersion = table.Column<int>(type: "int", nullable: false),
                    ConsentRecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsentTextSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClientInfo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSessions_ExamAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "gadfly",
                        principalTable: "ExamAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSessions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptCitations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptCitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptCitations_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "gadfly",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptCitations_SourceChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalSchema: "gadfly",
                        principalTable: "SourceChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptMastery",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Estimate = table.Column<double>(type: "float", nullable: false),
                    TurnsOnConcept = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptMastery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptMastery_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeDecisions",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<double>(type: "float", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverridesRecommendation = table.Column<bool>(type: "bit", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeDecisions_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityEvents",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrityEvents_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionReports",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Recommendation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OverallScore = table.Column<double>(type: "float", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Markdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntegritySummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionReports_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionTurns",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Move = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlannerReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetClaimIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    AnswerStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnswerCompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpeechMetricsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AudioRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    MasteryAfter = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionTurns_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Judgments",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverallScore = table.Column<double>(type: "float", nullable: false),
                    Agreement = table.Column<double>(type: "float", nullable: false),
                    NeedsHumanReview = table.Column<bool>(type: "bit", nullable: false),
                    RubricVersion = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextStyleScore = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Judgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Judgments_SessionTurns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "gadfly",
                        principalTable: "SessionTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionClaims",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Normalized = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContradictsClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionClaims_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionClaims_SessionClaims_ContradictsClaimId",
                        column: x => x.ContradictsClaimId,
                        principalSchema: "gadfly",
                        principalTable: "SessionClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionClaims_SessionTurns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "gadfly",
                        principalTable: "SessionTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JudgeVotes",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JudgmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OverallScore = table.Column<double>(type: "float", nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JudgeVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JudgeVotes_Judgments_JudgmentId",
                        column: x => x.JudgmentId,
                        principalSchema: "gadfly",
                        principalTable: "Judgments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                schema: "gadfly",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptCitations_ChunkId",
                schema: "gadfly",
                table: "ConceptCitations",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptCitations_ConceptId",
                schema: "gadfly",
                table: "ConceptCitations",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMastery_SessionId_ConceptKey",
                schema: "gadfly",
                table: "ConceptMastery",
                columns: new[] { "SessionId", "ConceptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRelations_FromConceptId_ToConceptId_Kind",
                schema: "gadfly",
                table: "ConceptRelations",
                columns: new[] { "FromConceptId", "ToConceptId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRelations_ToConceptId",
                schema: "gadfly",
                table: "ConceptRelations",
                column: "ToConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_Concepts_CollectionId_Key",
                schema: "gadfly",
                table: "Concepts",
                columns: new[] { "CollectionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_ExamId_StudentUserId",
                schema: "gadfly",
                table: "ExamAssignments",
                columns: new[] { "ExamId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_OrganizationId_StudentUserId",
                schema: "gadfly",
                table: "ExamAssignments",
                columns: new[] { "OrganizationId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CollectionId",
                schema: "gadfly",
                table: "Exams",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_OrganizationId_Status",
                schema: "gadfly",
                table: "Exams",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_AssignmentId_Attempt",
                schema: "gadfly",
                table: "ExamSessions",
                columns: new[] { "AssignmentId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_ExamId",
                schema: "gadfly",
                table: "ExamSessions",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_OrganizationId_State",
                schema: "gadfly",
                table: "ExamSessions",
                columns: new[] { "OrganizationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeDecisions_SessionId_DecidedAt",
                schema: "gadfly",
                table: "GradeDecisions",
                columns: new[] { "SessionId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityEvents_SessionId_StartedAt",
                schema: "gadfly",
                table: "IntegrityEvents",
                columns: new[] { "SessionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_State_RunAfter",
                schema: "gadfly",
                table: "Jobs",
                columns: new[] { "State", "RunAfter" });

            migrationBuilder.CreateIndex(
                name: "IX_JudgeVotes_JudgmentId",
                schema: "gadfly",
                table: "JudgeVotes",
                column: "JudgmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Judgments_TurnId",
                schema: "gadfly",
                table: "Judgments",
                column: "TurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RubricPoints_ConceptId_RubricVersion",
                schema: "gadfly",
                table: "RubricPoints",
                columns: new[] { "ConceptId", "RubricVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionClaims_ContradictsClaimId",
                schema: "gadfly",
                table: "SessionClaims",
                column: "ContradictsClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionClaims_SessionId_ConceptKey",
                schema: "gadfly",
                table: "SessionClaims",
                columns: new[] { "SessionId", "ConceptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionClaims_TurnId",
                schema: "gadfly",
                table: "SessionClaims",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReports_SessionId",
                schema: "gadfly",
                table: "SessionReports",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionTurns_SessionId_Index",
                schema: "gadfly",
                table: "SessionTurns",
                columns: new[] { "SessionId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceChunks_CollectionId",
                schema: "gadfly",
                table: "SourceChunks",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceChunks_DocumentId_Ordinal",
                schema: "gadfly",
                table: "SourceChunks",
                columns: new[] { "DocumentId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceCollections_OrganizationId_Title",
                schema: "gadfly",
                table: "SourceCollections",
                columns: new[] { "OrganizationId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_CollectionId_Sha256",
                schema: "gadfly",
                table: "SourceDocuments",
                columns: new[] { "CollectionId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageEntries_OrganizationId_CreatedAt",
                schema: "gadfly",
                table: "UsageEntries",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEntries_SessionId",
                schema: "gadfly",
                table: "UsageEntries",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Status_NextAttemptAt",
                schema: "gadfly",
                table: "WebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId",
                schema: "gadfly",
                table: "WebhookDeliveries",
                column: "WebhookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ConceptCitations",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ConceptMastery",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ConceptRelations",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradeDecisions",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "IntegrityEvents",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Jobs",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "JudgeVotes",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "OrganizationSettings",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "RubricPoints",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SessionClaims",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SessionReports",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "UsageEntries",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SourceChunks",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Judgments",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Concepts",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Webhooks",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SourceDocuments",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SessionTurns",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ExamSessions",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ExamAssignments",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Exams",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "SourceCollections",
                schema: "gadfly");
        }
    }
}
