using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// How long each outside call on a live exam took (GAD-T413): panel calls, the examiner's ElevenLabs voice and the
    /// speech-recognition hub, one row per call. Platform diagnostics, not tenant-owned. Written by hand with no target-model
    /// designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122073000_ApiCallTimings")]
    public partial class ApiCallTimings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiCallTimings",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TurnIndex = table.Column<int>(type: "int", nullable: true),
                    CallName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCallTimings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCallTimings_SessionId_StartedAtUtc",
                schema: "gadfly",
                table: "ApiCallTimings",
                columns: new[] { "SessionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCallTimings_StartedAtUtc",
                schema: "gadfly",
                table: "ApiCallTimings",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ApiCallTimings", schema: "gadfly");
        }
    }
}
