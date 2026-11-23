using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class VoiceTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoiceTier",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceTier",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceTier",
                schema: "gadfly",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "VoiceTier",
                schema: "gadfly",
                table: "Exams");
        }
    }
}
