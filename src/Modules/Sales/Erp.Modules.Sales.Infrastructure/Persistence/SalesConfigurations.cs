using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Sales.Infrastructure.Persistence;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new CustomerId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("SalesOrders", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new SalesOrderId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.CustomerId).HasConversion(x => x.Value, x => new CustomerId(x)).IsRequired();
        b.Property(x => x.InvoiceId).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new SalesInvoiceId(x.Value) : null);
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("SalesOrderLines", "sales");
            l.WithOwner().HasForeignKey("SalesOrderId");
            l.Property<int>("LineNo");
            l.HasKey("SalesOrderId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitPrice).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
    }
}

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> b)
    {
        b.ToTable("SalesInvoices", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new SalesInvoiceId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.SalesOrderId).HasConversion(x => x.Value, x => new SalesOrderId(x)).IsRequired();
        b.Property(x => x.CustomerId).HasConversion(x => x.Value, x => new CustomerId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.AmountPaid).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("SalesInvoiceLines", "sales");
            l.WithOwner().HasForeignKey("SalesInvoiceId");
            l.Property<int>("LineNo");
            l.HasKey("SalesInvoiceId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitPrice).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.Balance);
        b.Ignore(x => x.CostOfGoods);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.SalesOrderId).IsUnique();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new PaymentId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.InvoiceId).HasConversion(x => x.Value, x => new SalesInvoiceId(x)).IsRequired();
        b.Property(x => x.CustomerId).HasConversion(x => x.Value, x => new CustomerId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.InvoiceId);
    }
}
