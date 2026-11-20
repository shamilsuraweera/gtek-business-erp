using Erp.Modules.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options) : DbContext(options)
{
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();
    public DbSet<GoodsReceipt> Receipts => Set<GoodsReceipt>();
    public DbSet<VendorReturn> Returns => Set<VendorReturn>();
    public DbSet<DebitNote> DebitNotes => Set<DebitNote>();
    public DbSet<VendorRefund> VendorRefunds => Set<VendorRefund>();
    public DbSet<VendorBill> Bills => Set<VendorBill>();
    public DbSet<VendorPayment> Payments => Set<VendorPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("purchasing");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PurchasingDbContext).Assembly);
    }
}
