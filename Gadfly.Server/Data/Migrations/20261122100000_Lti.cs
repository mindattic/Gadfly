using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// LTI 1.3 (GAD-T312): a platform registration per LMS connection (issuer, client id, deployment id, its OIDC and
    /// JWKS endpoints), Gadfly's own signing key, the short-lived OIDC login state, the placement-to-exam mapping
    /// deep linking creates (with its AGS/NRPS endpoint URLs), the LTI-subject-to-Gadfly-user map, and the
    /// assignment-to-placement link grade passback reads. Written by hand with no target-model designer, so it
    /// carries no other change pending in the model. No DB-level foreign keys, matching every other table here.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122100000_Lti")]
    public partial class Lti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LtiToolKeys",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kid = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProtectedPrivateKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublicJwkJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table => { table.PrimaryKey("PK_LtiToolKeys", x => x.Id); });

            migrationBuilder.CreateTable(
                name: "LtiRegistrations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeploymentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuthLoginUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AuthTokenUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    KeySetUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table => { table.PrimaryKey("PK_LtiRegistrations", x => x.Id); });
            migrationBuilder.CreateIndex(
                name: "IX_LtiRegistrations_Issuer_ClientId_DeploymentId", schema: "gadfly", table: "LtiRegistrations",
                columns: new[] { "Issuer", "ClientId", "DeploymentId" }, unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_LtiRegistrations_OrganizationId", schema: "gadfly", table: "LtiRegistrations", column: "OrganizationId");

            migrationBuilder.CreateTable(
                name: "LtiLoginStates",
                schema: "gadfly",
                columns: table => new
                {
                    State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetLinkUri = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                },
                constraints: table => { table.PrimaryKey("PK_LtiLoginStates", x => x.State); });
            migrationBuilder.CreateIndex(
                name: "IX_LtiLoginStates_ExpiresAt", schema: "gadfly", table: "LtiLoginStates", column: "ExpiresAt");

            migrationBuilder.CreateTable(
                name: "LtiResourceLinks",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ResourceLinkId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineItemsUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LineItemUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContextMembershipsUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastRosterSyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                },
                constraints: table => { table.PrimaryKey("PK_LtiResourceLinks", x => x.Id); });
            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_RegistrationId_ContextId_ResourceLinkId", schema: "gadfly", table: "LtiResourceLinks",
                columns: new[] { "RegistrationId", "ContextId", "ResourceLinkId" }, unique: true);

            migrationBuilder.CreateTable(
                name: "LtiUserMappings",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LtiSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    GadflyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table => { table.PrimaryKey("PK_LtiUserMappings", x => x.Id); });
            migrationBuilder.CreateIndex(
                name: "IX_LtiUserMappings_RegistrationId_LtiSubject", schema: "gadfly", table: "LtiUserMappings",
                columns: new[] { "RegistrationId", "LtiSubject" }, unique: true);

            migrationBuilder.CreateTable(
                name: "LtiAssignmentLinks",
                schema: "gadfly",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LtiSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                },
                constraints: table => { table.PrimaryKey("PK_LtiAssignmentLinks", x => x.AssignmentId); });
            migrationBuilder.CreateIndex(
                name: "IX_LtiAssignmentLinks_ResourceLinkId", schema: "gadfly", table: "LtiAssignmentLinks", column: "ResourceLinkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LtiAssignmentLinks", schema: "gadfly");
            migrationBuilder.DropTable(name: "LtiUserMappings", schema: "gadfly");
            migrationBuilder.DropTable(name: "LtiResourceLinks", schema: "gadfly");
            migrationBuilder.DropTable(name: "LtiLoginStates", schema: "gadfly");
            migrationBuilder.DropTable(name: "LtiRegistrations", schema: "gadfly");
            migrationBuilder.DropTable(name: "LtiToolKeys", schema: "gadfly");
        }
    }
}
