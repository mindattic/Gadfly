using System;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Research use of exam records (GAD-T451, GAD-T452): an organization's research settings (off until switched on), study arms
    /// per exam and which arm each assignment is in, and the append-only research-consent decisions. New tables only; nothing
    /// existing changes. Written by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122110000_ResearchConsentAndStudyArms")]
    public partial class ResearchConsentAndStudyArms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResearchSettings",
                schema: "gadfly",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    StudyTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StudyContact = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    EthicsReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PseudonymKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MinimumGroupSize = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchSettings", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "StudyArms",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyArms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyArmMembers",
                schema: "gadfly",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyArmMembers", x => x.AssignmentId);
                });

            migrationBuilder.CreateTable(
                name: "ResearchConsents",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NoticeVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ConsentTextSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchConsents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyArms_OrganizationId_ExamId",
                schema: "gadfly",
                table: "StudyArms",
                columns: new[] { "OrganizationId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyArmMembers_ArmId",
                schema: "gadfly",
                table: "StudyArmMembers",
                column: "ArmId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchConsents_OrganizationId_StudentUserId_RecordedAt",
                schema: "gadfly",
                table: "ResearchConsents",
                columns: new[] { "OrganizationId", "StudentUserId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ResearchConsents", schema: "gadfly");
            migrationBuilder.DropTable(name: "StudyArmMembers", schema: "gadfly");
            migrationBuilder.DropTable(name: "StudyArms", schema: "gadfly");
            migrationBuilder.DropTable(name: "ResearchSettings", schema: "gadfly");
        }
    }
}
