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
