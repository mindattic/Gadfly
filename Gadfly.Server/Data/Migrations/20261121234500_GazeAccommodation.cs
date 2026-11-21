using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// The eye-contact accommodation on an assignment (GAD-T261): no screen fade and no spoken request for eye contact in the
    /// voice interview room. Written by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261121234500_GazeAccommodation")]
    public partial class GazeAccommodation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GazeAccommodation",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);   // existing assignments: no accommodation
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GazeAccommodation",
                schema: "gadfly",
                table: "ExamAssignments");
        }
    }
}
