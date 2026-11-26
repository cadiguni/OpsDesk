using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "job_title_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_titles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_titles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_job_title_id",
                table: "users",
                column: "job_title_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_titles_name",
                table: "job_titles",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_users_job_titles_job_title_id",
                table: "users",
                column: "job_title_id",
                principalTable: "job_titles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Catálogo inicial. Fica na migration, e não no DatabaseSeeder, porque o deploy
            // de produção roda só --migrate: no seed, instalação existente nunca receberia os
            // cargos. Daqui em diante o catálogo é do administrador, e nenhuma etapa o recria.
            var createdAt = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

            migrationBuilder.InsertData(
                table: "job_titles",
                columns: ["id", "name", "is_active", "created_at", "updated_at"],
                values: new object[,]
                {
                    { new Guid("019ac1a0-0000-7000-8000-000000000001"), "Tech Lead", true, createdAt, createdAt },
                    { new Guid("019ac1a0-0000-7000-8000-000000000002"), "Analista", true, createdAt, createdAt },
                    { new Guid("019ac1a0-0000-7000-8000-000000000003"), "Assistente", true, createdAt, createdAt },
                    { new Guid("019ac1a0-0000-7000-8000-000000000004"), "Estagiário", true, createdAt, createdAt },
                    { new Guid("019ac1a0-0000-7000-8000-000000000005"), "Aprendiz", true, createdAt, createdAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_job_titles_job_title_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "job_titles");

            migrationBuilder.DropIndex(
                name: "ix_users_job_title_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "job_title_id",
                table: "users");
        }
    }
}
