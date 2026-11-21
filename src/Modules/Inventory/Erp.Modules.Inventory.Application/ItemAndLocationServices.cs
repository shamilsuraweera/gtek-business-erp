using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Inventory.Application;

public sealed class ItemService(IItemStore store, ITaxCatalog taxCatalog, ICompanyContext companyContext, IClock clock, IAuditRecorder audit) : IItemService
{
    public async Task<ItemResponse> CreateAsync(CreateItemRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany(companyContext);
        if (await store.GetBySkuAsync(companyId, request.Sku, cancellationToken) is not null)
            throw new InvalidOperationException("An item with this SKU already exists for the company.");
        await EnsureTaxCodeAsync(request.TaxCode, cancellationToken);
        var item = Item.Create(companyId, request.Sku, request.Name, request.UnitOfMeasure ?? "EA",
            request.UnitPrice, request.UnitCost, clock.UtcNow, request.TaxCode, request.PriceTolerancePercent, request.QuantityTolerancePercent, request.UnderDeliveryTolerancePercent, request.ReorderLevel);
        await store.AddAsync(item, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Inventory", "item.created", "Item", item.Id.Value.ToString(), item.Sku, true, null, cancellationToken);
        return Map(item);
    }

    public async Task<IReadOnlyList<ItemResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(RequireCompany(companyContext), cancellationToken)).Select(Map).ToArray();

    public async Task<ItemResponse?> GetAsync(ItemId id, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(RequireCompany(companyContext), id, cancellationToken);
        return item is null ? null : Map(item);
    }

    public async Task<ItemResponse?> UpdateAsync(ItemId id, UpdateItemRequest request, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(RequireCompany(companyContext), id, cancellationToken);
        if (item is null) return null;
        // Keeping the item's current code is always allowed (it may have been deactivated since); a new code must be usable.
        if (Item.NormalizeTaxCode(request.TaxCode) != item.TaxCode) await EnsureTaxCodeAsync(request.TaxCode, cancellationToken);
        item.Update(request.Name, request.UnitOfMeasure ?? item.UnitOfMeasure, request.UnitPrice, request.UnitCost, request.TaxCode, request.PriceTolerancePercent, request.QuantityTolerancePercent, request.UnderDeliveryTolerancePercent, request.ReorderLevel);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Inventory", "item.updated", "Item", item.Id.Value.ToString(), item.Sku, true,
            new Dictionary<string, object?> { ["UnitPrice"] = item.UnitPrice, ["UnitCost"] = item.UnitCost, ["TaxCode"] = item.TaxCode, ["PriceTolerancePercent"] = item.PriceTolerancePercent, ["QuantityTolerancePercent"] = item.QuantityTolerancePercent, ["UnderDeliveryTolerancePercent"] = item.UnderDeliveryTolerancePercent, ["ReorderLevel"] = item.ReorderLevel }, cancellationToken);
        return Map(item);
    }

    public async Task<ItemResponse?> SetActiveAsync(ItemId id, bool active, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(RequireCompany(companyContext), id, cancellationToken);
        if (item is null) return null;
        if (active) item.Activate(); else item.Deactivate();
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Inventory", active ? "item.activated" : "item.deactivated", "Item",
            item.Id.Value.ToString(), item.Sku, true, null, cancellationToken);
        return Map(item);
    }

    private async Task EnsureTaxCodeAsync(string? taxCode, CancellationToken cancellationToken)
    {
        var code = Item.NormalizeTaxCode(taxCode);
        if (code is null) return;
        var found = await taxCatalog.GetAsync([code], cancellationToken);
        if (!found.TryGetValue(code, out var info)) throw new DomainException($"Tax code '{code}' does not exist.");
        if (!info.IsActive) throw new DomainException($"Tax code '{code}' is inactive.");
    }

    internal static CompanyId RequireCompany(ICompanyContext context) =>
        context.HasCompany ? context.CompanyId : throw new InvalidOperationException("An active company context is required.");

    internal static ItemResponse Map(Item i) => new(i.Id.Value, i.Sku, i.Name, i.UnitOfMeasure, i.UnitPrice, i.UnitCost, i.IsActive, i.TaxCode, i.PriceTolerancePercent, i.QuantityTolerancePercent, i.UnderDeliveryTolerancePercent, i.ReorderLevel);
}

public sealed class LocationService(ILocationStore store, ICompanyContext companyContext) : ILocationService
{
    public async Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        var companyId = ItemService.RequireCompany(companyContext);
        if (await store.GetByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("A location with this code already exists for the company.");
        var location = Location.Create(companyId, request.Code, request.Name);
        await store.AddAsync(location, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(location);
    }

    public async Task<IReadOnlyList<LocationResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(ItemService.RequireCompany(companyContext), cancellationToken)).Select(Map).ToArray();

    private static LocationResponse Map(Location l) => new(l.Id.Value, l.Code, l.Name, l.IsActive);
}

public sealed class InventoryCatalog(IItemStore items, ICompanyContext companyContext) : IInventoryCatalog
{
    public async Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        (await items.GetManyAsync(
            ItemService.RequireCompany(companyContext), itemIds.Select(x => new ItemId(x)).ToArray(), cancellationToken))
        .Select(i => new ItemInfo(i.Id.Value, i.Sku, i.Name, i.UnitOfMeasure, i.UnitPrice, i.UnitCost, i.IsActive, i.TaxCode, i.PriceTolerancePercent, i.QuantityTolerancePercent, i.UnderDeliveryTolerancePercent))
        .ToArray();
}
