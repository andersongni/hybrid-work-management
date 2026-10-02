using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using PresenceTracker.Application;
using PresenceTracker.Domain;
using PresenceTracker.Infrastructure;
using PresenceTracker.NetworkMonitoring;
using PresenceTracker.Persistence;

namespace PresenceTracker;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        using var singleInstance = new Mutex(true, @"Local\PresenceTracker.Singleton", out var created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        AppDataPaths.EnsureDirectories();
        var serilog = AppLogging.CreateLogger();
        Log.Logger = serilog;
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(serilog, dispose: false);
        builder.Services.AddDbContext<TrackerDbContext>(options => options.UseSqlite($"Data Source={AppDataPaths.Database}"));
        builder.Services.AddSingleton<PresenceCalculator>();
        builder.Services.AddSingleton<LocalHolidayProvider>();
        builder.Services.AddSingleton<IHolidayProvider>(services => services.GetRequiredService<LocalHolidayProvider>());
        builder.Services.AddScoped<ITrackerRepository, EfTrackerRepository>();
        builder.Services.AddScoped<TrackerService>();
        builder.Services.AddSingleton<WlanMonitor>();
        builder.Services.AddSingleton<IDatabaseBackupService, SqliteBackupService>();
        builder.Services.AddHostedService<BackupScheduler>();
        builder.Services.AddScoped<MainForm>();

        using var host = builder.Build();
        await host.StartAsync();
        using var uiScope = host.Services.CreateScope();
        var tracker = uiScope.ServiceProvider.GetRequiredService<TrackerService>();
        var logger = uiScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PresenceTracker.Startup");
        try
        {
            await tracker.InitializeAsync();
            var initial = await tracker.GetMonthAsync(DateTime.Today.Year, DateTime.Today.Month, DateOnly.FromDateTime(DateTime.Today));
            AppLogging.SetMinimumLevel(initial.Data.Settings.MinimumLogLevel);
            WindowsStartup.SetEnabled(initial.Data.Settings.StartWithWindows);

            var monitor = host.Services.GetRequiredService<WlanMonitor>();
            var changes = Channel.CreateUnbounded<WlanNetworkTransition>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            monitor.NetworkChanged += transition => changes.Writer.TryWrite(transition);
            try { monitor.Start(); }
            catch (Exception exception) { logger.LogWarning(exception, "Monitoramento WLAN não pôde iniciar; o aplicativo continuará disponível."); }
            var configuredNetworks = initial.Data.Networks
                .Where(network => network.IsActive && network.CountsAsPresence)
                .Select(network => network.Ssid)
                .ToArray();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var hasAttendanceForToday = initial.Data.AttendanceEvents.Any(attendance =>
                attendance.Date == today && (attendance.Source == AttendanceSource.Automatic || attendance.Status == AttendanceStatus.Active));
            try
            {
                if (hasAttendanceForToday)
                    monitor.MarkCurrentConnectionsForNetworks(configuredNetworks);
                else
                    monitor.RecordCurrentConnectionsForNetworks(configuredNetworks);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Não foi possível reconciliar a rede conectada na inicialização.");
            }
            using var stop = new CancellationTokenSource();
            var form = uiScope.ServiceProvider.GetRequiredService<MainForm>();
            var syncTask = Task.Run(async () =>
            {
                foreach (var year in new[] { DateTime.Today.Year, DateTime.Today.Year + 1 })
                {
                    if (stop.IsCancellationRequested) return;
                    try
                    {
                        using var syncScope = host.Services.CreateScope();
                        var syncTracker = syncScope.ServiceProvider.GetRequiredService<TrackerService>();
                        var existing = await syncTracker.GetHolidaysAsync(year, stop.Token);
                        if (existing.Any(h => h.Source == HolidaySource.Synchronized))
                            continue;
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                        await syncTracker.SynchronizeHolidaysAsync(year, new CombinedHolidayProvider(new LocalHolidayProvider(), new BrasilApiHolidayProvider(client)), stop.Token);
                        logger.LogInformation("Sincronização inicial de feriados concluída para {Year}.", year);
                        form.RefreshFromNetworkChange();
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Sincronização inicial de feriados indisponível para {Year}; a base local continuará ativa.", year);
                    }
                }
            }, stop.Token);
            var processor = Task.Run(async () =>
            {
                try
                {
                    await foreach (var transition in changes.Reader.ReadAllAsync(stop.Token))
                    {
                        try
                        {
                            using var eventScope = host.Services.CreateScope();
                            var eventTracker = eventScope.ServiceProvider.GetRequiredService<TrackerService>();
                            await eventTracker.RegisterNetworkChangeAsync(new NetworkChange(transition.OccurredAt,
                                transition.InterfaceId, transition.InterfaceName, transition.Ssid, transition.Type), stop.Token);
                            logger.LogInformation("Rede {Event} em {Ssid} pela interface {Interface}.",
                                transition.Type, transition.Ssid, transition.InterfaceName);
                            form.RefreshFromNetworkChange();
                        }
                        catch (Exception exception)
                        {
                            logger.LogError(exception, "Falha ao persistir evento de rede.");
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }, stop.Token);

            System.Windows.Forms.Application.Run(form);
            changes.Writer.TryComplete();
            stop.Cancel();
            try { await processor; } catch (OperationCanceledException) { }
            try { await syncTask; } catch (OperationCanceledException) { }
            catch (Exception exception) { logger.LogWarning(exception, "Sincronização inicial de feriados falhou."); }
            monitor.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Falha na inicialização do Presence Tracker.");
            MessageBox.Show("O Presence Tracker não conseguiu abrir o banco de dados local. Consulte os logs em %LOCALAPPDATA%\\\\PresenceTracker\\\\logs.",
                "Presence Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await host.StopAsync();
            Log.CloseAndFlush();
        }
    }
}














