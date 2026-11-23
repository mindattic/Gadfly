using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class GradingModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GAD-T703: exams that existed before grading modes keep grading combined with their stored weights;
            // new exams default to AiOnly in the entity.
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                schema: "gadfly",
                table: "GradingPolicies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Combined");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "gadfly",
                table: "GradingPolicies");
        }
    }
}
