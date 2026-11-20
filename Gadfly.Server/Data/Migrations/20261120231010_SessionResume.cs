using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LedgerJson",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "gadfly",
                table: "ExamSessions",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptJson",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LedgerJson",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "TranscriptJson",
                schema: "gadfly",
                table: "ExamSessions");
        }
    }
}
