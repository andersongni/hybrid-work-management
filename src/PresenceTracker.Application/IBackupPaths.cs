namespace PresenceTracker.Application;

public interface IBackupPaths
{
    string Database { get; }
    string DefaultBackupDirectory { get; }
    void EnsureDirectories();
}

public sealed class AppDataBackupPaths : IBackupPaths
{
    public string Database => AppDataPaths.Database;
    public string DefaultBackupDirectory => AppDataPaths.Backups;
    public void EnsureDirectories() => AppDataPaths.EnsureDirectories();
}
