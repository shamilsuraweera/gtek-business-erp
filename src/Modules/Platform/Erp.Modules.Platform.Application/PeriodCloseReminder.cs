using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;

namespace Erp.Modules.Platform.Application;

/// <summary>
/// Tells the people who manage accounting periods that periods have ended but are still open (ADR 0031): one summary per company per
/// reminder period, by email as well as in the app. A period counts once <see cref="ReminderOptions.PeriodCloseGraceDays"/> have passed since its
/// end date. A newer summary replaces the older one, and it clears itself once every ended period is closed.
/// </summary>
public sealed class PeriodCloseReminder(
    IAccountingPeriodStore periods, INotifier notifier, ICompanyContext companyContext, IClock clock, ReminderOptions options) : IReminderJob
{
    public const string Category = "period-close";
    public const string Subject = "period-close";
    /// <summary>The permission of the people told: those who close periods.</summary>
    public const string Recipients = Permissions.Platform.AccountingPeriodsManage;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var due = (await periods.ListAsync(companyContext.CompanyId, cancellationToken))
            .Where(p => p.Status == AccountingPeriodStatus.Open && p.EndDate.AddDays(options.PeriodCloseGraceDays) < today)
            .OrderBy(p => p.EndDate)
            .ToList();

        if (due.Count == 0)
        {
            await notifier.ResolveSubjectAsync(Subject, cancellationToken);
            return 0;
        }

        if (!options.IsDue(now, await notifier.LastSentAsync(Subject, cancellationToken))) return 0;

        var oldest = due[0];
        var days = today.DayNumber - oldest.EndDate.DayNumber;
        await notifier.ResolveSubjectAsync(Subject, cancellationToken);
        await notifier.NotifyPermissionHoldersAsync(Recipients, new NotificationMessage(
            Category,
            due.Count == 1 ? "1 accounting period is still open" : $"{due.Count} accounting periods are still open",
            $"{(due.Count == 1 ? "It has" : "They have")} ended but not been closed. The oldest, {oldest.Name}, ended {oldest.EndDate:yyyy-MM-dd} ({days} day{(days == 1 ? "" : "s")} ago). "
            + "Close a period once its postings are complete.",
            "/admin", Subject, Email: true), null, cancellationToken);
        return 1;
    }
}
