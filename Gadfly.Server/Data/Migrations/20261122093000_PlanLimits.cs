using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Plans, seats and licences (GAD-T309): the plan name and its seat/exam limits on OrganizationSettings, trial
    /// timing, and the renewal date a platform admin sets. Also lays the schema GAD-T310 (email notifications)
    /// needs: the organization's own on/off switch, a student's reminder opt-out, and the sent-log. Written by
    /// hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122093000_PlanLimits")]
    public partial class PlanLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan", schema: "gadfly", table: "OrganizationSettings",
                type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Trial");
            migrationBuilder.AddColumn<int>(
                name: "StaffSeatLimit", schema: "gadfly", table: "OrganizationSettings",
                type: "int", nullable: false, defaultValue: 3);
            migrationBuilder.AddColumn<int>(
                name: "StudentSeatLimit", schema: "gadfly", table: "OrganizationSettings",
                type: "int", nullable: false, defaultValue: 25);
            migrationBuilder.AddColumn<int>(
                name: "MonthlyExamAllowance", schema: "gadfly", table: "OrganizationSettings",
                type: "int", nullable: true, defaultValue: 50);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialStartedAt", schema: "gadfly", table: "OrganizationSettings",
                type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanRenewsAt", schema: "gadfly", table: "OrganizationSettings",
                type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "PlanNote", schema: "gadfly", table: "OrganizationSettings",
                type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled", schema: "gadfly", table: "OrganizationSettings",
                type: "bit", nullable: false, defaultValue: true);

            migrationBuilder.CreateTable(
                name: "StudentNotificationPreferences",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemindersOptedOut = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotificationPreferences", x => x.Id);
                });
            migrationBuilder.CreateIndex(
                name: "IX_StudentNotificationPreferences_OrganizationId_UserId",
                schema: "gadfly",
                table: "StudentNotificationPreferences",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "EmailLogs",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLogs", x => x.Id);
                });
            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_OrganizationId_CreatedAt",
                schema: "gadfly",
                table: "EmailLogs",
                columns: new[] { "OrganizationId", "CreatedAt" });
            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_Kind_RelatedId",
                schema: "gadfly",
                table: "EmailLogs",
                columns: new[] { "Kind", "RelatedId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailLogs", schema: "gadfly");
            migrationBuilder.DropTable(name: "StudentNotificationPreferences", schema: "gadfly");
            migrationBuilder.DropColumn(name: "EmailNotificationsEnabled", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "PlanNote", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "PlanRenewsAt", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "TrialStartedAt", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "MonthlyExamAllowance", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "StudentSeatLimit", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "StaffSeatLimit", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "Plan", schema: "gadfly", table: "OrganizationSettings");
        }
    }
}
