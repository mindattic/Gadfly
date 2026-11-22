using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Anonymous human marking (GAD-T439): the exam's grading policy can show graders a session code instead of the
    /// student's name until the human score is submitted. Off (0) for every existing policy. Written by hand with no
    /// target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122103000_AnonymousMarking")]
    public partial class AnonymousMarking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnonymousMarking",
                schema: "gadfly",
                table: "GradingPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AnonymousMarking", schema: "gadfly", table: "GradingPolicies");
        }
    }
}
