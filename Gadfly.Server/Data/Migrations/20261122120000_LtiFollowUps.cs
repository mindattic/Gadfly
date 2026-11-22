using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// LTI follow-ups (GAD-T458): a scheduled roster refresh per connection (<see cref="LtiRegistration.RosterRefreshHours"/>,
    /// 24 hours by default); grade-passback retries with backoff and a failed state on <see cref="LtiAssignmentLink"/>; the
    /// platform's login hints on <see cref="LtiLoginState"/>, so a launch whose cookie a frame blocked can restart in a new
    /// window; and <see cref="LtiLinkRequest"/>, an LMS identity waiting to be linked to an existing account. Written by hand
    /// with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122120000_LtiFollowUps")]
    public partial class LtiFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RosterRefreshHours", schema: "gadfly", table: "LtiRegistrations",
                type: "int", nullable: false, defaultValue: 24);

            migrationBuilder.AddColumn<string>(
                name: "LoginHint", schema: "gadfly", table: "LtiLoginStates",
                type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "LtiMessageHint", schema: "gadfly", table: "LtiLoginStates",
                type: "nvarchar(4000)", maxLength: 4000, nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PassbackPendingPercent", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "decimal(18,6)", precision: 18, scale: 6, nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "PassbackAttempts", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PassbackRetryAt", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "PassbackError", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "nvarchar(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PassbackFailedAt", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "datetimeoffset", nullable: true);

            migrationBuilder.CreateTable(
                name: "LtiLinkRequests",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LtiSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    LmsEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    LmsName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BrowserTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EmailSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EmailTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiLinkRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LtiLinkRequests_RegistrationId_LtiSubject", schema: "gadfly", table: "LtiLinkRequests",
                columns: new[] { "RegistrationId", "LtiSubject" }, unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_LtiLinkRequests_OrganizationId_Status", schema: "gadfly", table: "LtiLinkRequests",
                columns: new[] { "OrganizationId", "Status" });
            migrationBuilder.CreateIndex(
                name: "IX_LtiLinkRequests_EmailTokenHash", schema: "gadfly", table: "LtiLinkRequests",
                column: "EmailTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LtiLinkRequests", schema: "gadfly");
            migrationBuilder.DropColumn(name: "PassbackFailedAt", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "PassbackError", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "PassbackRetryAt", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "PassbackAttempts", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "PassbackPendingPercent", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "LtiMessageHint", schema: "gadfly", table: "LtiLoginStates");
            migrationBuilder.DropColumn(name: "LoginHint", schema: "gadfly", table: "LtiLoginStates");
            migrationBuilder.DropColumn(name: "RosterRefreshHours", schema: "gadfly", table: "LtiRegistrations");
        }
    }
}
