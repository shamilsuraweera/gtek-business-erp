using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PriceTolerances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceTolerancePercent",
                schema: "purchasing",
                table: "Vendors",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TolerancePercent",
                schema: "purchasing",
                table: "VendorBillLines",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 2m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceTolerancePercent",
                schema: "purchasing",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "TolerancePercent",
                schema: "purchasing",
                table: "VendorBillLines");
        }
    }
}
