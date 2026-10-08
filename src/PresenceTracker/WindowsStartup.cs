using Microsoft.Win32;

namespace PresenceTracker;

internal static class WindowsStartup
{
    public const string AutostartArgument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PresenceTracker";

    public static bool IsAutostartLaunch(IEnumerable<string>? args = null) =>
        (args ?? Environment.GetCommandLineArgs())
            .Any(argument => string.Equals(argument, AutostartArgument, StringComparison.OrdinalIgnoreCase));

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
            key.SetValue(ValueName, string.Concat((char)34, executable, (char)34, ' ', AutostartArgument));
        }
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
