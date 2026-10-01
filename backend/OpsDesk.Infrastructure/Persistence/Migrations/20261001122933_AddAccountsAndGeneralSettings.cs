using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndGeneralSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_sensitive",
                table: "outbound_emails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    organization_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    allow_self_registration = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_email_domains = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_system_settings_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    new_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_audit", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_audit_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_audit_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id_used_at",
                table: "password_reset_tokens",
                columns: new[] { "user_id", "used_at" });

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_updated_by_id",
                table: "system_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_audit_changed_by_id",
                table: "user_audit",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_audit_user_id_created_at",
                table: "user_audit",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "user_audit");

            migrationBuilder.DropColumn(
                name: "is_sensitive",
                table: "outbound_emails");
        }
    }
}
