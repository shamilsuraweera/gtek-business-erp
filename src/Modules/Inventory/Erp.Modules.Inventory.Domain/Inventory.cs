using Erp.SharedKernel;

namespace Erp.Modules.Inventory.Domain;

public readonly record struct ItemId(Guid Value);
public readonly record struct LocationId(Guid Value);
public readonly record struct ItemLedgerEntryId(Guid Value);
public sealed record Item(ItemId Id, CompanyId CompanyId, string Sku, string Name);
public sealed record Location(LocationId Id, CompanyId CompanyId, string Code, string Name);
public sealed record UnitOfMeasure(string Code, string Name);
public sealed record ItemLedgerEntry(ItemLedgerEntryId Id, CompanyId CompanyId, ItemId ItemId, LocationId LocationId, decimal Quantity, DateTimeOffset PostedAt, string SourceReference);
