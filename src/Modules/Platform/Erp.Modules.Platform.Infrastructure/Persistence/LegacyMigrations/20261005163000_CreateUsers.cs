using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

public partial class CreateUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Users",
            schema: "platform",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsBootstrapAdministrator = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "UserCredentials",
            schema: "platform",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserCredentials", x => x.UserId);
                table.ForeignKey("FK_UserCredentials_Users_UserId", x => x.UserId, "Users", "Id", "platform", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Users_UserName", "Users", "UserName", "platform", unique: true);
        migrationBuilder.CreateIndex("IX_Users_Email", "Users", "Email", "platform", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("UserCredentials", "platform");
        migrationBuilder.DropTable("Users", "platform");
    }
}
