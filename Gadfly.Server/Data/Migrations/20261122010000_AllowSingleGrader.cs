using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Separation of duties in grading (GAD-T285): the organization's "allow a single grader" override, off by default, so the
    /// person who entered a grade's human score cannot reconcile or approve it unless a small organization turns it on. Written
    /// by hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122010000_AllowSingleGrader")]
    public partial class AllowSingleGrader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSingleGrader",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);   // existing organizations: separation of duties enforced
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowSingleGrader", schema: "gadfly", table: "OrganizationSettings");
        }
    }
}
