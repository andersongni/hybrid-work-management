namespace PresenceTracker.Application;

public static class AppDataPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresenceTracker");
    public static string Database => Path.Combine(Root, "presence.db");
    public static string Backups => Path.Combine(Root, "backups");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Config => Path.Combine(Root, "config");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Config);
    }
}






