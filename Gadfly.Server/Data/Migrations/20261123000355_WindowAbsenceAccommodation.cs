using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class WindowAbsenceAccommodation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WindowAbsenceFlagOff",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WindowAbsenceReviewAfterCount",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WindowAbsenceReviewAfterSeconds",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WindowAbsenceFlagOff",
                schema: "gadfly",
                table: "ExamAssignments");

            migrationBuilder.DropColumn(
                name: "WindowAbsenceReviewAfterCount",
                schema: "gadfly",
                table: "ExamAssignments");

            migrationBuilder.DropColumn(
                name: "WindowAbsenceReviewAfterSeconds",
                schema: "gadfly",
                table: "ExamAssignments");
        }
    }
}
