using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Platform.Infrastructure.Persistence.Migrations;

public partial class CreateAuthorizationModel : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(name: "Roles", schema: "platform", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false),
            Code = t.Column<string>("character varying(100)", maxLength: 100, nullable: false),
            Name = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            Description = t.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            Status = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
            IsSystem = t.Column<bool>("boolean", nullable: false),
            CreatedAt = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
            ModifiedAt = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true)
        }, constraints: c => c.PrimaryKey("PK_Roles", x => x.Id));
        m.CreateTable(name: "Permissions", schema: "platform", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false),
            Code = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            Name = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            Module = t.Column<string>("character varying(100)", maxLength: 100, nullable: false),
            Description = t.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            CreatedAt = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: c => c.PrimaryKey("PK_Permissions", x => x.Id));
        m.CreateTable(name: "UserRoles", schema: "platform", columns: t => new
        {
            UserId = t.Column<Guid>("uuid", nullable: false),
            RoleId = t.Column<Guid>("uuid", nullable: false)
        }, constraints: c =>
        {
            c.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
            c.ForeignKey("FK_UserRoles_Users_UserId", x => x.UserId, "Users", "Id", "platform", ReferentialAction.Cascade);
            c.ForeignKey("FK_UserRoles_Roles_RoleId", x => x.RoleId, "Roles", "Id", "platform", ReferentialAction.Cascade);
        });
        m.CreateTable(name: "RolePermissions", schema: "platform", columns: t => new
        {
            RoleId = t.Column<Guid>("uuid", nullable: false),
            PermissionId = t.Column<Guid>("uuid", nullable: false)
        }, constraints: c =>
        {
            c.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
            c.ForeignKey("FK_RolePermissions_Roles_RoleId", x => x.RoleId, "Roles", "Id", "platform", ReferentialAction.Cascade);
            c.ForeignKey("FK_RolePermissions_Permissions_PermissionId", x => x.PermissionId, "Permissions", "Id", "platform", ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_Roles_Code", "Roles", "Code", "platform", unique: true);
        m.CreateIndex("IX_Permissions_Code", "Permissions", "Code", "platform", unique: true);
        m.CreateIndex("IX_UserRoles_RoleId", "UserRoles", "RoleId", "platform");
        m.CreateIndex("IX_RolePermissions_PermissionId", "RolePermissions", "PermissionId", "platform");
        m.Sql("""
            INSERT INTO platform."Permissions" ("Id", "Code", "Name", "Module", "Description", "CreatedAt")
            VALUES
              (gen_random_uuid(), 'platform.companies.read', 'Read companies', 'platform', 'View companies.', now()),
              (gen_random_uuid(), 'platform.companies.manage', 'Manage companies', 'platform', 'Create and manage companies.', now()),
              (gen_random_uuid(), 'platform.users.read', 'Read users', 'platform', 'View users.', now()),
              (gen_random_uuid(), 'platform.users.manage', 'Manage users', 'platform', 'Create and manage users and role assignments.', now()),
              (gen_random_uuid(), 'platform.roles.read', 'Read roles', 'platform', 'View roles.', now()),
              (gen_random_uuid(), 'platform.roles.manage', 'Manage roles', 'platform', 'Create and manage roles and assignments.', now()),
              (gen_random_uuid(), 'platform.permissions.read', 'Read permissions', 'platform', 'View the permission catalogue.', now()),
              (gen_random_uuid(), 'finance.accounts.read', 'Read finance accounts', 'finance', 'View finance accounts.', now())
            ON CONFLICT ("Code") DO NOTHING;
            INSERT INTO platform."Roles" ("Id", "Code", "Name", "Description", "Status", "IsSystem", "CreatedAt")
            VALUES (gen_random_uuid(), 'SYSTEM_ADMIN', 'System Administrator', 'Initial installation administrator.', 'Active', true, now())
            ON CONFLICT ("Code") DO NOTHING;
            INSERT INTO platform."RolePermissions" ("RoleId", "PermissionId")
            SELECT r."Id", p."Id" FROM platform."Roles" r CROSS JOIN platform."Permissions" p
            WHERE r."Code" = 'SYSTEM_ADMIN'
            ON CONFLICT DO NOTHING;
            INSERT INTO platform."UserRoles" ("UserId", "RoleId")
            SELECT u."Id", r."Id" FROM platform."Users" u CROSS JOIN platform."Roles" r
            WHERE u."IsBootstrapAdministrator" = true AND r."Code" = 'SYSTEM_ADMIN'
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("RolePermissions", "platform");
        m.DropTable("UserRoles", "platform");
        m.DropTable("Permissions", "platform");
        m.DropTable("Roles", "platform");
    }
}
