using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CourseId",
                schema: "gadfly",
                table: "SourceCollections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentMode",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CourseId",
                schema: "gadfly",
                table: "Exams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ApprovalRecords",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinalGrades",
                schema: "gadfly",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FinalPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LetterOrLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalGrades", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_FinalGrades_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeAppeals",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeAppeals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GradingPolicies",
                schema: "gadfly",
                columns: table => new
                {
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiWeight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    HumanWeight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AdditionalReviewAt = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MandatoryReconciliationAt = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BlindHumanGrading = table.Column<bool>(type: "bit", nullable: false),
                    InconclusiveProcedure = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AllowAssistedEvidence = table.Column<bool>(type: "bit", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingPolicies", x => x.ExamId);
                    table.ForeignKey(
                        name: "FK_GradingPolicies_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradingPolicyChanges",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingPolicyChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradingPolicyChanges_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningObjectives",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningObjectives_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentResponses",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RevisesResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Modality = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OriginalText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SpeechRecognitionText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpeechRecognitionConfidence = table.Column<double>(type: "float", nullable: true),
                    VerificationState = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VerifiedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Assisted = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentResponses_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gadfly",
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentResponses_SessionTurns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "gadfly",
                        principalTable: "SessionTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentResponses_StudentResponses_RevisesResponseId",
                        column: x => x.RevisesResponseId,
                        principalSchema: "gadfly",
                        principalTable: "StudentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiObjectiveGrades",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MisconceptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AmbiguitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewFlagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsMock = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiObjectiveGrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiObjectiveGrades_LearningObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "gadfly",
                        principalTable: "LearningObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeDisagreements",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    HumanScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DifferencePoints = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Band = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeDisagreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeDisagreements_LearningObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "gadfly",
                        principalTable: "LearningObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HumanObjectiveGrades",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvidenceNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WasBlind = table.Column<bool>(type: "bit", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanObjectiveGrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HumanObjectiveGrades_LearningObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "gadfly",
                        principalTable: "LearningObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RubricCriteria",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricCriteria_LearningObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "gadfly",
                        principalTable: "LearningObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentEvidence",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TranscriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Stance = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Assisted = table.Column<bool>(type: "bit", nullable: false),
                    Contaminated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentEvidence_LearningObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "gadfly",
                        principalTable: "LearningObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentEvidence_StudentResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalSchema: "gadfly",
                        principalTable: "StudentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClarificationEvents",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NonLeadingChecked = table.Column<bool>(type: "bit", nullable: false),
                    StudentReply = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClarificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClarificationEvents_StudentResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalSchema: "gadfly",
                        principalTable: "StudentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResponseVerifications",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    To = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponseVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponseVerifications_StudentResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalSchema: "gadfly",
                        principalTable: "StudentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptVersions",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectionKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptVersions_StudentResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalSchema: "gadfly",
                        principalTable: "StudentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeReconciliations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisagreementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecondReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cause = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedAiScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ResolvedHumanScore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeReconciliations_GradeDisagreements_DisagreementId",
                        column: x => x.DisagreementId,
                        principalSchema: "gadfly",
                        principalTable: "GradeDisagreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RubricLevelDescriptors",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Descriptor = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricLevelDescriptors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricLevelDescriptors_RubricCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalSchema: "gadfly",
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCollections_CourseId",
                schema: "gadfly",
                table: "SourceCollections",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CourseId",
                schema: "gadfly",
                table: "Exams",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_AiObjectiveGrades_ObjectiveId",
                schema: "gadfly",
                table: "AiObjectiveGrades",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_AiObjectiveGrades_SessionId_ObjectiveId",
                schema: "gadfly",
                table: "AiObjectiveGrades",
                columns: new[] { "SessionId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecords_SessionId",
                schema: "gadfly",
                table: "ApprovalRecords",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentEvidence_ObjectiveId",
                schema: "gadfly",
                table: "AssessmentEvidence",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentEvidence_ResponseId",
                schema: "gadfly",
                table: "AssessmentEvidence",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentEvidence_SessionId_ObjectiveId",
                schema: "gadfly",
                table: "AssessmentEvidence",
                columns: new[] { "SessionId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_At",
                schema: "gadfly",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SessionId",
                schema: "gadfly",
                table: "AuditEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationEvents_ResponseId",
                schema: "gadfly",
                table: "ClarificationEvents",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationEvents_SessionId",
                schema: "gadfly",
                table: "ClarificationEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrganizationId_Title",
                schema: "gadfly",
                table: "Courses",
                columns: new[] { "OrganizationId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_FinalGrades_OrganizationId_Status",
                schema: "gadfly",
                table: "FinalGrades",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeAppeals_SessionId",
                schema: "gadfly",
                table: "GradeAppeals",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeDisagreements_ObjectiveId",
                schema: "gadfly",
                table: "GradeDisagreements",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeDisagreements_SessionId",
                schema: "gadfly",
                table: "GradeDisagreements",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeReconciliations_DisagreementId",
                schema: "gadfly",
                table: "GradeReconciliations",
                column: "DisagreementId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingPolicyChanges_ExamId",
                schema: "gadfly",
                table: "GradingPolicyChanges",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_HumanObjectiveGrades_ObjectiveId",
                schema: "gadfly",
                table: "HumanObjectiveGrades",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_HumanObjectiveGrades_SessionId_ObjectiveId",
                schema: "gadfly",
                table: "HumanObjectiveGrades",
                columns: new[] { "SessionId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningObjectives_ExamId_Key",
                schema: "gadfly",
                table: "LearningObjectives",
                columns: new[] { "ExamId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResponseVerifications_ResponseId",
                schema: "gadfly",
                table: "ResponseVerifications",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriteria_ObjectiveId",
                schema: "gadfly",
                table: "RubricCriteria",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_RubricLevelDescriptors_CriterionId_Level",
                schema: "gadfly",
                table: "RubricLevelDescriptors",
                columns: new[] { "CriterionId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentResponses_RevisesResponseId",
                schema: "gadfly",
                table: "StudentResponses",
                column: "RevisesResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentResponses_SessionId_Sequence",
                schema: "gadfly",
                table: "StudentResponses",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentResponses_TurnId",
                schema: "gadfly",
                table: "StudentResponses",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptVersions_ResponseId_Version",
                schema: "gadfly",
                table: "TranscriptVersions",
                columns: new[] { "ResponseId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Courses_CourseId",
                schema: "gadfly",
                table: "Exams",
                column: "CourseId",
                principalSchema: "gadfly",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceCollections_Courses_CourseId",
                schema: "gadfly",
                table: "SourceCollections",
                column: "CourseId",
                principalSchema: "gadfly",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Courses_CourseId",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceCollections_Courses_CourseId",
                schema: "gadfly",
                table: "SourceCollections");

            migrationBuilder.DropTable(
                name: "AiObjectiveGrades",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ApprovalRecords",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "AssessmentEvidence",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ClarificationEvents",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Courses",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "FinalGrades",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradeAppeals",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradeReconciliations",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradingPolicies",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradingPolicyChanges",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "HumanObjectiveGrades",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "ResponseVerifications",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "RubricLevelDescriptors",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "TranscriptVersions",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "GradeDisagreements",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "RubricCriteria",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "StudentResponses",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "LearningObjectives",
                schema: "gadfly");

            migrationBuilder.DropIndex(
                name: "IX_SourceCollections_CourseId",
                schema: "gadfly",
                table: "SourceCollections");

            migrationBuilder.DropIndex(
                name: "IX_Exams_CourseId",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CourseId",
                schema: "gadfly",
                table: "SourceCollections");

            migrationBuilder.DropColumn(
                name: "AssessmentMode",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CourseId",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Subject",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Topic",
                schema: "gadfly",
                table: "Exams");
        }
    }
}
