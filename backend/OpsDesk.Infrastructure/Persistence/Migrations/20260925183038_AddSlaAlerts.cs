using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sla_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deadline = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sla_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_sla_alerts_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sla_alerts_ticket_id_deadline_stage_due_at",
                table: "sla_alerts",
                columns: new[] { "ticket_id", "deadline", "stage", "due_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sla_alerts");
        }
    }
}
