using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PresenceTracker.Persistence;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202609300001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
                TargetPercent TEXT NOT NULL,
                WorkingDays INTEGER NOT NULL,
                StartWithWindows INTEGER NOT NULL,
                MinimizeToTray INTEGER NOT NULL,
                Theme INTEGER NOT NULL,
                BackupRetentionCount INTEGER NOT NULL,
                MinimumLogLevel TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS PresenceNetworks (
                Id INTEGER NOT NULL CONSTRAINT PK_PresenceNetworks PRIMARY KEY AUTOINCREMENT,
                Ssid TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                CountsAsPresence INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Holidays (
                Id INTEGER NOT NULL CONSTRAINT PK_Holidays PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Name TEXT NOT NULL,
                Scope INTEGER NOT NULL,
                Source INTEGER NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Classifications (
                Id INTEGER NOT NULL CONSTRAINT PK_Classifications PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Type INTEGER NOT NULL,
                Note TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AttendanceEvents (
                Id INTEGER NOT NULL CONSTRAINT PK_AttendanceEvents PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                OccurredAt TEXT NOT NULL,
                Source INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                NetworkEventId INTEGER NULL
            );
            CREATE TABLE IF NOT EXISTS NetworkEvents (
                Id INTEGER NOT NULL CONSTRAINT PK_NetworkEvents PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                OccurredAt TEXT NOT NULL,
                Ssid TEXT NULL,
                InterfaceId TEXT NOT NULL,
                InterfaceName TEXT NOT NULL,
                Type INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Plans (
                Id INTEGER NOT NULL CONSTRAINT PK_Plans PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS MonthlySnapshots (
                Id INTEGER NOT NULL CONSTRAINT PK_MonthlySnapshots PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                TargetPercent TEXT NOT NULL,
                WorkingDays INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_PresenceNetworks_Ssid ON PresenceNetworks(Ssid);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Holidays_Date_Scope_Name ON Holidays(Date, Scope, Name);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Classifications_Date ON Classifications(Date);
            CREATE INDEX IF NOT EXISTS IX_AttendanceEvents_Date_Source ON AttendanceEvents(Date, Source);
            CREATE INDEX IF NOT EXISTS IX_NetworkEvents_Date_OccurredAt ON NetworkEvents(Date, OccurredAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Plans_Date ON Plans(Date);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_MonthlySnapshots_Year_Month ON MonthlySnapshots(Year, Month);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS MonthlySnapshots; DROP TABLE IF EXISTS Plans; DROP TABLE IF EXISTS NetworkEvents; DROP TABLE IF EXISTS AttendanceEvents; DROP TABLE IF EXISTS Classifications; DROP TABLE IF EXISTS Holidays; DROP TABLE IF EXISTS PresenceNetworks; DROP TABLE IF EXISTS Settings;");
    }
}







