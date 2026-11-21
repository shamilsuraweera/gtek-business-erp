using System.Globalization;
using Erp.Application.Abstractions;
using Erp.SharedKernel;

namespace Erp.Modules.Inventory.Application;

/// <summary>
/// Tells the people who manage stock that items are at or below their reorder level (ADR 0030): one summary per company per reminder
/// period, by email as well as in the app. Only active items with a reorder level are watched. An item that is at or below its level is
/// left out if what is already on order would lift it back above the level (ADR 0037). A newer summary replaces the older one, and it clears
/// itself once nothing is low.
/// </summary>
public sealed class LowStockReminder(
    IItemStore items, IStockStore stock, IOnOrderQuantities onOrder, INotifier notifier, ICompanyContext companyContext, IClock clock, ReminderOptions options) : IReminderJob
{
    public const string Category = "low-stock";
    public const string Subject = "low-stock";
    /// <summary>The permission of the people told: those who receive and adjust stock. Kept equal to the catalogue entry by a test.</summary>
    public const string Recipients = "inventory.stock.manage";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var companyId = companyContext.CompanyId;
        var monitored = (await items.ListAsync(companyId, cancellationToken)).Where(i => i is { IsActive: true, ReorderLevel: not null }).ToList();
        var onHand = (await stock.OnHandByItemAsync(companyId, cancellationToken)).ToDictionary(x => x.ItemId, x => x.Quantity);
        var coming = monitored.Count == 0 ? new Dictionary<Guid, decimal>() : (await onOrder.GetAsync(cancellationToken)).ToDictionary();
        var low = monitored
            .Select(i => (Item: i, OnHand: onHand.GetValueOrDefault(i.Id), OnOrder: coming.GetValueOrDefault(i.Id.Value)))
            // Low now, and still low even counting what is on its way.
            .Where(x => x.Item.IsLow(x.OnHand) && x.Item.IsLow(x.OnHand + x.OnOrder))
            // The most urgent first: furthest below the level, out of stock before merely low.
            .OrderBy(x => x.OnHand - x.Item.ReorderLevel!.Value).ThenBy(x => x.Item.Sku)
            .ToList();

        if (low.Count == 0)
        {
            await notifier.ResolveSubjectAsync(Subject, cancellationToken);
            return 0;
        }

        if (!options.IsDue(clock.UtcNow, await notifier.LastSentAsync(Subject, cancellationToken))) return 0;

        var (worst, quantity, incoming) = (low[0].Item, low[0].OnHand, low[0].OnOrder);
        var outOfStock = low.Count(x => x.OnHand <= 0);
        await notifier.ResolveSubjectAsync(Subject, cancellationToken);
        await notifier.NotifyPermissionHoldersAsync(Recipients, new NotificationMessage(
            Category,
            low.Count == 1 ? "1 item is low on stock" : $"{low.Count} items are low on stock",
            $"{(outOfStock > 0 ? $"{outOfStock} out of stock. " : string.Empty)}Lowest: {worst.Sku} ({worst.Name}) has {Format(quantity)} {worst.UnitOfMeasure} left{(incoming > 0 ? $" (+{Format(incoming)} on order)" : string.Empty)}, reorder level {Format(worst.ReorderLevel!.Value)}.",
            "/items", Subject, Email: true), null, cancellationToken);
        return 1;
    }

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
