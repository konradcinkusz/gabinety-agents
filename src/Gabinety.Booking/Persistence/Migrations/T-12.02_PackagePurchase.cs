using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackagePurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "practitioner_slug",
                table: "bookings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "units_total",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "units_used",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // A-02: several packages of one agent exist at the same time, so the no-double-booking rule (CONTRACTS 4.1) goes. PostgreSQL only: InMemory never had it.
            if (migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
                migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_no_overlap;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same definition as T-04.04. Fails when packages that overlap exist by then: remove them first.
            if (migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
                migrationBuilder.Sql("""
                    ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
                    EXCLUDE USING gist (
                        practitioner_id WITH =,
                        tsrange(start_utc AT TIME ZONE 'UTC', (end_utc AT TIME ZONE 'UTC') + interval '15 minutes') WITH &&)
                    WHERE (status IN ('PendingPayment','Confirmed','Disputed'));
                    """);

            migrationBuilder.DropColumn(
                name: "kind",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "practitioner_slug",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "units_total",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "units_used",
                table: "bookings");
        }
    }
}
