using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gib_user_changelog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<short>(type: "smallint", nullable: false),
                    identifier = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    change_type = table.Column<short>(type: "smallint", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    account_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_creation_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    aliases_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gib_user_changelog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_metadata",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    e_invoice_user_count = table.Column<int>(type: "integer", nullable: false),
                    e_despatch_user_count = table.Column<int>(type: "integer", nullable: false),
                    last_sync_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    last_sync_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_failure_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_metadata", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gib_user_changelog_document_type_changed_at",
                table: "gib_user_changelog",
                columns: new[] { "document_type", "changed_at" });

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS gib_user_temp_pk (
                    identifier      VARCHAR(11) NOT NULL,
                    account_type    VARCHAR(50),
                    first_creation_time TIMESTAMP NOT NULL,
                    title           VARCHAR(500) NOT NULL,
                    title_lower     VARCHAR(500) NOT NULL,
                    type            VARCHAR(50),
                    documents       JSONB
                );

                CREATE TABLE IF NOT EXISTS gib_user_temp_gb (
                    identifier      VARCHAR(11) NOT NULL,
                    account_type    VARCHAR(50),
                    first_creation_time TIMESTAMP NOT NULL,
                    title           VARCHAR(500) NOT NULL,
                    title_lower     VARCHAR(500) NOT NULL,
                    type            VARCHAR(50),
                    documents       JSONB
                );");

            // Main tables for merged data (EF reads from materialized views, but base tables needed here)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS e_invoice_gib_users (
                    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    identifier      VARCHAR(11) NOT NULL UNIQUE,
                    account_type    VARCHAR(50),
                    first_creation_time TIMESTAMP NOT NULL,
                    title           VARCHAR(500) NOT NULL,
                    title_lower     VARCHAR(500) NOT NULL,
                    type            VARCHAR(50),
                    aliases_json    JSONB,
                    content_hash    CHAR(32)
                );

                CREATE TABLE IF NOT EXISTS e_despatch_gib_users (
                    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    identifier      VARCHAR(11) NOT NULL UNIQUE,
                    account_type    VARCHAR(50),
                    first_creation_time TIMESTAMP NOT NULL,
                    title           VARCHAR(500) NOT NULL,
                    title_lower     VARCHAR(500) NOT NULL,
                    type            VARCHAR(50),
                    aliases_json    JSONB,
                    content_hash    CHAR(32)
                );");

            // Indexes on main tables
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_e_invoice_gib_users_identifier ON e_invoice_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_e_invoice_gib_users_title_lower ON e_invoice_gib_users (title_lower);
                CREATE INDEX IF NOT EXISTS idx_e_despatch_gib_users_identifier ON e_despatch_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_e_despatch_gib_users_title_lower ON e_despatch_gib_users (title_lower);");

            // Materialized views for zero-downtime reads
            migrationBuilder.Sql(@"
                CREATE MATERIALIZED VIEW IF NOT EXISTS mv_e_invoice_gib_users AS
                    SELECT * FROM e_invoice_gib_users;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_mv_e_invoice_gib_users_id ON mv_e_invoice_gib_users (id);
                CREATE INDEX IF NOT EXISTS idx_mv_e_invoice_gib_users_identifier ON mv_e_invoice_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_mv_e_invoice_gib_users_title_lower ON mv_e_invoice_gib_users (title_lower);

                CREATE MATERIALIZED VIEW IF NOT EXISTS mv_e_despatch_gib_users AS
                    SELECT * FROM e_despatch_gib_users;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_mv_e_despatch_gib_users_id ON mv_e_despatch_gib_users (id);
                CREATE INDEX IF NOT EXISTS idx_mv_e_despatch_gib_users_identifier ON mv_e_despatch_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_mv_e_despatch_gib_users_title_lower ON mv_e_despatch_gib_users (title_lower);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop materialized views first (they depend on tables)
            migrationBuilder.Sql(@"
                DROP MATERIALIZED VIEW IF EXISTS mv_e_invoice_gib_users;
                DROP MATERIALIZED VIEW IF EXISTS mv_e_despatch_gib_users;

                DROP TABLE IF EXISTS e_invoice_gib_users;
                DROP TABLE IF EXISTS e_despatch_gib_users;

                DROP TABLE IF EXISTS gib_user_temp_pk;
                DROP TABLE IF EXISTS gib_user_temp_gb;
            ");

            migrationBuilder.DropTable(
                name: "gib_user_changelog");

            migrationBuilder.DropTable(
                name: "sync_metadata");
        }
    }
}
