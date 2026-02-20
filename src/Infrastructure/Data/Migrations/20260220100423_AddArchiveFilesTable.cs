using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveFilesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archive_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<short>(type: "smallint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    user_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_files", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_files_document_type_created_at",
                table: "archive_files",
                columns: new[] { "document_type", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_archive_files_file_name",
                table: "archive_files",
                column: "file_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_files");
        }
    }
}
