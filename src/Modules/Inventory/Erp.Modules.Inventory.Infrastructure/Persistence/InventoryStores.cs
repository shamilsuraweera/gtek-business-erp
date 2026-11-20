using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure.Persistence;

public sealed class ItemStore(InventoryDbContext db) : IItemStore
{
    public Task AddAsync(Item item, CancellationToken cancellationToken)
    {
        db.Items.Add(item);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Item>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Items.Where(x => x.CompanyId == companyId).OrderBy(x => x.Sku).ToListAsync(cancellationToken);

    public Task<Item?> GetAsync(CompanyId companyId, ItemId id, CancellationToken cancellationToken) =>
        db.Items.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Item>> GetManyAsync(CompanyId companyId, IReadOnlyCollection<ItemId> ids, CancellationToken cancellationToken) =>
        await db.Items.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToListAsync(cancellationToken);

    public Task<Item?> GetBySkuAsync(CompanyId companyId, string sku, CancellationToken cancellationToken)
    {
        var normalized = Item.NormalizeSku(sku);
        return db.Items.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Sku == normalized, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class LocationStore(InventoryDbContext db) : ILocationStore
{
    public Task AddAsync(Location location, CancellationToken cancellationToken)
    {
        db.Locations.Add(location);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Location>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Locations.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public Task<Location?> GetAsync(CompanyId companyId, LocationId id, CancellationToken cancellationToken) =>
        db.Locations.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<Location?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = Location.NormalizeCode(code);
        return db.Locations.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class StockStore(InventoryDbContext db) : IStockStore
{
    public Task AddEntryAsync(ItemLedgerEntry entry, CancellationToken cancellationToken)
    {
        db.LedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public async Task<decimal> OnHandAsync(CompanyId companyId, ItemId itemId, LocationId? locationId, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.CompanyId == companyId && x.ItemId == itemId);
        if (locationId is { } location) query = query.Where(x => x.LocationId == location);
        return await query.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<ItemQuantity>> OnHandByItemAsync(CompanyId companyId, CancellationToken cancellationToken)
    {
        var rows = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Quantity * x.UnitCost) })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new ItemQuantity(x.ItemId, x.Quantity, decimal.Round(x.Value, 2))).ToArray();
    }

    public async Task<IReadOnlyList<ItemLedgerEntry>> LedgerAsync(CompanyId companyId, ItemId? itemId, int take, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (itemId is { } id) query = query.Where(x => x.ItemId == id);
        return await query.OrderByDescending(x => x.PostedAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<T> InItemTransactionAsync<T>(
        CompanyId companyId, ItemId itemId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"inventory:{companyId.Value}:{itemId.Value}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
