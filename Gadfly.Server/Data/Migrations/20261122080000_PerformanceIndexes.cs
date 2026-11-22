using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Performance pass (GAD-T389): indexes for queries the admin pages, grading queue and dashboard already run, that no
    /// earlier migration covered. No column changes. Written by hand with no target-model designer, so it carries no other
    /// change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122080000_PerformanceIndexes")]
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The transcript verification page (/transcripts/verify, anonymous, GAD-T295) looks up a transcript.issued audit
            // row by its code alone, before it knows which organization issued it: a global index on the action and record
            // leads it straight there instead of scanning every audit row.
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action_EntityType_EntityId",
                schema: "gadfly",
                table: "AuditEvents",
                columns: new[] { "Action", "EntityType", "EntityId" });

            // The student profile and directory, the live session monitor, grading and review pages all look up a student's
            // sessions within the organization; only AssignmentId and ExamId were indexed before.
            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_OrganizationId_StudentUserId",
                schema: "gadfly",
                table: "ExamSessions",
                columns: new[] { "OrganizationId", "StudentUserId" });

            // The admin dashboard's 7/30-day windows (GAD-T300) filter ended sessions by organization and a date floor on
            // EndedAt, then take a bounded, ordered sample: the existing (OrganizationId, State) index left the date bound
            // and the order to a sort over every ended session in the organization.
            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_OrganizationId_State_EndedAt",
                schema: "gadfly",
                table: "ExamSessions",
                columns: new[] { "OrganizationId", "State", "EndedAt" });

            // The dashboard's failed-jobs widget (GAD-T300), loaded on every dashboard view, filters the (tenant-unfiltered)
            // job table by organization and state, then a recency floor; only (State, RunAfter) was indexed before.
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_OrganizationId_State_CompletedAt",
                schema: "gadfly",
                table: "Jobs",
                columns: new[] { "OrganizationId", "State", "CompletedAt" });

            // The grading queue's per-session AI-grading job status (GradingService.LatestJobsAsync), loaded on every queue
            // view, filters by kind and organization, newest first.
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_OrganizationId_Kind_CreatedAt",
                schema: "gadfly",
                table: "Jobs",
                columns: new[] { "OrganizationId", "Kind", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AuditEvents_Action_EntityType_EntityId", schema: "gadfly", table: "AuditEvents");
            migrationBuilder.DropIndex(name: "IX_ExamSessions_OrganizationId_StudentUserId", schema: "gadfly", table: "ExamSessions");
            migrationBuilder.DropIndex(name: "IX_ExamSessions_OrganizationId_State_EndedAt", schema: "gadfly", table: "ExamSessions");
            migrationBuilder.DropIndex(name: "IX_Jobs_OrganizationId_State_CompletedAt", schema: "gadfly", table: "Jobs");
            migrationBuilder.DropIndex(name: "IX_Jobs_OrganizationId_Kind_CreatedAt", schema: "gadfly", table: "Jobs");
        }
    }
}
