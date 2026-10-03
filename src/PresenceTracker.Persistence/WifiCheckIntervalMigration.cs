using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202610020004_WifiCheckInterval")]
public sealed class WifiCheckIntervalMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings ADD COLUMN WifiCheckIntervalMinutes INTEGER NOT NULL DEFAULT 10;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings DROP COLUMN WifiCheckIntervalMinutes;");
}
