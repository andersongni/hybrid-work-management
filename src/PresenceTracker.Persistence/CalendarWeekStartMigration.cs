using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202610020001_CalendarWeekStart")]
public sealed class CalendarWeekStartMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings ADD COLUMN CalendarWeekStartsOn INTEGER NOT NULL DEFAULT 1;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings DROP COLUMN CalendarWeekStartsOn;");
}
