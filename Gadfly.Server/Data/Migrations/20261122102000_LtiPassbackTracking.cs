using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// LTI 1.3 follow-up (GAD-T312): grade passback's own idempotency tracking on <see cref="LtiAssignmentLink"/>
    /// (GAD-T383: when a score was last sent and what it was, so the passback sweeper never resends an unchanged released
    /// result and does resend after an amendment), and <see cref="LtiRegistration.TokenAudience"/> (the client
    /// assertion's audience for platforms whose token endpoint wants one other than its URL, such as Brightspace).
    /// Written by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122102000_LtiPassbackTracking")]
    public partial class LtiPassbackTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PassbackSentAt", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<decimal>(
                name: "LastPassbackPercent", schema: "gadfly", table: "LtiAssignmentLinks",
                type: "decimal(18,6)", precision: 18, scale: 6, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "TokenAudience", schema: "gadfly", table: "LtiRegistrations",
                type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TokenAudience", schema: "gadfly", table: "LtiRegistrations");
            migrationBuilder.DropColumn(name: "LastPassbackPercent", schema: "gadfly", table: "LtiAssignmentLinks");
            migrationBuilder.DropColumn(name: "PassbackSentAt", schema: "gadfly", table: "LtiAssignmentLinks");
        }
    }
}
