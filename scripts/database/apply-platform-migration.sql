-- LEGACY BOOTSTRAP ONLY:
-- EF Core migrations are authoritative for new databases and future schema
-- evolution. This idempotent script is retained for SQL-bootstrap databases
-- that predate the EF baseline. Validate such a database with
-- adopt-platform-bootstrap.ps1 before recording EF migration history.
CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE IF NOT EXISTS platform."Companies" (
    "Id" uuid NOT NULL,
    "Code" character varying(20) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModifiedAt" timestamp with time zone NULL,
    CONSTRAINT "PK_Companies" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Companies_Code"
    ON platform."Companies" ("Code");

CREATE TABLE IF NOT EXISTS platform."Users" (
    "Id" uuid NOT NULL,
    "UserName" character varying(100) NOT NULL,
    "Email" character varying(320) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModifiedAt" timestamp with time zone NULL,
    "IsBootstrapAdministrator" boolean NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_UserName"
    ON platform."Users" ("UserName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email"
    ON platform."Users" ("Email");

CREATE TABLE IF NOT EXISTS platform."UserCredentials" (
    "UserId" uuid NOT NULL,
    "PasswordHash" character varying(500) NOT NULL,
    CONSTRAINT "PK_UserCredentials" PRIMARY KEY ("UserId"),
    CONSTRAINT "FK_UserCredentials_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES platform."Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS platform."Roles" (
    "Id" uuid NOT NULL,
    "Code" character varying(100) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(1000) NULL,
    "Status" character varying(20) NOT NULL,
    "IsSystem" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModifiedAt" timestamp with time zone NULL,
    CONSTRAINT "PK_Roles" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Roles_Code" ON platform."Roles" ("Code");

CREATE TABLE IF NOT EXISTS platform."Permissions" (
    "Id" uuid NOT NULL,
    "Code" character varying(200) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Module" character varying(100) NOT NULL,
    "Description" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Permissions" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Permissions_Code" ON platform."Permissions" ("Code");

CREATE TABLE IF NOT EXISTS platform."UserRoles" (
    "UserId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    CONSTRAINT "PK_UserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES platform."Users" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_UserRoles_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES platform."Roles" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_UserRoles_RoleId" ON platform."UserRoles" ("RoleId");

CREATE TABLE IF NOT EXISTS platform."RolePermissions" (
    "RoleId" uuid NOT NULL,
    "PermissionId" uuid NOT NULL,
    CONSTRAINT "PK_RolePermissions" PRIMARY KEY ("RoleId", "PermissionId"),
    CONSTRAINT "FK_RolePermissions_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES platform."Roles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_RolePermissions_Permissions_PermissionId" FOREIGN KEY ("PermissionId") REFERENCES platform."Permissions" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_RolePermissions_PermissionId" ON platform."RolePermissions" ("PermissionId");

CREATE TABLE IF NOT EXISTS platform."UserCompanyAccess" (
    "UserId" uuid NOT NULL,
    "CompanyId" uuid NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid NOT NULL,
    "ModifiedAt" timestamp with time zone NULL,
    "ModifiedBy" uuid NULL,
    CONSTRAINT "PK_UserCompanyAccess" PRIMARY KEY ("UserId", "CompanyId"),
    CONSTRAINT "FK_UserCompanyAccess_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES platform."Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserCompanyAccess_Companies_CompanyId"
        FOREIGN KEY ("CompanyId") REFERENCES platform."Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserCompanyAccess_Users_CreatedBy"
        FOREIGN KEY ("CreatedBy") REFERENCES platform."Users" ("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_UserCompanyAccess_CompanyId"
    ON platform."UserCompanyAccess" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_UserCompanyAccess_CreatedBy"
    ON platform."UserCompanyAccess" ("CreatedBy");

CREATE TABLE IF NOT EXISTS platform."AuditEntries" (
    "Id" uuid NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "ActorUserId" uuid NULL,
    "ActorUserName" character varying(100) NULL,
    "CompanyId" uuid NULL,
    "Category" character varying(100) NOT NULL,
    "Action" character varying(200) NOT NULL,
    "EntityType" character varying(100) NOT NULL,
    "EntityId" character varying(200) NULL,
    "EntityDisplay" character varying(500) NULL,
    "Outcome" character varying(20) NOT NULL,
    "CorrelationId" character varying(200) NULL,
    "IpAddress" character varying(100) NULL,
    "MetadataJson" character varying(4000) NULL,
    CONSTRAINT "PK_AuditEntries" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_AuditEntries_OccurredAt"
    ON platform."AuditEntries" ("OccurredAt");
CREATE INDEX IF NOT EXISTS "IX_AuditEntries_ActorUserId_OccurredAt"
    ON platform."AuditEntries" ("ActorUserId", "OccurredAt");
CREATE INDEX IF NOT EXISTS "IX_AuditEntries_CompanyId_OccurredAt"
    ON platform."AuditEntries" ("CompanyId", "OccurredAt");
CREATE INDEX IF NOT EXISTS "IX_AuditEntries_Action_OccurredAt"
    ON platform."AuditEntries" ("Action", "OccurredAt");
CREATE INDEX IF NOT EXISTS "IX_AuditEntries_EntityType_EntityId_OccurredAt"
    ON platform."AuditEntries" ("EntityType", "EntityId", "OccurredAt");

CREATE TABLE IF NOT EXISTS platform."NumberSequences" (
    "Id" uuid NOT NULL,
    "CompanyId" uuid NOT NULL,
    "Code" character varying(100) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Prefix" character varying(50) NOT NULL,
    "Suffix" character varying(50) NULL,
    "NextValue" bigint NOT NULL,
    "Padding" integer NOT NULL,
    "Increment" bigint NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid NULL,
    "ModifiedAt" timestamp with time zone NULL,
    "ModifiedBy" uuid NULL,
    CONSTRAINT "PK_NumberSequences" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_NumberSequences_Companies_CompanyId"
        FOREIGN KEY ("CompanyId") REFERENCES platform."Companies" ("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_NumberSequences_CompanyId_Code"
    ON platform."NumberSequences" ("CompanyId", "Code");
CREATE INDEX IF NOT EXISTS "IX_NumberSequences_CompanyId"
    ON platform."NumberSequences" ("CompanyId");

INSERT INTO platform."Permissions" ("Id", "Code", "Name", "Module", "Description", "CreatedAt")
VALUES
  (gen_random_uuid(), 'platform.companies.read', 'Read companies', 'platform', 'View companies.', now()),
  (gen_random_uuid(), 'platform.companies.manage', 'Manage companies', 'platform', 'Create and manage companies.', now()),
  (gen_random_uuid(), 'platform.users.read', 'Read users', 'platform', 'View users.', now()),
  (gen_random_uuid(), 'platform.users.manage', 'Manage users', 'platform', 'Create and manage users and role assignments.', now()),
  (gen_random_uuid(), 'platform.roles.read', 'Read roles', 'platform', 'View roles.', now()),
  (gen_random_uuid(), 'platform.roles.manage', 'Manage roles', 'platform', 'Create and manage roles and assignments.', now()),
  (gen_random_uuid(), 'platform.permissions.read', 'Read permissions', 'platform', 'View the permission catalogue.', now()),
  (gen_random_uuid(), 'platform.company-access.read', 'Read company access', 'platform', 'View user and company access relationships.', now()),
  (gen_random_uuid(), 'platform.company-access.manage', 'Manage company access', 'platform', 'Grant, revoke, and restore user access to companies.', now()),
  (gen_random_uuid(), 'platform.audit.read', 'Read audit history', 'platform', 'View the append-only ERP audit history.', now()),
  (gen_random_uuid(), 'platform.number-sequences.read', 'Read number sequences', 'platform', 'View and issue company number sequences.', now()),
  (gen_random_uuid(), 'platform.number-sequences.manage', 'Manage number sequences', 'platform', 'Create and configure company number sequences.', now()),
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
