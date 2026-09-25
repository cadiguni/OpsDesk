using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sender_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    protected_client_secret = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    client_secret_updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    client_secret_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    portal_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_settings_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbound_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    to_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_outbound_emails_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "settings_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changed_fields = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings_audit", x => x.id);
                    table.ForeignKey(
                        name: "fk_settings_audit_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_settings_updated_by_id",
                table: "email_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_actor_id",
                table: "notifications",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_ticket_id",
                table: "notifications",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_read_at",
                table: "notifications",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_emails_status_next_attempt_at",
                table: "outbound_emails",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_emails_ticket_id",
                table: "outbound_emails",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_settings_audit_area_created_at",
                table: "settings_audit",
                columns: new[] { "area", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_settings_audit_changed_by_id",
                table: "settings_audit",
                column: "changed_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_settings");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "outbound_emails");

            migrationBuilder.DropTable(
                name: "settings_audit");
        }
    }
}
