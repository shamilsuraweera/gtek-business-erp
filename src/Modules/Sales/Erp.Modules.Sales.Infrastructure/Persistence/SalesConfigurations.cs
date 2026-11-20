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
        b.Property(x => x.AmountCredited).HasPrecision(19, 4);
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

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("CreditNotes", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new CreditNoteId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.SalesInvoiceId).HasConversion(x => x.Value, x => new SalesInvoiceId(x)).IsRequired();
        b.Property(x => x.CustomerId).HasConversion(x => x.Value, x => new CustomerId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.AmountApplied).HasPrecision(19, 4);
        b.Property(x => x.AmountRefunded).HasPrecision(19, 4);
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("CreditNoteLines", "sales");
            l.WithOwner().HasForeignKey("CreditNoteId");
            l.Property<int>("LineNo");
            l.HasKey("CreditNoteId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitPrice).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Property(x => x.ReturnedToStock).IsRequired();
            l.Ignore(x => x.LineTotal);
            l.Ignore(x => x.ReturnedCost);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.ReturnedCost);
        b.Ignore(x => x.RefundDue);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.SalesInvoiceId);
    }
}

internal sealed class CustomerRefundConfiguration : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> b)
    {
        b.ToTable("Refunds", "sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new RefundId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.CreditNoteId).HasConversion(x => x.Value, x => new CreditNoteId(x)).IsRequired();
        b.Property(x => x.CustomerId).HasConversion(x => x.Value, x => new CustomerId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.CreditNoteId);
    }
}
