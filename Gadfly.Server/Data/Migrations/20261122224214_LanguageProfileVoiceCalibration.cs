using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class LanguageProfileVoiceCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoiceCalibratedAt",
                schema: "gadfly",
                table: "StudentLanguageProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceCalibrationJson",
                schema: "gadfly",
                table: "StudentLanguageProfiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceCalibratedAt",
                schema: "gadfly",
                table: "StudentLanguageProfiles");

            migrationBuilder.DropColumn(
                name: "VoiceCalibrationJson",
                schema: "gadfly",
                table: "StudentLanguageProfiles");
        }
    }
}
