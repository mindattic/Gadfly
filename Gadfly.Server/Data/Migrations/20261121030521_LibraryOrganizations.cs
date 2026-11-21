using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthOrganizationAudit",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthOrganizationAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthOrganizations",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthOrganizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthInvitations",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSentUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SendCount = table.Column<int>(type: "int", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthInvitations_AuthOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "auth",
                        principalTable: "AuthOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthMemberships",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthMemberships_AuthOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "auth",
                        principalTable: "AuthOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthMemberships_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_OrganizationId_NormalizedEmail",
                schema: "auth",
                table: "AuthInvitations",
                columns: new[] { "OrganizationId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthInvitations_TokenHash",
                schema: "auth",
                table: "AuthInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthMemberships_OrganizationId_UserId",
                schema: "auth",
                table: "AuthMemberships",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthMemberships_UserId",
                schema: "auth",
                table: "AuthMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthOrganizationAudit_OrganizationId_TimestampUtc",
                schema: "auth",
                table: "AuthOrganizationAudit",
                columns: new[] { "OrganizationId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthOrganizations_Slug",
                schema: "auth",
                table: "AuthOrganizations",
                column: "Slug",
                unique: true);

            // GAD-T156: move the interim tables' rows into the library's tables, keeping every id (tenant-owned rows carry the
            // organization id), then drop the interim tables. Dates become UTC; a creator of Guid.Empty becomes NULL; a membership
            // whose account no longer exists is skipped (the library keeps a real foreign key to AuthUsers).
            migrationBuilder.Sql(@"
INSERT INTO [auth].[AuthOrganizations] ([Id], [Name], [Slug], [IsActive], [CreatedByUserId], [CreatedUtc])
SELECT o.[Id], o.[Name], o.[Slug], o.[IsActive],
       NULLIF(o.[CreatedByUserId], '00000000-0000-0000-0000-000000000000'),
       CAST(SWITCHOFFSET(o.[CreatedAt], 0) AS datetime2)
FROM [gadfly].[Organizations] o;

INSERT INTO [auth].[AuthMemberships] ([Id], [OrganizationId], [UserId], [Role], [IsActive], [CreatedUtc])
SELECT m.[Id], m.[OrganizationId], m.[UserId], m.[Role], m.[IsActive],
       CAST(SWITCHOFFSET(m.[CreatedAt], 0) AS datetime2)
FROM [gadfly].[OrganizationMembers] m
WHERE EXISTS (SELECT 1 FROM [auth].[AuthUsers] u WHERE u.[Id] = m.[UserId]);");

            migrationBuilder.DropTable(
                name: "OrganizationMembers",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "gadfly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "gadfly",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId",
                schema: "gadfly",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_UserId",
                schema: "gadfly",
                table: "OrganizationMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                schema: "gadfly",
                table: "Organizations",
                column: "Slug",
                unique: true);

            // Best-effort reverse copy (invitations and the organization audit have no interim home and are dropped).
            migrationBuilder.Sql(@"
INSERT INTO [gadfly].[Organizations] ([Id], [Name], [Slug], [IsActive], [CreatedByUserId], [CreatedAt])
SELECT o.[Id], o.[Name], o.[Slug], o.[IsActive],
       ISNULL(o.[CreatedByUserId], '00000000-0000-0000-0000-000000000000'),
       TODATETIMEOFFSET(o.[CreatedUtc], 0)
FROM [auth].[AuthOrganizations] o;

INSERT INTO [gadfly].[OrganizationMembers] ([Id], [OrganizationId], [UserId], [Role], [IsActive], [CreatedAt])
SELECT m.[Id], m.[OrganizationId], m.[UserId], LEFT(m.[Role], 20), m.[IsActive],
       TODATETIMEOFFSET(m.[CreatedUtc], 0)
FROM [auth].[AuthMemberships] m;");

            migrationBuilder.DropTable(
                name: "AuthInvitations",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthMemberships",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthOrganizationAudit",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthOrganizations",
                schema: "auth");
        }
    }
}
