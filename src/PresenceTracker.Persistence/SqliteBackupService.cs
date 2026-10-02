using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PresenceTracker.Application;

namespace PresenceTracker.Persistence;

public interface IDatabaseBackupService
{
    Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteBackupService(ILogger<SqliteBackupService> logger) : IDatabaseBackupService
{
    public async Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppDataPaths.Database)) return null;
        AppDataPaths.EnsureDirectories();
        var filename = $"presence-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        var destination = Path.Combine(AppDataPaths.Backups, filename);
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = AppDataPaths.Database, Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);

            await using var settingsConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = AppDataPaths.Database, Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await settingsConnection.OpenAsync(cancellationToken);
            await using var command = settingsConnection.CreateCommand();
            command.CommandText = "SELECT BackupRetentionCount FROM Settings WHERE Id = 1";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var retention = value is null or DBNull ? 30 : Convert.ToInt32(value);
            var oldFiles = new DirectoryInfo(AppDataPaths.Backups).GetFiles("presence-*.db")
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(Math.Clamp(retention, 1, 365));
            foreach (var file in oldFiles)
                file.Delete();
            logger.LogInformation("Backup SQLite criado em {BackupPath}", destination);
            return destination;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao criar backup SQLite.");
            throw;
        }
    }
}



