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
