using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConceptDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyFactsJson",
                schema: "gadfly",
                table: "Concepts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MisconceptionsJson",
                schema: "gadfly",
                table: "Concepts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyFactsJson",
                schema: "gadfly",
                table: "Concepts");

            migrationBuilder.DropColumn(
                name: "MisconceptionsJson",
                schema: "gadfly",
                table: "Concepts");
        }
    }
}
