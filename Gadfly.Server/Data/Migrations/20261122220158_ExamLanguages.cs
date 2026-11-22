using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<bool>(
                name: "NeedsInstructorReview",
                schema: "gadfly",
                table: "ExamSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryLanguage",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewFlaggedAt",
                schema: "gadfly",
                table: "ExamSessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Languages",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "en,ko");

            migrationBuilder.CreateTable(
                name: "ExamLocalizations",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ObjectiveTitlesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamLocalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamLocalizations_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "gadfly",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentLanguageProfiles",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrimaryLanguage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LanguagesSpoken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentLanguageProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamLocalizations_ExamId_Language",
                schema: "gadfly",
                table: "ExamLocalizations",
                columns: new[] { "ExamId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentLanguageProfiles_OrganizationId_UserId",
                schema: "gadfly",
                table: "StudentLanguageProfiles",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamLocalizations",
                schema: "gadfly");

            migrationBuilder.DropTable(
                name: "StudentLanguageProfiles",
                schema: "gadfly");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "NeedsInstructorReview",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "PrimaryLanguage",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ReviewFlaggedAt",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "Languages",
                schema: "gadfly",
                table: "Exams");
        }
    }
}
