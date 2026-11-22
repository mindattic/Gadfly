using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gadfly.Server.Data.Migrations
{
    /// <summary>
    /// Separate reconciler and approver (GAD-T444): the organization's ApproverSeparation setting, 0 = Automatic for every
    /// existing organization (the approver must differ from the reconciler whenever someone else could approve). Written by
    /// hand with no target-model designer, so it carries no other change pending in the model.
    /// </summary>
    [DbContext(typeof(GadflyDbContext))]
    [Migration("20261122101000_ApproverSeparation")]
    public partial class ApproverSeparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApproverSeparation",
                schema: "gadfly",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);   // Automatic
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApproverSeparation", schema: "gadfly", table: "OrganizationSettings");
        }
    }
}
