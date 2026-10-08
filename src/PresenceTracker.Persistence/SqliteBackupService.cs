using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PresenceTracker.Application;

namespace PresenceTracker.Persistence;

public interface IDatabaseBackupService
{
    Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task<string?> CreateBackupIfDueAsync(CancellationToken cancellationToken = default);
    Task<string?> RestoreBackupAsync(string backupPath, CancellationToken cancellationToken = default);
}

public sealed class SqliteBackupService(ILogger<SqliteBackupService> logger, IBackupPaths paths) : IDatabaseBackupService
{
    public async Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.Database)) return null;
        paths.EnsureDirectories();
        try
        {
            var (backupDirectory, retention, _) = await ReadBackupSettingsAsync(cancellationToken);
            Directory.CreateDirectory(backupDirectory);
            var filename = $"presence-{DateTime.Now:yyyyMMdd-HHmmss-fff}.ptbackup";
            var destination = Path.Combine(backupDirectory, filename);
            await CopyDatabaseAsync(paths.Database, destination, cancellationToken);
            SqliteConnection.ClearAllPools();

            var oldFiles = new DirectoryInfo(backupDirectory).GetFiles("presence-*")
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

    public async Task<string?> CreateBackupIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.Database)) return null;
        var (backupDirectory, _, intervalHours) = await ReadBackupSettingsAsync(cancellationToken);
        if (intervalHours is < 1 or > 720) return null;
        Directory.CreateDirectory(backupDirectory);
        var mostRecent = new DirectoryInfo(backupDirectory).GetFiles("presence-*")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (mostRecent is not null && DateTime.UtcNow - mostRecent.LastWriteTimeUtc < TimeSpan.FromHours(intervalHours))
            return null;
        return await CreateBackupAsync(cancellationToken);
    }

    public async Task<string?> RestoreBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("O arquivo de backup não foi encontrado.", backupPath);
        if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(paths.Database), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha um arquivo de backup, não o banco de dados em uso.");

        try
        {
            await ValidateBackupAsync(backupPath, cancellationToken);
            var safetyBackup = await CreateBackupAsync(cancellationToken);
            await CopyDatabaseAsync(backupPath, paths.Database, cancellationToken);
            SqliteConnection.ClearAllPools();
            logger.LogInformation("Backup Presence Tracker restaurado de {BackupPath}; segurança em {SafetyBackupPath}.", backupPath, safetyBackup);
            return safetyBackup;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao restaurar backup Presence Tracker de {BackupPath}.", backupPath);
            throw;
        }
    }

    private async Task<(string Directory, int Retention, int IntervalHours)> ReadBackupSettingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.Database, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT BackupFolder, BackupRetentionCount, BackupIntervalHours FROM Settings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (paths.DefaultBackupDirectory, 30, 24);
        var directory = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var retention = reader.IsDBNull(1) ? 30 : reader.GetInt32(1);
        var intervalHours = reader.IsDBNull(2) ? 24 : reader.GetInt32(2);
        return (string.IsNullOrWhiteSpace(directory) ? paths.DefaultBackupDirectory : Path.GetFullPath(directory), retention, intervalHours);
    }

    private static async Task ValidateBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check";
            var result = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O arquivo selecionado está corrompido ou não é um banco SQLite válido.");

            await using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Settings', 'PresenceNetworks', 'AttendanceEvents', 'NetworkEvents', 'Plans', 'Holidays', 'Classifications', 'MonthlySnapshots')";
            var count = Convert.ToInt32(await tables.ExecuteScalarAsync(cancellationToken));
            if (count != 8)
                throw new InvalidDataException("O arquivo não contém todos os dados de um backup do Presence Tracker.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("O arquivo selecionado está corrompido ou não é um banco SQLite válido.", exception);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static Task CopyDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }, cancellationToken);
}
