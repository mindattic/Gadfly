using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>Learning Mode's fixed, instructor-written hint ladder per objective (GAD-T271).</summary>
    public partial class HintLadder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HintLadderJson",
                schema: "gadfly",
                table: "LearningObjectives",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");   // existing objectives use the engine's default ladder
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HintLadderJson",
                schema: "gadfly",
                table: "LearningObjectives");
        }
    }
}
