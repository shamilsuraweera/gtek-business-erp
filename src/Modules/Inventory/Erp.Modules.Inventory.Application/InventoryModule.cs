using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory.Application;

public sealed record ItemResponse(Guid Id, string Sku, string Name, string UnitOfMeasure, decimal UnitPrice, decimal UnitCost, bool IsActive, string? TaxCode, decimal? PriceTolerancePercent);
public sealed record CreateItemRequest(string Sku, string Name, string? UnitOfMeasure, decimal UnitPrice, decimal UnitCost, string? TaxCode = null, decimal? PriceTolerancePercent = null);
public sealed record UpdateItemRequest(string Name, string? UnitOfMeasure, decimal UnitPrice, decimal UnitCost, string? TaxCode = null, decimal? PriceTolerancePercent = null);

public sealed record LocationResponse(Guid Id, string Code, string Name, bool IsActive);
public sealed record CreateLocationRequest(string Code, string Name);

public sealed record ReceiveStockRequest(Guid ItemId, decimal Quantity, decimal? UnitCost, DateOnly? Date, string? Reference, Guid? LocationId);
public sealed record AdjustStockRequest(Guid ItemId, decimal Quantity, string Reason, DateOnly? Date, Guid? LocationId);

public sealed record StockLevelResponse(
    Guid ItemId, string Sku, string ItemName, string UnitOfMeasure, decimal OnHand, decimal UnitCost, decimal Value);
public sealed record StockLedgerEntryResponse(
    Guid Id, Guid ItemId, string Sku, Guid LocationId, decimal Quantity, decimal UnitCost,
    DateOnly PostingDate, DateTimeOffset PostedAt, string SourceType, string SourceReference);

public sealed record ItemQuantity(ItemId ItemId, decimal Quantity, decimal Value);

public interface IItemStore
{
    Task AddAsync(Item item, CancellationToken cancellationToken);
    Task<IReadOnlyList<Item>> ListAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<Item?> GetAsync(CompanyId companyId, ItemId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Item>> GetManyAsync(CompanyId companyId, IReadOnlyCollection<ItemId> ids, CancellationToken cancellationToken);
    Task<Item?> GetBySkuAsync(CompanyId companyId, string sku, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ILocationStore
{
    Task AddAsync(Location location, CancellationToken cancellationToken);
    Task<IReadOnlyList<Location>> ListAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<Location?> GetAsync(CompanyId companyId, LocationId id, CancellationToken cancellationToken);
    Task<Location?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IStockStore
{
    Task AddEntryAsync(ItemLedgerEntry entry, CancellationToken cancellationToken);
    Task<decimal> OnHandAsync(CompanyId companyId, ItemId itemId, LocationId? locationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemQuantity>> OnHandByItemAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemLedgerEntry>> LedgerAsync(CompanyId companyId, ItemId? itemId, int take, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> in a transaction holding a per-item lock, so concurrent issues cannot oversell stock.</summary>
    Task<T> InItemTransactionAsync<T>(CompanyId companyId, ItemId itemId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IItemService
{
    Task<ItemResponse> CreateAsync(CreateItemRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemResponse>> ListAsync(CancellationToken cancellationToken);
    Task<ItemResponse?> GetAsync(ItemId id, CancellationToken cancellationToken);
    Task<ItemResponse?> UpdateAsync(ItemId id, UpdateItemRequest request, CancellationToken cancellationToken);
    Task<ItemResponse?> SetActiveAsync(ItemId id, bool active, CancellationToken cancellationToken);
}

public interface ILocationService
{
    Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocationResponse>> ListAsync(CancellationToken cancellationToken);
}

public interface IStockService
{
    Task<StockLedgerEntryResponse> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken);
    Task<StockLedgerEntryResponse> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockLevelResponse>> OnHandAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StockLedgerEntryResponse>> LedgerAsync(Guid? itemId, int take, CancellationToken cancellationToken);
}

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<StockService>();
        services.AddScoped<IStockService>(sp => sp.GetRequiredService<StockService>());
        services.AddScoped<IInventoryStock>(sp => sp.GetRequiredService<StockService>());
        services.AddScoped<IInventoryCatalog, InventoryCatalog>();
        return services;
    }
}
