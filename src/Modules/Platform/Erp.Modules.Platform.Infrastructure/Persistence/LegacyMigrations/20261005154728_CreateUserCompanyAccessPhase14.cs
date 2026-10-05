using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

public partial class CreateUserCompanyAccessPhase14 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserCompanyAccess",
            schema: "platform",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserCompanyAccess", x => new { x.UserId, x.CompanyId });
                table.ForeignKey(
                    name: "FK_UserCompanyAccess_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "platform",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserCompanyAccess_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalSchema: "platform",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserCompanyAccess_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "platform",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserCompanyAccess_CompanyId",
            schema: "platform",
            table: "UserCompanyAccess",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_UserCompanyAccess_CreatedBy",
            schema: "platform",
            table: "UserCompanyAccess",
            column: "CreatedBy");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "UserCompanyAccess", schema: "platform");
}
