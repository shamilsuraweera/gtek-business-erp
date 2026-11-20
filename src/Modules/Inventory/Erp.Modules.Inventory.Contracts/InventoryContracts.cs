namespace Erp.Modules.Inventory.Contracts;

public sealed record ItemInfo(Guid Id, string Sku, string Name, string UnitOfMeasure, decimal UnitPrice, decimal UnitCost, bool IsActive);

public sealed record StockMovementCommand(
    Guid ItemId,
    decimal Quantity,
    DateOnly Date,
    string SourceType,
    string SourceReference,
    Guid? LocationId = null,
    decimal? UnitCost = null);

public sealed record StockMovementResult(Guid ItemId, decimal Quantity, decimal UnitCost, decimal TotalCost);

/// <summary>Cross-module read access to the item catalogue.</summary>
public interface IInventoryCatalog
{
    Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
}

/// <summary>Cross-module stock movements. Issues are rejected when they would make stock negative.</summary>
public interface IInventoryStock
{
    /// <summary>Removes <c>Quantity</c> (positive) from stock, valued at <c>UnitCost</c> when given, otherwise the item's standard cost.</summary>
    Task<StockMovementResult> IssueAsync(StockMovementCommand command, CancellationToken cancellationToken);

    /// <summary>Adds <c>Quantity</c> (positive) to stock, valued at <c>UnitCost</c> when given, otherwise the item's standard cost.</summary>
    Task<StockMovementResult> ReceiveAsync(StockMovementCommand command, CancellationToken cancellationToken);
}
