using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Each AI objective grade records the grading prompt-set version it was made under
    /// (<see cref="AiObjectiveGrade.GradingPromptVersion"/>, GAD-T484), so a calibration from double marks rests only on grades
    /// made under the current prompts. Existing grades were all made under version 1. Written by hand with no target-model
    /// designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122130000_AiGradePromptVersion")]
    public partial class AiGradePromptVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GradingPromptVersion", schema: "gadfly", table: "AiObjectiveGrades",
                type: "int", nullable: false, defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "GradingPromptVersion", schema: "gadfly", table: "AiObjectiveGrades");
        }
    }
}
