using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// The audit log viewer (GAD-T301) filters an organization's events by who acted, by action and by record, newest first:
    /// three indexes that lead with the organization so each filter is an index seek, not a scan of the whole log.
    /// No column changes. Written by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122033000_AuditLogIndexes")]
    public partial class AuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_ActorUserId_At",
                schema: "gadfly",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "ActorUserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_Action_At",
                schema: "gadfly",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "Action", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_EntityId",
                schema: "gadfly",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AuditEvents_OrganizationId_ActorUserId_At", schema: "gadfly", table: "AuditEvents");
            migrationBuilder.DropIndex(name: "IX_AuditEvents_OrganizationId_Action_At", schema: "gadfly", table: "AuditEvents");
            migrationBuilder.DropIndex(name: "IX_AuditEvents_OrganizationId_EntityId", schema: "gadfly", table: "AuditEvents");
        }
    }
}
