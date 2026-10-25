using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SydneyBuddy.Api.Data.Migrations;

/// <summary>Adds the foreign-key indexes EF Core creates by convention for World Model projections.</summary>
public partial class AlignWorldModelIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_world_snapshots_world_id",
            table: "world_snapshots",
            column: "world_id");

        migrationBuilder.CreateIndex(
            name: "IX_world_events_run_id",
            table: "world_events",
            column: "run_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_world_snapshots_world_id",
            table: "world_snapshots");

        migrationBuilder.DropIndex(
            name: "IX_world_events_run_id",
            table: "world_events");
    }
}
