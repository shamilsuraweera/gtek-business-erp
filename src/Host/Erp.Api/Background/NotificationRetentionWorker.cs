using Erp.Modules.Platform.Application;

namespace Erp.Api.Background;

/// <summary>
/// Purges notifications that are past their retention (ADR 0024): once shortly after start-up, then on a fixed interval.
/// A failed run is logged and retried at the next interval; it never stops the API.
/// </summary>
public sealed class NotificationRetentionWorker(
    IServiceScopeFactory scopes, NotificationRetentionOptions options, ILogger<NotificationRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.InitialDelaySeconds), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.IntervalMinutes));
            do await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<INotificationRetention>().PurgeAsync(cancellationToken);
            if (result.Total > 0)
                logger.LogInformation("Notification retention removed {Read} read and {Unread} unread notifications.", result.Read, result.Unread);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Notification retention could not run; it will be retried in {Minutes} minutes.", options.IntervalMinutes);
        }
    }
}
