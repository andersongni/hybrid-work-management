using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202610020003_BackupInterval")]
public sealed class BackupIntervalMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings ADD COLUMN BackupIntervalHours INTEGER NOT NULL DEFAULT 24;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings DROP COLUMN BackupIntervalHours;");
}
