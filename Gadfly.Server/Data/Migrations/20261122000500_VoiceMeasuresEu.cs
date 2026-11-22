using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Voice measurements off by default in the EU/EEA (GAD-T259): the organization's EU flag and voice-measurements setting, and
    /// the student-lives-in-the-EU flag on an assignment. Written by hand with no target-model designer, so it carries no other
    /// change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122000500_VoiceMeasuresEu")]
    public partial class VoiceMeasuresEu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InEuropeanUnion",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);   // existing organizations: not marked as EU; behaviour unchanged

            migrationBuilder.AddColumn<string>(
                name: "VoiceMeasures",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "RegionDefault");

            migrationBuilder.AddColumn<bool>(
                name: "StudentInEuropeanUnion",
                schema: "gadfly",
                table: "ExamAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InEuropeanUnion", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "VoiceMeasures", schema: "gadfly", table: "OrganizationSettings");
            migrationBuilder.DropColumn(name: "StudentInEuropeanUnion", schema: "gadfly", table: "ExamAssignments");
        }
    }
}
