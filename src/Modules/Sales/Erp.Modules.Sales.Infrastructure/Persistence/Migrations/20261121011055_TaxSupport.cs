using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "sales",
                table: "SalesInvoiceLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxCode",
                schema: "sales",
                table: "SalesInvoiceLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "sales",
                table: "SalesInvoiceLines",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "sales",
                table: "CreditNoteLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "sales",
                table: "CreditNoteLines",
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
                schema: "sales",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxCode",
                schema: "sales",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "sales",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
