using Erp.Application.Abstractions;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Inventory.Application;

public sealed class StockService(
    IStockStore stock,
    IItemStore items,
    ILocationStore locations,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IStockService, IInventoryStock
{
    public async Task<StockLedgerEntryResponse> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0) throw new DomainException("Received quantity must be positive.");
        var entry = await MoveAsync(
            new ItemId(request.ItemId), request.Quantity, request.UnitCost,
            request.Date ?? Today(), "Receipt", request.Reference ?? string.Empty,
            request.LocationId is { } l ? new LocationId(l) : null, cancellationToken);
        await audit.RecordAsync("Inventory", "stock.received", "Item", request.ItemId.ToString(), null, true,
            new Dictionary<string, object?> { ["Quantity"] = request.Quantity }, cancellationToken);
        return await MapAsync(entry, cancellationToken);
    }

    public async Task<StockLedgerEntryResponse> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new DomainException("An adjustment reason is required.");
        var entry = await MoveAsync(
            new ItemId(request.ItemId), request.Quantity, null, request.Date ?? Today(), "Adjustment", request.Reason,
            request.LocationId is { } l ? new LocationId(l) : null, cancellationToken);
        await audit.RecordAsync("Inventory", "stock.adjusted", "Item", request.ItemId.ToString(), null, true,
            new Dictionary<string, object?> { ["Quantity"] = request.Quantity, ["Reason"] = request.Reason }, cancellationToken);
        return await MapAsync(entry, cancellationToken);
    }

    public async Task<StockMovementResult> IssueAsync(StockMovementCommand command, CancellationToken cancellationToken)
    {
        if (command.Quantity <= 0) throw new DomainException("Issued quantity must be positive.");
        var entry = await MoveAsync(
            new ItemId(command.ItemId), -command.Quantity, null, command.Date, command.SourceType, command.SourceReference,
            command.LocationId is { } l ? new LocationId(l) : null, cancellationToken);
        return new StockMovementResult(command.ItemId, command.Quantity, entry.UnitCost, entry.UnitCost * command.Quantity);
    }

    public async Task<StockMovementResult> ReceiveAsync(StockMovementCommand command, CancellationToken cancellationToken)
    {
        if (command.Quantity <= 0) throw new DomainException("Received quantity must be positive.");
        var entry = await MoveAsync(
            new ItemId(command.ItemId), command.Quantity, null, command.Date, command.SourceType, command.SourceReference,
            command.LocationId is { } l ? new LocationId(l) : null, cancellationToken);
        return new StockMovementResult(command.ItemId, command.Quantity, entry.UnitCost, entry.UnitCost * command.Quantity);
    }

    public async Task<IReadOnlyList<StockLevelResponse>> OnHandAsync(CancellationToken cancellationToken)
    {
        var companyId = ItemService.RequireCompany(companyContext);
        var quantities = (await stock.OnHandByItemAsync(companyId, cancellationToken)).ToDictionary(x => x.ItemId, x => x.Quantity);
        return (await items.ListAsync(companyId, cancellationToken))
            .Select(i =>
            {
                var onHand = quantities.GetValueOrDefault(i.Id);
                return new StockLevelResponse(i.Id.Value, i.Sku, i.Name, i.UnitOfMeasure, onHand, i.UnitCost, onHand * i.UnitCost);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<StockLedgerEntryResponse>> LedgerAsync(Guid? itemId, int take, CancellationToken cancellationToken)
    {
        var companyId = ItemService.RequireCompany(companyContext);
        var entries = await stock.LedgerAsync(companyId, itemId is { } id ? new ItemId(id) : null, Math.Clamp(take, 1, 500), cancellationToken);
        var skus = (await items.ListAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Sku);
        return entries.Select(e => Map(e, skus.GetValueOrDefault(e.ItemId, string.Empty))).ToArray();
    }

    private async Task<ItemLedgerEntry> MoveAsync(
        ItemId itemId, decimal signedQuantity, decimal? unitCost, DateOnly date,
        string sourceType, string sourceReference, LocationId? locationId, CancellationToken cancellationToken)
    {
        var companyId = ItemService.RequireCompany(companyContext);
        if (signedQuantity == 0) throw new DomainException("Stock quantity cannot be zero.");
        var item = await items.GetAsync(companyId, itemId, cancellationToken)
            ?? throw new DomainException("The item was not found.");
        if (!item.IsActive) throw new DomainException($"Item '{item.Sku}' is inactive.");
        var location = await ResolveLocationAsync(companyId, locationId, cancellationToken);

        return await stock.InItemTransactionAsync(companyId, itemId, async ct =>
        {
            if (signedQuantity < 0)
            {
                var available = await stock.OnHandAsync(companyId, itemId, location.Id, ct);
                if (available + signedQuantity < 0)
                    throw new DomainException($"Insufficient stock for '{item.Sku}': {available:0.####} available, {-signedQuantity:0.####} requested.");
            }

            var entry = ItemLedgerEntry.Create(
                companyId, itemId, location.Id, signedQuantity, unitCost ?? item.UnitCost,
                clock.UtcNow, date, sourceType, sourceReference);
            await stock.AddEntryAsync(entry, ct);
            await stock.SaveChangesAsync(ct);
            return entry;
        }, cancellationToken);
    }

    private async Task<Location> ResolveLocationAsync(CompanyId companyId, LocationId? id, CancellationToken ct)
    {
        if (id is { } locationId)
            return await locations.GetAsync(companyId, locationId, ct) ?? throw new DomainException("The location was not found.");
        var main = await locations.GetByCodeAsync(companyId, Location.DefaultCode, ct);
        if (main is not null) return main;
        main = Location.Create(companyId, Location.DefaultCode, "Main warehouse");
        await locations.AddAsync(main, ct);
        await locations.SaveChangesAsync(ct);
        return main;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private async Task<StockLedgerEntryResponse> MapAsync(ItemLedgerEntry entry, CancellationToken ct)
    {
        var item = await items.GetAsync(entry.CompanyId, entry.ItemId, ct);
        return Map(entry, item?.Sku ?? string.Empty);
    }

    private static StockLedgerEntryResponse Map(ItemLedgerEntry e, string sku) => new(
        e.Id.Value, e.ItemId.Value, sku, e.LocationId.Value, e.Quantity, e.UnitCost,
        e.PostingDate, e.PostedAt, e.SourceType, e.SourceReference);
}
