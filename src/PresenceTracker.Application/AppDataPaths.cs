namespace PresenceTracker.Application;

public static class AppDataPaths
{
    private static readonly object Sync = new();
    private static bool loaded;
    private static string? logsOverride;
    private static string? backupsOverride;

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PresenceTracker");

    public static string Database => Path.Combine(Root, "presence.db");
    public static string Config => Path.Combine(Root, "config");
    public static string InstallConfigFile => Path.Combine(Config, "install.ini");

    public static string Backups
    {
        get
        {
            EnsureInstallConfigLoaded();
            return string.IsNullOrWhiteSpace(backupsOverride)
                ? Path.Combine(Root, "backups")
                : backupsOverride;
        }
    }

    public static string Logs
    {
        get
        {
            EnsureInstallConfigLoaded();
            return string.IsNullOrWhiteSpace(logsOverride)
                ? Path.Combine(Root, "logs")
                : logsOverride;
        }
    }

    public static void EnsureDirectories()
    {
        EnsureInstallConfigLoaded();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Config);
    }

    public static InstallPathDefaults? ReadInstallDefaults()
    {
        EnsureInstallConfigLoaded();
        if (!File.Exists(InstallConfigFile))
            return null;

        var values = ReadIniSection(InstallConfigFile, "Install");
        if (values.Count == 0)
            return null;

        values.TryGetValue("LogsFolder", out var logsFolder);
        values.TryGetValue("BackupsFolder", out var backupsFolder);
        values.TryGetValue("StartWithWindows", out var startWithWindows);
        values.TryGetValue("ApplyDefaults", out var applyDefaults);

        return new InstallPathDefaults(
            string.IsNullOrWhiteSpace(logsFolder) ? null : Path.GetFullPath(logsFolder.Trim()),
            string.IsNullOrWhiteSpace(backupsFolder) ? null : Path.GetFullPath(backupsFolder.Trim()),
            ParseBool(startWithWindows, defaultValue: true),
            ParseBool(applyDefaults, defaultValue: false));
    }

    public static void MarkInstallDefaultsApplied()
    {
        if (!File.Exists(InstallConfigFile))
            return;

        WriteIniValue(InstallConfigFile, "Install", "ApplyDefaults", "0");
    }

    private static void EnsureInstallConfigLoaded()
    {
        if (loaded)
            return;

        lock (Sync)
        {
            if (loaded)
                return;

            try
            {
                if (File.Exists(InstallConfigFile))
                {
                    var values = ReadIniSection(InstallConfigFile, "Install");
                    if (values.TryGetValue("LogsFolder", out var logsFolder) && !string.IsNullOrWhiteSpace(logsFolder))
                        logsOverride = Path.GetFullPath(logsFolder.Trim());
                    if (values.TryGetValue("BackupsFolder", out var backupsFolder) && !string.IsNullOrWhiteSpace(backupsFolder))
                        backupsOverride = Path.GetFullPath(backupsFolder.Trim());
                }
            }
            catch
            {
                logsOverride = null;
                backupsOverride = null;
            }

            loaded = true;
        }
    }

    private static Dictionary<string, string> ReadIniSection(string path, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return result;

        var currentSection = "";
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0)
                result[key] = value;
        }

        return result;
    }

    private static void WriteIniValue(string path, string section, string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var sectionHeader = $"[{section}]";
        var sectionIndex = lines.FindIndex(line => line.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add(sectionHeader);
            lines.Add($"{key}={value}");
            File.WriteAllLines(path, lines);
            return;
        }

        var inserted = false;
        for (var index = sectionIndex + 1; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                break;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var existingKey = trimmed[..separator].Trim();
            if (!existingKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            lines[index] = $"{key}={value}";
            inserted = true;
            break;
        }

        if (!inserted)
            lines.Insert(sectionIndex + 1, $"{key}={value}");

        File.WriteAllLines(path, lines);
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Trim() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => defaultValue
        };
    }
}

public sealed record InstallPathDefaults(
    string? LogsFolder,
    string? BackupsFolder,
    bool StartWithWindows,
    bool ApplyDefaults);
