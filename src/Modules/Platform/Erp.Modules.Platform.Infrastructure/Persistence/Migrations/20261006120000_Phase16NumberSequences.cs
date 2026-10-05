using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

public partial class Phase16NumberSequences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NumberSequences",
            schema: "platform",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Prefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Suffix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                NextValue = table.Column<long>(type: "bigint", nullable: false),
                Padding = table.Column<int>(type: "integer", nullable: false),
                Increment = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NumberSequences", x => x.Id);
                table.ForeignKey(
                    name: "FK_NumberSequences_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    principalSchema: "platform",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_NumberSequences_CompanyId", "NumberSequences", "CompanyId", "platform");
        migrationBuilder.CreateIndex("IX_NumberSequences_CompanyId_Code", "NumberSequences", new[] { "CompanyId", "Code" }, "platform", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("NumberSequences", "platform");
}
