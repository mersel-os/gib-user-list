using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // B-tree indexes on base tables are redundant:
            // - identifier already has UNIQUE constraint (implicit B-tree)
            // - title_lower on base tables is never queried (API reads from MVs)
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_e_invoice_gib_users_identifier;
                DROP INDEX IF EXISTS idx_e_invoice_gib_users_title_lower;
                DROP INDEX IF EXISTS idx_e_despatch_gib_users_identifier;
                DROP INDEX IF EXISTS idx_e_despatch_gib_users_title_lower;");

            // B-tree indexes on MVs don't support LIKE '%pattern%' (contains/substring search).
            // Replace with GIN trigram indexes that handle substring matching in O(1) via inverted index.
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_mv_e_invoice_gib_users_identifier;
                DROP INDEX IF EXISTS idx_mv_e_invoice_gib_users_title_lower;
                DROP INDEX IF EXISTS idx_mv_e_despatch_gib_users_identifier;
                DROP INDEX IF EXISTS idx_mv_e_despatch_gib_users_title_lower;");

            migrationBuilder.Sql(@"
                CREATE INDEX idx_mv_einv_title_trgm
                    ON mv_e_invoice_gib_users USING gin (title_lower gin_trgm_ops);
                CREATE INDEX idx_mv_einv_identifier_trgm
                    ON mv_e_invoice_gib_users USING gin (identifier gin_trgm_ops);

                CREATE INDEX idx_mv_edesp_title_trgm
                    ON mv_e_despatch_gib_users USING gin (title_lower gin_trgm_ops);
                CREATE INDEX idx_mv_edesp_identifier_trgm
                    ON mv_e_despatch_gib_users USING gin (identifier gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_mv_einv_title_trgm;
                DROP INDEX IF EXISTS idx_mv_einv_identifier_trgm;
                DROP INDEX IF EXISTS idx_mv_edesp_title_trgm;
                DROP INDEX IF EXISTS idx_mv_edesp_identifier_trgm;");

            // Restore original B-tree indexes
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_e_invoice_gib_users_identifier ON e_invoice_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_e_invoice_gib_users_title_lower ON e_invoice_gib_users (title_lower);
                CREATE INDEX IF NOT EXISTS idx_e_despatch_gib_users_identifier ON e_despatch_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_e_despatch_gib_users_title_lower ON e_despatch_gib_users (title_lower);

                CREATE INDEX IF NOT EXISTS idx_mv_e_invoice_gib_users_identifier ON mv_e_invoice_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_mv_e_invoice_gib_users_title_lower ON mv_e_invoice_gib_users (title_lower);
                CREATE INDEX IF NOT EXISTS idx_mv_e_despatch_gib_users_identifier ON mv_e_despatch_gib_users (identifier);
                CREATE INDEX IF NOT EXISTS idx_mv_e_despatch_gib_users_title_lower ON mv_e_despatch_gib_users (title_lower);");
        }
    }
}
