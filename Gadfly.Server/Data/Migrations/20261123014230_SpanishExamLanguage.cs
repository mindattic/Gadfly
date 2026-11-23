using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Spanish joins the default exam languages (GAD-T611): new exams offer en,ko,es, and existing exams that still offered exactly en,ko
    /// gain es.
    /// </summary>
    public partial class SpanishExamLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Languages",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "en,ko,es",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "en,ko");

            // Existing exams (GAD-T611): one still offering exactly the old default, en,ko, was never narrowed by staff (Spanish could
            // not be chosen before), so it is offered in Spanish too. An exam whose staff changed the list keeps it as they left it.
            migrationBuilder.Sql("UPDATE [gadfly].[Exams] SET [Languages] = N'en,ko,es' WHERE [Languages] = N'en,ko';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [gadfly].[Exams] SET [Languages] = N'en,ko' WHERE [Languages] = N'en,ko,es';");

            migrationBuilder.AlterColumn<string>(
                name: "Languages",
                schema: "gadfly",
                table: "Exams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "en,ko",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "en,ko,es");
        }
    }
}
