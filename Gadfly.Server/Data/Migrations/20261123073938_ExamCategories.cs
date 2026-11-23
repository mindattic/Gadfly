using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                schema: "gadfly",
                table: "Exams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExamCategories",
                schema: "gadfly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DisplayNamesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CategoryId",
                schema: "gadfly",
                table: "Exams",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamCategories_OrganizationId_Slug",
                schema: "gadfly",
                table: "ExamCategories",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCategories_OrganizationId_SortOrder",
                schema: "gadfly",
                table: "ExamCategories",
                columns: new[] { "OrganizationId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_ExamCategories_CategoryId",
                schema: "gadfly",
                table: "Exams",
                column: "CategoryId",
                principalSchema: "gadfly",
                principalTable: "ExamCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_ExamCategories_CategoryId",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropTable(
                name: "ExamCategories",
                schema: "gadfly");

            migrationBuilder.DropIndex(
                name: "IX_Exams_CategoryId",
                schema: "gadfly",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "gadfly",
                table: "Exams");
        }
    }
}
