using System.Globalization;
using Erp.Application.Abstractions;
using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Application;

/// <summary>
/// Tells the people who pay vendors that bills are past due (ADR 0029): one summary per company per reminder period,
/// by email as well as in the app. A newer summary replaces the older one, and it clears itself once nothing is overdue.
/// </summary>
public sealed class OverdueBillReminder(
    IPurchasingStore store, INotifier notifier, ICompanyContext companyContext, IClock clock, ReminderOptions options) : IReminderJob
{
    public const string Category = "overdue-bill";
    public const string Subject = "overdue-bills";
    /// <summary>The permission of the people told: those who record vendor payments. Kept equal to the catalogue entry by a test.</summary>
    public const string Recipients = "purchasing.payments.record";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var overdue = await store.ListOverdueBillsAsync(companyContext.CompanyId, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken);
        if (overdue.Count == 0)
        {
            await notifier.ResolveSubjectAsync(Subject, cancellationToken);
            return 0;
        }

        if (!options.IsDue(now, await notifier.LastSentAsync(Subject, cancellationToken))) return 0;

        var oldest = overdue[0];
        var days = DateOnly.FromDateTime(now.UtcDateTime).DayNumber - oldest.DueDate.DayNumber;
        var total = overdue.Sum(x => x.Balance);
        await notifier.ResolveSubjectAsync(Subject, cancellationToken);
        await notifier.NotifyPermissionHoldersAsync(Recipients, new NotificationMessage(
            Category,
            overdue.Count == 1 ? "1 bill is overdue" : $"{overdue.Count} bills are overdue",
            $"{total.ToString("N2", CultureInfo.InvariantCulture)} is outstanding. The oldest, {oldest.Number}, was due {oldest.DueDate:yyyy-MM-dd} ({days} day{(days == 1 ? "" : "s")} ago).",
            "/bills", Subject, Email: true), null, cancellationToken);
        return 1;
    }
}
