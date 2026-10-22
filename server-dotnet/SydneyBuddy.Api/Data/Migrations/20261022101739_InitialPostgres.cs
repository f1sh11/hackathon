using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SydneyBuddy.Api.Data.Migrations
{
    /// <summary>创建初始 PostgreSQL 表及版本约束；creates the initial JSONB schema without touching legacy SQLite data.</summary>
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memories",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trips", x => x.id);
                    table.CheckConstraint("ck_trips_revision", "revision > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memories");

            migrationBuilder.DropTable(
                name: "trips");
        }
    }
}
