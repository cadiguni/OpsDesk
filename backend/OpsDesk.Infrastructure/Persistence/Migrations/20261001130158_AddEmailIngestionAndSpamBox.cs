using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIngestionAndSpamBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    default_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    internal_domains_only = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_accounts_categories_default_category_id",
                        column: x => x.default_category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "spam_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pattern = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spam_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_spam_rules_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inbound_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    in_reply_to = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    references = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    headers_json = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    spam_score = table.Column<int>(type: "integer", nullable: false),
                    spam_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbound_emails_email_accounts_email_account_id",
                        column: x => x.email_account_id,
                        principalTable: "email_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbound_emails_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inbound_emails_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_accounts_address",
                table: "email_accounts",
                column: "address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_accounts_default_category_id",
                table: "email_accounts",
                column: "default_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_email_account_id_message_id",
                table: "inbound_emails",
                columns: new[] { "email_account_id", "message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_reviewed_by_id",
                table: "inbound_emails",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_status_received_at",
                table: "inbound_emails",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_ticket_id",
                table: "inbound_emails",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_spam_rules_created_by_id",
                table: "spam_rules",
                column: "created_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_emails");

            migrationBuilder.DropTable(
                name: "spam_rules");

            migrationBuilder.DropTable(
                name: "email_accounts");
        }
    }
}
