using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionClosingReflection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosingReflectionAt",
                schema: "gadfly",
                table: "ExamSessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingReflectionMode",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingReflectionState",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingReflectionText",
                schema: "gadfly",
                table: "ExamSessions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingReflectionAt",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ClosingReflectionMode",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ClosingReflectionState",
                schema: "gadfly",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ClosingReflectionText",
                schema: "gadfly",
                table: "ExamSessions");
        }
    }
}
