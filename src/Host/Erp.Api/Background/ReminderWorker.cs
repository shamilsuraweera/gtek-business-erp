using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;

namespace Erp.Api.Background;

/// <summary>
/// Runs the modules' scheduled reminders (ADR 0029) for every active company: once shortly after start-up, then every
/// <see cref="ReminderOptions.IntervalMinutes"/>. Each reminder decides itself whether it is due, so running often is harmless.
/// One company or reminder failing is logged and never stops the others or the API.
/// </summary>
public sealed class ReminderWorker(IServiceScopeFactory scopes, ReminderOptions options, ILogger<ReminderWorker> logger) : BackgroundService
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
        IReadOnlyList<CompanyResponse> companies;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            companies = (await scope.ServiceProvider.GetRequiredService<ICompanyService>().ListAsync(cancellationToken))
                .Where(c => c.Status == CompanyStatus.Active).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Reminders could not list companies; they will be retried in {Minutes} minutes.", options.IntervalMinutes);
            return;
        }

        var sent = 0;
        foreach (var company in companies)
        {
            try
            {
                // A scope per company: its own database contexts, with the company the reminders act for.
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ActiveCompanyContext>().Set(new Erp.SharedKernel.CompanyId(company.Id));
                foreach (var job in scope.ServiceProvider.GetServices<IReminderJob>())
                {
                    try
                    {
                        sent += await job.RunAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Reminder {Job} failed for company {Company}.", job.GetType().Name, company.Code);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Reminders failed for company {Company}.", company.Code);
            }
        }

        if (sent > 0) logger.LogInformation("Sent {Count} reminder notification batches.", sent);
    }
}
