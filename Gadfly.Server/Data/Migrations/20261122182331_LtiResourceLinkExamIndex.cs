using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class LtiResourceLinkExamIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_ExamId",
                schema: "gadfly",
                table: "LtiResourceLinks",
                column: "ExamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LtiResourceLinks_ExamId",
                schema: "gadfly",
                table: "LtiResourceLinks");
        }
    }
}
