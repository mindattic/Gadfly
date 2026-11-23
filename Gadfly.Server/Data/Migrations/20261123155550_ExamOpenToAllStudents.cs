using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamOpenToAllStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpenToAllStudents",
                schema: "gadfly",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: true);   // GAD-T668: existing exams are open to all students
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenToAllStudents",
                schema: "gadfly",
                table: "Exams");
        }
    }
}
