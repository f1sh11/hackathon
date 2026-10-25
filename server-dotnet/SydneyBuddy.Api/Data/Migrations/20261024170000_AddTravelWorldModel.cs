using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SydneyBuddy.Api.Data.Migrations;

/// <summary>Adds the append-only, trip-scoped storage used by the travel World Model.</summary>
public partial class AddTravelWorldModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "travel_worlds",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                trip_id = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_travel_worlds", x => x.id);
                table.ForeignKey("FK_travel_worlds_trips_trip_id", x => x.trip_id, "trips", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "world_runs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                world_id = table.Column<string>(type: "text", nullable: false),
                request_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                trip_revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<string>(type: "text", nullable: false),
                completed_at = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_runs", x => x.id);
                table.ForeignKey("FK_world_runs_travel_worlds_world_id", x => x.world_id, "travel_worlds", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "world_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                world_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                idempotency_key = table.Column<string>(type: "text", nullable: false),
                actor = table.Column<string>(type: "text", nullable: false),
                occurred_at = table.Column<string>(type: "text", nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_events", x => x.id);
                table.ForeignKey("FK_world_events_travel_worlds_world_id", x => x.world_id, "travel_worlds", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_world_events_world_runs_run_id", x => x.run_id, "world_runs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "world_snapshots",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                world_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                sequence = table.Column<int>(type: "integer", nullable: false),
                occurred_at = table.Column<string>(type: "text", nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_world_snapshots", x => x.id);
                table.ForeignKey("FK_world_snapshots_travel_worlds_world_id", x => x.world_id, "travel_worlds", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_world_snapshots_world_runs_run_id", x => x.run_id, "world_runs", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_travel_worlds_trip_id", table: "travel_worlds", column: "trip_id", unique: true);
        migrationBuilder.CreateIndex(name: "IX_world_runs_world_id_request_id", table: "world_runs", columns: new[] { "world_id", "request_id" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_world_events_world_id_idempotency_key", table: "world_events", columns: new[] { "world_id", "idempotency_key" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_world_snapshots_run_id_sequence", table: "world_snapshots", columns: new[] { "run_id", "sequence" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "world_events");
        migrationBuilder.DropTable(name: "world_snapshots");
        migrationBuilder.DropTable(name: "world_runs");
        migrationBuilder.DropTable(name: "travel_worlds");
    }
}
