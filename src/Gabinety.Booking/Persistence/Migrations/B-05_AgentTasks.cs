using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B05_AgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    practitioner_slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    input_text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    output_text = table.Column<string>(type: "text", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_tasks_booking_id_created_at",
                table: "agent_tasks",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_tasks_status_created_at",
                table: "agent_tasks",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_tasks");
        }
    }
}
