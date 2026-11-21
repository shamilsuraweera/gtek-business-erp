using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRetentionCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationRetentionSettings",
                schema: "platform",
                table: "NotificationRetentionSettings");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "platform",
                table: "NotificationRetentionSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationRetentionSettings",
                schema: "platform",
                table: "NotificationRetentionSettings",
                columns: new[] { "CompanyId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without categories there is one row per company, so category rows cannot be kept.
            migrationBuilder.Sql("DELETE FROM platform.\"NotificationRetentionSettings\" WHERE \"Category\" <> '';");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationRetentionSettings",
                schema: "platform",
                table: "NotificationRetentionSettings");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "platform",
                table: "NotificationRetentionSettings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationRetentionSettings",
                schema: "platform",
                table: "NotificationRetentionSettings",
                column: "CompanyId");
        }
    }
}
