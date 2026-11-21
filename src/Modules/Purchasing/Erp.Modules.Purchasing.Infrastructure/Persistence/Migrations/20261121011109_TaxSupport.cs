using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "purchasing",
                table: "VendorBillLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxCode",
                schema: "purchasing",
                table: "VendorBillLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "purchasing",
                table: "VendorBillLines",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "purchasing",
                table: "DebitNoteLines",
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
                name: "TaxAmount",
                schema: "purchasing",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "TaxCode",
                schema: "purchasing",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "purchasing",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "purchasing",
                table: "DebitNoteLines");
        }
    }
}
