using Erp.Modules.Sales.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure.Persistence;

public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> Orders => Set<SalesOrder>();
    public DbSet<SalesInvoice> Invoices => Set<SalesInvoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CustomerRefund> Refunds => Set<CustomerRefund>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);
    }
}
