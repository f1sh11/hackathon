using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SydneyBuddy.Api.Data.Migrations;
[DbContext(typeof(BuddyDbContext))]
[Migration("20261025020000_SocialImports")]
public sealed class SocialImports:Migration {
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE social_imports (id text PRIMARY KEY, data jsonb NOT NULL);
        CREATE VIEW travel_social_places AS
        SELECT place->>'id' AS id, place AS payload FROM social_imports CROSS JOIN LATERAL jsonb_array_elements(data->'places') AS place WHERE data->>'status'='published';
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("DROP VIEW travel_social_places; DROP TABLE social_imports;");
}
