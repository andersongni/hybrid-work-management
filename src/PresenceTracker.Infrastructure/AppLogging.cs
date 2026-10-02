using Serilog;
using Serilog.Events;
using Serilog.Core;
using PresenceTracker.Application;

namespace PresenceTracker.Infrastructure;

public static class AppLogging
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    public static void SetMinimumLevel(string value)
    {
        if (Enum.TryParse<LogEventLevel>(value, true, out var level))
            LevelSwitch.MinimumLevel = level;
    }

    public static ILogger CreateLogger()
    {
        AppDataPaths.EnsureDirectories();
        return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(AppDataPaths.Logs, "app-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}




