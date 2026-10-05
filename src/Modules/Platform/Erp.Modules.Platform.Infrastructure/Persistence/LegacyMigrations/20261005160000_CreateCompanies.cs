using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

public partial class CreateCompanies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "platform");

        migrationBuilder.CreateTable(
            name: "Companies",
            schema: "platform",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Companies", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_Companies_Code",
            schema: "platform",
            table: "Companies",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "Companies", schema: "platform");
}
