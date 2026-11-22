using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// The typed-answer accommodation on an assignment (GAD-T260): an instructor or admin lets one student type their answers in the
    /// voice interview room. Written by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122021500_TypedAnswerAccommodation")]
    public partial class TypedAnswerAccommodation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TypedAnswerAccommodation",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);   // existing assignments: spoken answers only
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TypedAnswerAccommodation",
                schema: "gadfly",
                table: "ExamAssignments");
        }
    }
}
