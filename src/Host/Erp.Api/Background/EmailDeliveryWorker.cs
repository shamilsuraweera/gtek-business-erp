using Erp.Modules.Platform.Application;

namespace Erp.Api.Background;

/// <summary>
/// Sends the emails waiting in the outbox (ADR 0026), checking every few seconds. A pass that fails is logged and tried again on the next
/// tick, and a message that fails is retried with a back-off by the delivery logic itself. Registered only when email is enabled.
/// </summary>
public sealed class EmailDeliveryWorker(IServiceScopeFactory scopes, EmailOptions options, ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Email delivery is on: {Host}:{Port} ({Security}), from {From}.", options.Host, options.Port, options.Security, options.FromAddress);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollSeconds));
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
            var run = await scope.ServiceProvider.GetRequiredService<IEmailDelivery>().RunOnceAsync(cancellationToken);
            if (run.Sent + run.Retrying + run.GaveUp > 0)
                logger.LogInformation("Email: {Sent} sent, {Retrying} will be retried, {GaveUp} given up.", run.Sent, run.Retrying, run.GaveUp);
            if (run.Purged > 0) logger.LogInformation("Email outbox: removed {Count} old messages.", run.Purged);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Email delivery could not run; it will be tried again in {Seconds} seconds.", options.PollSeconds);
        }
    }
}
