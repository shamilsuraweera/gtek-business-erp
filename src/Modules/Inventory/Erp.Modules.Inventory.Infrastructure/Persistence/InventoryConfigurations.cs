using Erp.Modules.Inventory.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Inventory.Infrastructure.Persistence;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("Items", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new ItemId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.UnitOfMeasure).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
        b.Property(x => x.UnitCost).HasPrecision(19, 4);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.PriceTolerancePercent).HasPrecision(7, 4);
        b.Property(x => x.QuantityTolerancePercent).HasPrecision(7, 4);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Sku }).IsUnique();
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("Locations", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new LocationId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ItemLedgerEntryConfiguration : IEntityTypeConfiguration<ItemLedgerEntry>
{
    public void Configure(EntityTypeBuilder<ItemLedgerEntry> b)
    {
        b.ToTable("ItemLedgerEntries", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new ItemLedgerEntryId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.ItemId).HasConversion(x => x.Value, x => new ItemId(x)).IsRequired();
        b.Property(x => x.LocationId).HasConversion(x => x.Value, x => new LocationId(x)).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitCost).HasPrecision(19, 4);
        b.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceReference).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.LocationId });
    }
}
