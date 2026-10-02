using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PresenceTracker.Persistence;

namespace PresenceTracker.Infrastructure;

public sealed class BackupScheduler(IDatabaseBackupService backupService, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await backupService.CreateBackupAsync(stoppingToken); }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Erro no backup periódico; uma nova tentativa ocorrerá no próximo ciclo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}


