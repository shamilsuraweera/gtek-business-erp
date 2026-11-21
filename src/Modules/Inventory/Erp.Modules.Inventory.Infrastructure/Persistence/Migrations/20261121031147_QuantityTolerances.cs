using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuantityTolerances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuantityTolerancePercent",
                schema: "inventory",
                table: "Items",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantityTolerancePercent",
                schema: "inventory",
                table: "Items");
        }
    }
}
