using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure.Persistence;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure.Persistence;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.IntegrationTests;

/// <summary>The queries behind the overdue and on-order logic run real SQL (filters, ordering, owned line collections), so they are checked against PostgreSQL.</summary>
public sealed class ReminderQueriesPostgreSqlTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static string Connection() =>
        Environment.GetEnvironmentVariable("ERP_TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException("Set ERP_TEST_DATABASE_CONNECTION to a disposable PostgreSQL database before running PostgreSQL integration tests.");

    // ---- sales ------------------------------------------------------------------------------------------------------

    private static SalesInvoice Invoice(CompanyId company, string number, int invoiceDaysAgo, int terms, bool paid = false)
    {
        var invoiceDate = Today.AddDays(-invoiceDaysAgo);
        var order = SalesOrder.Create(company, "SO-" + number, new CustomerId(Guid.NewGuid()), invoiceDate, null, Now);
        order.AddLine(Guid.NewGuid(), "SKU", "Item", 1m, 100m);
        var invoice = SalesInvoice.Create(company, number, order, invoiceDate, terms,
            order.Lines.Select(l => new SalesInvoiceLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, 0m)).ToArray(), Guid.NewGuid(), "JE-" + number, Now);
        if (paid) invoice.ApplyPayment(invoice.Total);
        return invoice;
    }

    [Fact]
    public async Task Overdue_invoices_are_the_open_ones_past_due_oldest_first_for_this_company_only()
    {
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseNpgsql(Connection()).Options;
        var company = new CompanyId(Guid.NewGuid());
        var other = new CompanyId(Guid.NewGuid());
        await using (var setup = new SalesDbContext(options))
        {
            setup.Invoices.AddRange(
                Invoice(company, "INV-OLDEST", 60, 30),                 // due 30 days ago
                Invoice(company, "INV-RECENT", 35, 30),                 // due 5 days ago
                Invoice(company, "INV-NOT-DUE", 5, 30),                 // due in 25 days
                Invoice(company, "INV-DUE-TODAY", 30, 30),              // due today: not yet overdue
                Invoice(company, "INV-PAID", 60, 30, paid: true),       // overdue date but settled
                Invoice(other, "INV-OTHER", 60, 30));                   // another company's
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var context = new SalesDbContext(options);
            var overdue = await new SalesStore(context).ListOverdueInvoicesAsync(company, Today, default);
            Assert.Equal(["INV-OLDEST", "INV-RECENT"], overdue.Select(x => x.Number));
        }
        finally
        {
            await using var cleanup = new SalesDbContext(options);
            await cleanup.Invoices.Where(x => x.CompanyId == company || x.CompanyId == other).ExecuteDeleteAsync();
        }
    }

    // ---- purchasing -------------------------------------------------------------------------------------------------

    private static PurchaseOrder Order(CompanyId company, string number, Guid item, decimal quantity = 10m, Action<PurchaseOrder>? shape = null)
    {
        var order = PurchaseOrder.Create(company, number, new VendorId(Guid.NewGuid()), Today.AddDays(-90), null, Now);
        order.AddLine(item, "SKU", "Item", quantity, 5m);
        shape?.Invoke(order);
        return order;
    }

    private static VendorBill Bill(CompanyId company, string number, int billDaysAgo, int terms, bool paid = false)
    {
        var order = Order(company, "PO-" + number, Guid.NewGuid(), 10m, o => { o.Open(); o.Release(); });
        var billDate = Today.AddDays(-billDaysAgo);
        var bill = VendorBill.Create(company, number, null, order, billDate, terms,
            order.Lines.Select(l => new VendorBillLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitCost, l.UnitCost)).ToArray(), Guid.NewGuid(), "JE-" + number, false, Now);
        if (paid) bill.ApplyPayment(bill.Total);
        return bill;
    }

    [Fact]
    public async Task Overdue_bills_are_the_open_ones_past_due_oldest_first_for_this_company_only()
    {
        var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseNpgsql(Connection()).Options;
        var company = new CompanyId(Guid.NewGuid());
        var other = new CompanyId(Guid.NewGuid());
        await using (var setup = new PurchasingDbContext(options))
        {
            setup.Bills.AddRange(
                Bill(company, "BILL-OLDEST", 90, 45),                   // due 45 days ago
                Bill(company, "BILL-RECENT", 50, 45),                   // due 5 days ago
                Bill(company, "BILL-NOT-DUE", 10, 45),
                Bill(company, "BILL-PAID", 90, 45, paid: true),
                Bill(other, "BILL-OTHER", 90, 45));
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var context = new PurchasingDbContext(options);
            var overdue = await new PurchasingStore(context).ListOverdueBillsAsync(company, Today, default);
            Assert.Equal(["BILL-OLDEST", "BILL-RECENT"], overdue.Select(x => x.Number));
        }
        finally
        {
            await using var cleanup = new PurchasingDbContext(options);
            await cleanup.Bills.Where(x => x.CompanyId == company || x.CompanyId == other).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Orders_awaiting_goods_are_the_released_and_part_received_ones_with_their_lines()
    {
        var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseNpgsql(Connection()).Options;
        var company = new CompanyId(Guid.NewGuid());
        var other = new CompanyId(Guid.NewGuid());
        var item = Guid.NewGuid();
        await using (var setup = new PurchasingDbContext(options))
        {
            setup.Orders.AddRange(
                Order(company, "PO-DRAFT", item),
                Order(company, "PO-OPEN", item, shape: o => o.Open()),
                Order(company, "PO-RELEASED", item, shape: o => { o.Open(); o.Release(); }),
                Order(company, "PO-PART", item, shape: o => { o.Open(); o.Release(); o.RecordReceipt([(item, 4m)]); }),
                Order(company, "PO-RECEIVED", item, shape: o => { o.Open(); o.Release(); o.RecordReceipt([(item, 10m)]); }),
                Order(other, "PO-OTHER", item, shape: o => { o.Open(); o.Release(); }));
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var context = new PurchasingDbContext(options);
            var awaiting = await new PurchasingStore(context).ListOrdersAwaitingGoodsAsync(company, default);
            Assert.Equal(["PO-PART", "PO-RELEASED"], awaiting.Select(x => x.Number).Order());
            var part = awaiting.Single(x => x.Number == "PO-PART");
            Assert.Equal((10m, 4m), (part.Lines.Single().Quantity, part.Lines.Single().QuantityReceived));      // lines come with the order
        }
        finally
        {
            await using var cleanup = new PurchasingDbContext(options);
            await cleanup.Orders.Where(x => x.CompanyId == company || x.CompanyId == other).ExecuteDeleteAsync();
        }
    }
}
