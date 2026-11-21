using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstructorOwnershipScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InstructorsSeeOnlyOwn",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);   // existing organizations start scoped (decision D18 default A); no ownership backfill: Course, SourceCollection and Exam already carry CreatedByUserId
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstructorsSeeOnlyOwn",
                schema: "gadfly",
                table: "OrganizationSettings");
        }
    }
}
