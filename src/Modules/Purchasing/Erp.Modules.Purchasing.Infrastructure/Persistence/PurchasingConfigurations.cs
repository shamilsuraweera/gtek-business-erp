using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Purchasing.Infrastructure.Persistence;

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("Vendors", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new VendorId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.PriceTolerancePercent).HasPrecision(7, 4);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new PurchaseOrderId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("PurchaseOrderLines", "purchasing");
            l.WithOwner().HasForeignKey("PurchaseOrderId");
            l.Property<int>("LineNo");
            l.HasKey("PurchaseOrderId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Property(x => x.QuantityReceived).HasPrecision(19, 4);
            l.Property(x => x.QuantityBilled).HasPrecision(19, 4);
            l.Property(x => x.QuantityReturned).HasPrecision(19, 4);
            l.Property(x => x.QuantityDebited).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
            l.Ignore(x => x.OutstandingToReceive);
            l.Ignore(x => x.ReceivedNotBilled);
            l.Ignore(x => x.NetReceived);
            l.Ignore(x => x.NetBilled);
            l.Ignore(x => x.DebitableQuantity);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("GoodsReceipts", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new GoodsReceiptId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.PurchaseOrderId).HasConversion(x => x.Value, x => new PurchaseOrderId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("GoodsReceiptLines", "purchasing");
            l.WithOwner().HasForeignKey("GoodsReceiptId");
            l.Property<int>("LineNo");
            l.HasKey("GoodsReceiptId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.PurchaseOrderId);
    }
}

internal sealed class VendorBillConfiguration : IEntityTypeConfiguration<VendorBill>
{
    public void Configure(EntityTypeBuilder<VendorBill> b)
    {
        b.ToTable("VendorBills", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new VendorBillId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.PurchaseOrderId).HasConversion(x => x.Value, x => new PurchaseOrderId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.VendorReference).HasMaxLength(100);
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.AmountPaid).HasPrecision(19, 4);
        b.Property(x => x.AmountDebited).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("VendorBillLines", "purchasing");
            l.WithOwner().HasForeignKey("VendorBillId");
            l.Property<int>("LineNo");
            l.HasKey("VendorBillId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.OrderUnitCost).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Property(x => x.TaxCode).HasMaxLength(20);
            l.Property(x => x.TaxRate).HasPrecision(7, 4);
            l.Property(x => x.TolerancePercent).HasPrecision(7, 4).HasDefaultValue(ThreeWayMatch.PriceTolerancePercent);
            l.Ignore(x => x.WithinTolerance);
            l.Property(x => x.TaxAmount).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
            l.Ignore(x => x.LineGross);
            l.Ignore(x => x.AccruedTotal);
            l.Ignore(x => x.PriceVariance);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Subtotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.AccruedTotal);
        b.Ignore(x => x.PriceVariance);
        b.Ignore(x => x.Balance);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.PurchaseOrderId);
        b.HasIndex(x => new { x.CompanyId, x.VendorId, x.VendorReference });
    }
}

internal sealed class VendorPaymentConfiguration : IEntityTypeConfiguration<VendorPayment>
{
    public void Configure(EntityTypeBuilder<VendorPayment> b)
    {
        b.ToTable("VendorPayments", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new VendorPaymentId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.BillId).HasConversion(x => x.Value, x => new VendorBillId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.BillId);
    }
}

internal sealed class VendorReturnConfiguration : IEntityTypeConfiguration<VendorReturn>
{
    public void Configure(EntityTypeBuilder<VendorReturn> b)
    {
        b.ToTable("VendorReturns", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new VendorReturnId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.PurchaseOrderId).HasConversion(x => x.Value, x => new PurchaseOrderId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("VendorReturnLines", "purchasing");
            l.WithOwner().HasForeignKey("VendorReturnId");
            l.Property<int>("LineNo");
            l.HasKey("VendorReturnId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.PurchaseOrderId);
    }
}

internal sealed class DebitNoteConfiguration : IEntityTypeConfiguration<DebitNote>
{
    public void Configure(EntityTypeBuilder<DebitNote> b)
    {
        b.ToTable("DebitNotes", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new DebitNoteId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.PurchaseOrderId).HasConversion(x => x.Value, x => new PurchaseOrderId(x)).IsRequired();
        b.Property(x => x.VendorBillId).HasConversion(x => x.Value, x => new VendorBillId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.AmountApplied).HasPrecision(19, 4);
        b.Property(x => x.AmountRefunded).HasPrecision(19, 4);
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("DebitNoteLines", "purchasing");
            l.WithOwner().HasForeignKey("DebitNoteId");
            l.Property<int>("LineNo");
            l.HasKey("DebitNoteId", "LineNo");
            l.Property(x => x.ItemId).IsRequired();
            l.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).HasPrecision(19, 4);
            l.Property(x => x.OrderUnitCost).HasPrecision(19, 4);
            l.Property(x => x.UnitCost).HasPrecision(19, 4);
            l.Property(x => x.TaxRate).HasPrecision(7, 4);
            l.Property(x => x.TaxAmount).HasPrecision(19, 4);
            l.Ignore(x => x.LineTotal);
            l.Ignore(x => x.LineGross);
            l.Ignore(x => x.AccruedTotal);
            l.Ignore(x => x.PriceVariance);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.Subtotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.AccruedTotal);
        b.Ignore(x => x.PriceVariance);
        b.Ignore(x => x.RefundDue);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.VendorBillId);
    }
}

internal sealed class VendorRefundConfiguration : IEntityTypeConfiguration<VendorRefund>
{
    public void Configure(EntityTypeBuilder<VendorRefund> b)
    {
        b.ToTable("VendorRefunds", "purchasing");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new VendorRefundId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.DebitNoteId).HasConversion(x => x.Value, x => new DebitNoteId(x)).IsRequired();
        b.Property(x => x.VendorId).HasConversion(x => x.Value, x => new VendorId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200).IsRequired();
        b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => x.DebitNoteId);
    }
}
