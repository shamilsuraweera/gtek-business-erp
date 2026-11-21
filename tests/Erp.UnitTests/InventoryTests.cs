using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class InventoryTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());

    [Fact]
    public void Item_normalizes_sku_and_uom_and_starts_active()
    {
        var item = Item.Create(Company, " ab-1 ", "Widget", " ea ", 150m, 90m, DateTimeOffset.UtcNow);

        Assert.Equal("AB-1", item.Sku);
        Assert.Equal("EA", item.UnitOfMeasure);
        Assert.True(item.IsActive);
    }

    [Theory]
    [InlineData("", "Name", 1, 1)]
    [InlineData("SKU", " ", 1, 1)]
    [InlineData("SKU", "Name", -1, 1)]
    [InlineData("SKU", "Name", 1, -1)]
    public void Invalid_items_are_rejected(string sku, string name, int price, int cost) =>
        Assert.Throws<DomainException>(() => Item.Create(Company, sku, name, "EA", price, cost, DateTimeOffset.UtcNow));

    [Fact]
    public void Item_update_changes_price_and_cost_and_validates()
    {
        var item = Item.Create(Company, "S", "N", "EA", 10m, 5m, DateTimeOffset.UtcNow);
        item.Update("New", "box", 20m, 8m);

        Assert.Equal(("New", "BOX", 20m, 8m), (item.Name, item.UnitOfMeasure, item.UnitPrice, item.UnitCost));
        Assert.Throws<DomainException>(() => item.Update("New", "box", -1m, 8m));
    }

    [Fact]
    public void Ledger_entry_rejects_zero_quantity_and_negative_cost()
    {
        var args = (new ItemId(Guid.NewGuid()), new LocationId(Guid.NewGuid()));
        Assert.Throws<DomainException>(() => ItemLedgerEntry.Create(
            Company, args.Item1, args.Item2, 0m, 1m, DateTimeOffset.UtcNow, new DateOnly(2026, 1, 1), "Receipt", "r"));
        Assert.Throws<DomainException>(() => ItemLedgerEntry.Create(
            Company, args.Item1, args.Item2, 1m, -1m, DateTimeOffset.UtcNow, new DateOnly(2026, 1, 1), "Receipt", "r"));
    }

    [Fact]
    public void Location_code_is_normalized() =>
        Assert.Equal("MAIN", Location.Create(Company, " main ", "Main").Code);

    [Fact]
    public void Item_price_tolerance_is_optional_validated_and_updatable()
    {
        var item = Item.Create(Company, "T-1", "Tolerant", "EA", 10m, 5m, DateTimeOffset.UtcNow);
        Assert.Null(item.PriceTolerancePercent);
        item.Update("Tolerant", "EA", 10m, 5m, null, 0m);
        Assert.Equal(0m, item.PriceTolerancePercent); // zero is a real setting (exact prices), not "unset"
        item.Update("Tolerant", "EA", 10m, 5m, null, null);
        Assert.Null(item.PriceTolerancePercent);
        Assert.Throws<DomainException>(() => item.Update("Tolerant", "EA", 10m, 5m, null, 100.5m));
        Assert.Throws<DomainException>(() => Item.Create(Company, "T-2", "N", "EA", 1m, 1m, DateTimeOffset.UtcNow, null, -1m));
    }

    [Fact]
    public void Item_quantity_tolerance_is_optional_validated_and_independent()
    {
        var item = Item.Create(Company, "Q-1", "Q", "EA", 10m, 5m, DateTimeOffset.UtcNow, null, 2m, 5m);
        Assert.Equal(2m, item.PriceTolerancePercent);
        Assert.Equal(5m, item.QuantityTolerancePercent);
        item.Update("Q", "EA", 10m, 5m, null, 2m, 0m);
        Assert.Equal(0m, item.QuantityTolerancePercent);
        item.Update("Q", "EA", 10m, 5m, null, 2m, null);
        Assert.Null(item.QuantityTolerancePercent);
        Assert.Throws<DomainException>(() => item.Update("Q", "EA", 10m, 5m, null, null, 101m));
    }
}
