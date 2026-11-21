using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuantityTolerances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuantityTolerancePercent",
                schema: "purchasing",
                table: "Vendors",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityTolerancePercent",
                schema: "purchasing",
                table: "GoodsReceiptLines",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantityTolerancePercent",
                schema: "purchasing",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QuantityTolerancePercent",
                schema: "purchasing",
                table: "GoodsReceiptLines");
        }
    }
}
