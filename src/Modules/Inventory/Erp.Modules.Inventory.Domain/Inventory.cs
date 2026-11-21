using Erp.SharedKernel;

namespace Erp.Modules.Inventory.Domain;

public readonly record struct ItemId(Guid Value);
public readonly record struct LocationId(Guid Value);
public readonly record struct ItemLedgerEntryId(Guid Value);

public sealed record UnitOfMeasure(string Code, string Name);

public sealed class Item : AggregateRoot<ItemId>
{
    private Item()
    {
        Sku = string.Empty;
        Name = string.Empty;
        UnitOfMeasure = "EA";
    }

    private Item(ItemId id, CompanyId companyId, string sku, string name, string unitOfMeasure,
        decimal unitPrice, decimal unitCost, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Sku = NormalizeSku(sku);
        Name = NormalizeName(name);
        UnitOfMeasure = NormalizeUom(unitOfMeasure);
        UnitPrice = RequireNonNegative(unitPrice, "Unit price");
        UnitCost = RequireNonNegative(unitCost, "Unit cost");
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Sku { get; private set; }
    public string Name { get; private set; }
    public string UnitOfMeasure { get; private set; }
    /// <summary>Default selling price.</summary>
    public decimal UnitPrice { get; private set; }
    /// <summary>Standard cost used to value stock issues (COGS).</summary>
    public decimal UnitCost { get; private set; }
    /// <summary>The tax code applied when this item is sold or bought; none means the item carries no tax. Prices are tax-exclusive.</summary>
    public string? TaxCode { get; private set; }
    /// <summary>Price tolerance for matching vendor bills of this item to the purchase order; none defers to the vendor, then the company default.</summary>
    public decimal? PriceTolerancePercent { get; private set; }
    /// <summary>How far above the ordered quantity a delivery of this item may go; none defers to the vendor, then to no over-delivery.</summary>
    public decimal? QuantityTolerancePercent { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Item Create(CompanyId companyId, string sku, string name, string unitOfMeasure,
        decimal unitPrice, decimal unitCost, DateTimeOffset createdAt, string? taxCode = null, decimal? priceTolerancePercent = null, decimal? quantityTolerancePercent = null)
    {
        var item = new Item(new ItemId(Guid.NewGuid()), companyId, sku, name, unitOfMeasure, unitPrice, unitCost, createdAt);
        item.TaxCode = NormalizeTaxCode(taxCode);
        item.PriceTolerancePercent = ValidateTolerance(priceTolerancePercent);
        item.QuantityTolerancePercent = ValidateTolerance(quantityTolerancePercent, "Quantity");
        return item;
    }

    public static decimal? ValidateTolerance(decimal? tolerancePercent, string kind = "Price") =>
        tolerancePercent is null ? null
        : tolerancePercent is < 0 or > 100 ? throw new DomainException($"{kind} tolerance must be between 0 and 100%.")
        : decimal.Round(tolerancePercent.Value, 4);

    public static string? NormalizeTaxCode(string? taxCode) =>
        string.IsNullOrWhiteSpace(taxCode) ? null : taxCode.Trim().ToUpperInvariant();

    public static string NormalizeSku(string sku) =>
        string.IsNullOrWhiteSpace(sku) ? throw new DomainException("Item SKU is required.") : sku.Trim().ToUpperInvariant();

    public void Update(string name, string unitOfMeasure, decimal unitPrice, decimal unitCost, string? taxCode = null, decimal? priceTolerancePercent = null, decimal? quantityTolerancePercent = null)
    {
        TaxCode = NormalizeTaxCode(taxCode);
        PriceTolerancePercent = ValidateTolerance(priceTolerancePercent);
        QuantityTolerancePercent = ValidateTolerance(quantityTolerancePercent, "Quantity");
        Name = NormalizeName(name);
        UnitOfMeasure = NormalizeUom(unitOfMeasure);
        UnitPrice = RequireNonNegative(unitPrice, "Unit price");
        UnitCost = RequireNonNegative(unitCost, "Unit cost");
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("Item name is required.") : name.Trim();

    private static string NormalizeUom(string uom) =>
        string.IsNullOrWhiteSpace(uom) ? throw new DomainException("Unit of measure is required.") : uom.Trim().ToUpperInvariant();

    private static decimal RequireNonNegative(decimal value, string label) =>
        value < 0 ? throw new DomainException($"{label} cannot be negative.") : value;
}

public sealed class Location : AggregateRoot<LocationId>
{
    public const string DefaultCode = "MAIN";

    private Location()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Location(LocationId id, CompanyId companyId, string code, string name) : base(id)
    {
        CompanyId = companyId;
        Code = NormalizeCode(code);
        Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("Location name is required.") : name.Trim();
        IsActive = true;
    }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }

    public static Location Create(CompanyId companyId, string code, string name) =>
        new(new LocationId(Guid.NewGuid()), companyId, code, name);

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Location code is required.") : code.Trim().ToUpperInvariant();
}

/// <summary>Append-only stock movement. Positive quantity is a receipt, negative is an issue.</summary>
public sealed record ItemLedgerEntry(
    ItemLedgerEntryId Id,
    CompanyId CompanyId,
    ItemId ItemId,
    LocationId LocationId,
    decimal Quantity,
    decimal UnitCost,
    DateTimeOffset PostedAt,
    DateOnly PostingDate,
    string SourceType,
    string SourceReference)
{
    public static ItemLedgerEntry Create(
        CompanyId companyId, ItemId itemId, LocationId locationId, decimal quantity, decimal unitCost,
        DateTimeOffset postedAt, DateOnly postingDate, string sourceType, string sourceReference)
    {
        if (quantity == 0) throw new DomainException("Stock quantity cannot be zero.");
        if (unitCost < 0) throw new DomainException("Unit cost cannot be negative.");
        return new ItemLedgerEntry(
            new ItemLedgerEntryId(Guid.NewGuid()), companyId, itemId, locationId, quantity, unitCost,
            postedAt, postingDate,
            string.IsNullOrWhiteSpace(sourceType) ? "Adjustment" : sourceType.Trim(),
            sourceReference?.Trim() ?? string.Empty);
    }
}
