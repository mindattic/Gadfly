using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamsAlwaysSpoken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every exam is spoken on camera (GAD-T853); typed answers are a per-student accommodation, so no exam stays in Text mode.
            migrationBuilder.Sql("UPDATE [gadfly].[Exams] SET [Mode] = N'Voice' WHERE [Mode] = N'Text';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which exams were Text before is not kept; they stay Voice.
        }
    }
}
