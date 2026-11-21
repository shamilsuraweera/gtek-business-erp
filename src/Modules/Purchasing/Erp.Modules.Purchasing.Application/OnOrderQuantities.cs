using Erp.Application.Abstractions;

namespace Erp.Modules.Purchasing.Application;

/// <summary>
/// What is still to arrive from vendors: for orders that have been released (placed) and not fully received, each line's ordered quantity less
/// what has been received. Drafts and confirmed-but-unreleased orders do not count, because the vendor has not been asked yet; cancelled
/// and fully received orders have nothing left to come.
/// </summary>
public sealed class OnOrderQuantities(IPurchasingStore store, ICompanyContext companyContext) : IOnOrderQuantities
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetAsync(CancellationToken cancellationToken)
    {
        var orders = await store.ListOrdersAwaitingGoodsAsync(PurchasingCompany.Require(companyContext), cancellationToken);
        return orders
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.ItemId)
            .Select(g => (Item: g.Key, Quantity: g.Sum(l => Math.Max(0m, l.Quantity - l.QuantityReceived))))
            .Where(x => x.Quantity > 0m)
            .ToDictionary(x => x.Item, x => x.Quantity);
    }
}
