using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PresenceTracker.Application;
using PresenceTracker.Infrastructure;
using PresenceTracker.Persistence;

namespace PresenceTracker.Tests;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public async Task CreateBackupReturnsNullWhenDatabaseIsMissing()
    {
        await using var fixture = BackupTestFixture.CreateEmpty();
        var service = fixture.CreateService();

        var path = await service.CreateBackupAsync();

        Assert.Null(path);
    }

    [Fact]
    public async Task ManualBackupCreatesValidPtbackupFile()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync();
        var service = fixture.CreateService();

        var path = await service.CreateBackupAsync();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.EndsWith(".ptbackup", path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", await IntegrityCheckAsync(path!));
        Assert.Equal(8, await RequiredTableCountAsync(path!));
    }

    [Fact]
    public async Task AutomaticBackupIsSkippedWhenIntervalHasNotElapsed()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync(intervalHours: 24);
        var stale = Path.Combine(fixture.Paths.DefaultBackupDirectory, "presence-recent.ptbackup");
        await File.WriteAllBytesAsync(stale, [1, 2, 3]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
        var service = fixture.CreateService();

        var path = await service.CreateBackupIfDueAsync();

        Assert.Null(path);
    }

    [Fact]
    public async Task AutomaticBackupRunsWhenIntervalHasElapsed()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync(intervalHours: 1);
        var stale = Path.Combine(fixture.Paths.DefaultBackupDirectory, "presence-old.ptbackup");
        await File.WriteAllBytesAsync(stale, [1, 2, 3]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-3));
        var service = fixture.CreateService();

        var path = await service.CreateBackupIfDueAsync();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal("ok", await IntegrityCheckAsync(path!));
    }

    [Fact]
    public async Task RetentionDeletesOlderBackups()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync(retention: 2);
        var service = fixture.CreateService();
        var created = new List<string>();

        for (var index = 0; index < 3; index++)
        {
            var path = await service.CreateBackupAsync();
            Assert.NotNull(path);
            created.Add(path!);
            File.SetCreationTimeUtc(path!, DateTime.UtcNow.AddMinutes(index));
            await Task.Delay(5);
        }

        var remaining = Directory.GetFiles(fixture.Paths.DefaultBackupDirectory, "presence-*")
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(2, remaining.Length);
        Assert.DoesNotContain(Path.GetFileName(created[0]), remaining);
        Assert.Contains(Path.GetFileName(created[1]), remaining);
        Assert.Contains(Path.GetFileName(created[2]), remaining);
    }

    [Fact]
    public async Task RestoreReplacesDatabaseAndCreatesSafetyBackup()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync();
        var service = fixture.CreateService();
        var originalTarget = await fixture.ReadTargetPercentAsync();
        var backupPath = await service.CreateBackupAsync();
        Assert.NotNull(backupPath);

        await fixture.WriteTargetPercentAsync(99m);
        Assert.Equal(99m, await fixture.ReadTargetPercentAsync());

        var safety = await service.RestoreBackupAsync(backupPath!);

        Assert.NotNull(safety);
        Assert.True(File.Exists(safety));
        Assert.Equal(originalTarget, await fixture.ReadTargetPercentAsync());
    }

    [Fact]
    public async Task RestoreRejectsCorruptedFileAndKeepsCurrentDatabase()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync();
        var service = fixture.CreateService();
        await fixture.WriteTargetPercentAsync(55m);
        var corrupted = Path.Combine(fixture.Root, "broken.ptbackup");
        await File.WriteAllTextAsync(corrupted, "not-a-sqlite-database");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreBackupAsync(corrupted));
        Assert.Equal(55m, await fixture.ReadTargetPercentAsync());
    }

    [Fact]
    public async Task RestoreRejectsIncompleteSchemaAndKeepsCurrentDatabase()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync();
        var service = fixture.CreateService();
        await fixture.WriteTargetPercentAsync(60m);
        var incomplete = Path.Combine(fixture.Root, "incomplete.ptbackup");
        await CreateIncompleteSqliteAsync(incomplete);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreBackupAsync(incomplete));
        Assert.Equal(60m, await fixture.ReadTargetPercentAsync());
    }

    [Fact]
    public async Task RestoreRejectsLiveDatabasePath()
    {
        await using var fixture = await BackupTestFixture.CreateInitializedAsync();
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RestoreBackupAsync(fixture.Paths.Database));
    }

    private static async Task<string?> IntegrityCheckAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<int> RequiredTableCountAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN (
              'Settings', 'PresenceNetworks', 'AttendanceEvents', 'NetworkEvents',
              'Plans', 'Holidays', 'Classifications', 'MonthlySnapshots')
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task CreateIncompleteSqliteAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Settings (Id INTEGER PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();
        SqliteConnection.ClearAllPools();
    }

    private sealed class TestBackupPaths(string root) : IBackupPaths
    {
        public string Root { get; } = root;
        public string Database => Path.Combine(Root, "presence.db");
        public string DefaultBackupDirectory => Path.Combine(Root, "backups");

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(DefaultBackupDirectory);
        }
    }

    private sealed class BackupTestFixture : IAsyncDisposable
    {
        private BackupTestFixture(TestBackupPaths paths)
        {
            Paths = paths;
            Root = paths.Root;
        }

        public TestBackupPaths Paths { get; }
        public string Root { get; }

        public static BackupTestFixture CreateEmpty()
        {
            var root = Path.Combine(Path.GetTempPath(), "PresenceTrackerBackupTests", Guid.NewGuid().ToString("N"));
            var paths = new TestBackupPaths(root);
            paths.EnsureDirectories();
            return new BackupTestFixture(paths);
        }

        public static async Task<BackupTestFixture> CreateInitializedAsync(int retention = 30, int intervalHours = 24)
        {
            var fixture = CreateEmpty();
            var options = new DbContextOptionsBuilder<TrackerDbContext>()
                .UseSqlite($"Data Source={fixture.Paths.Database}")
                .Options;
            await using (var db = new TrackerDbContext(options))
            {
                var repository = new EfTrackerRepository(db, new LocalHolidayProvider());
                await repository.InitializeAsync();
                var settings = await db.Settings.SingleAsync();
                settings.BackupRetentionCount = retention;
                settings.BackupIntervalHours = intervalHours;
                settings.BackupFolder = fixture.Paths.DefaultBackupDirectory;
                await db.SaveChangesAsync();
            }

            SqliteConnection.ClearAllPools();
            return fixture;
        }

        public SqliteBackupService CreateService() =>
            new(NullLogger<SqliteBackupService>.Instance, Paths);

        public async Task<decimal> ReadTargetPercentAsync()
        {
            SqliteConnection.ClearAllPools();
            var options = new DbContextOptionsBuilder<TrackerDbContext>()
                .UseSqlite($"Data Source={Paths.Database}")
                .Options;
            await using var db = new TrackerDbContext(options);
            return (await db.Settings.AsNoTracking().SingleAsync()).TargetPercent;
        }

        public async Task WriteTargetPercentAsync(decimal target)
        {
            SqliteConnection.ClearAllPools();
            var options = new DbContextOptionsBuilder<TrackerDbContext>()
                .UseSqlite($"Data Source={Paths.Database}")
                .Options;
            await using var db = new TrackerDbContext(options);
            var settings = await db.Settings.SingleAsync();
            settings.TargetPercent = target;
            await db.SaveChangesAsync();
            SqliteConnection.ClearAllPools();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup on Windows file locks.
            }

            return ValueTask.CompletedTask;
        }
    }
}
