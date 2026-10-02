using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202610020002_CustomBackupFolder")]
public sealed class CustomBackupFolderMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings ADD COLUMN BackupFolder TEXT NOT NULL DEFAULT ''; ");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE Settings DROP COLUMN BackupFolder;");
}
