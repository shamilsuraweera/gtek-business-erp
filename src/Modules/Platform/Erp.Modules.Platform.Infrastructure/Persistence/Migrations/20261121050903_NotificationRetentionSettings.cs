using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRetentionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationRetentionSettings",
                schema: "platform",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadDays = table.Column<int>(type: "integer", nullable: true),
                    UnreadDays = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRetentionSettings", x => x.CompanyId);
                    table.CheckConstraint("CK_NotificationRetentionSettings_ReadDays", "\"ReadDays\" IS NULL OR (\"ReadDays\" BETWEEN 1 AND 3650)");
                    table.CheckConstraint("CK_NotificationRetentionSettings_UnreadDays", "\"UnreadDays\" IS NULL OR (\"UnreadDays\" BETWEEN 0 AND 3650)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationRetentionSettings",
                schema: "platform");
        }
    }
}
