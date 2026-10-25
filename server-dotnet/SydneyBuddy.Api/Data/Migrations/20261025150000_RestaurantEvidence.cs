using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SydneyBuddy.Api.Data.Migrations;
[DbContext(typeof(BuddyDbContext))]
[Migration("20261025150000_RestaurantEvidence")]
public sealed class RestaurantEvidence:Migration {
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE restaurant_imports(id text PRIMARY KEY,venue_id text NOT NULL,data jsonb NOT NULL);
        CREATE INDEX "IX_restaurant_imports_venue_id" ON restaurant_imports(venue_id);
        CREATE TABLE restaurant_estimates(id text PRIMARY KEY,data jsonb NOT NULL);
        CREATE TABLE restaurant_venues(id text PRIMARY KEY,data jsonb NOT NULL);
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("DROP TABLE restaurant_estimates; DROP TABLE restaurant_imports; DROP TABLE restaurant_venues;");
}
