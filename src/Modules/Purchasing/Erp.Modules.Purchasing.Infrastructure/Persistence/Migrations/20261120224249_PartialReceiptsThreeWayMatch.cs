using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartialReceiptsThreeWayMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                schema: "purchasing",
                table: "VendorBills");




            migrationBuilder.AddColumn<bool>(
                name: "VarianceOverridden",
                schema: "purchasing",
                table: "VendorBills",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderUnitCost",
                schema: "purchasing",
                table: "VendorBillLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityBilled",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityReceived",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLines",
                schema: "purchasing",
                columns: table => new
                {
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLines", x => new { x.GoodsReceiptId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalSchema: "purchasing",
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Data backfill (old model: whole-order receipt and bill, tracked on the purchase order itself) ----
            // Receipts and bills of existing orders were always for the full quantity.
            migrationBuilder.Sql("""
                UPDATE purchasing."PurchaseOrderLines" l
                SET "QuantityReceived" = l."Quantity"
                FROM purchasing."PurchaseOrders" o
                WHERE o."Id" = l."PurchaseOrderId" AND o."Status" IN ('Received', 'Billed');

                UPDATE purchasing."PurchaseOrderLines" l
                SET "QuantityBilled" = l."Quantity"
                FROM purchasing."PurchaseOrders" o
                WHERE o."Id" = l."PurchaseOrderId" AND o."Status" = 'Billed';

                UPDATE purchasing."VendorBillLines" SET "OrderUnitCost" = "UnitCost";
                """);

            // One goods receipt per already-received order. The journal id was never stored (only its number), and a
            // migration must not read the Finance module's tables (ADR 0002), so legacy receipts carry an empty journal id.
            migrationBuilder.Sql("""
                INSERT INTO purchasing."GoodsReceipts"
                    ("Id", "CompanyId", "Number", "PurchaseOrderId", "VendorId", "ReceiptDate", "Reference", "JournalId", "JournalNumber", "CreatedAt")
                SELECT gen_random_uuid(), o."CompanyId", 'GRN-' || o."Number", o."Id", o."VendorId",
                       COALESCE(o."ReceivedDate", o."OrderDate"), '', '00000000-0000-0000-0000-000000000000'::uuid,
                       COALESCE(o."ReceiptJournalNumber", ''), o."CreatedAt"
                FROM purchasing."PurchaseOrders" o
                WHERE o."Status" IN ('Received', 'Billed');

                INSERT INTO purchasing."GoodsReceiptLines" ("GoodsReceiptId", "ItemId", "Sku", "Description", "Quantity", "UnitCost")
                SELECT r."Id", l."ItemId", l."Sku", l."Description", l."Quantity", l."UnitCost"
                FROM purchasing."PurchaseOrderLines" l
                JOIN purchasing."GoodsReceipts" r ON r."PurchaseOrderId" = l."PurchaseOrderId";
                """);

            // ---- Old columns are no longer needed ----
            migrationBuilder.DropColumn(name: "BillId", schema: "purchasing", table: "PurchaseOrders");
            migrationBuilder.DropColumn(name: "ReceiptJournalNumber", schema: "purchasing", table: "PurchaseOrders");
            migrationBuilder.DropColumn(name: "ReceivedDate", schema: "purchasing", table: "PurchaseOrders");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CompanyId_VendorId_VendorReference",
                schema: "purchasing",
                table: "VendorBills",
                columns: new[] { "CompanyId", "VendorId", "VendorReference" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                schema: "purchasing",
                table: "VendorBills",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CompanyId_Number",
                schema: "purchasing",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_PurchaseOrderId",
                schema: "purchasing",
                table: "GoodsReceipts",
                column: "PurchaseOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoodsReceiptLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "GoodsReceipts",
                schema: "purchasing");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_CompanyId_VendorId_VendorReference",
                schema: "purchasing",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                schema: "purchasing",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "VarianceOverridden",
                schema: "purchasing",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "OrderUnitCost",
                schema: "purchasing",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "QuantityBilled",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "QuantityReceived",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddColumn<Guid>(
                name: "BillId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptJournalNumber",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReceivedDate",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                schema: "purchasing",
                table: "VendorBills",
                column: "PurchaseOrderId",
                unique: true);
        }
    }
}
