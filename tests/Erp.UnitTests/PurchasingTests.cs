using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class PurchasingTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly VendorId VendorRef = new(Guid.NewGuid());

    private static PurchaseOrder Order(int lines = 1)
    {
        var order = PurchaseOrder.Create(Company, "PO-1", VendorRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        for (var i = 0; i < lines; i++) order.AddLine(Guid.NewGuid(), $"SKU-{i}", "Item", 10m, 25.255m);
        return order;
    }

    private static PurchaseOrder Received()
    {
        var order = Order();
        order.Open();
        order.Release();
        order.MarkReceived(new DateOnly(2026, 11, 5), "JE-1");
        return order;
    }

    [Fact]
    public void Order_total_rounds_each_line()
    {
        Assert.Equal(252.55m, Order().Total);
        Assert.Equal(505.10m, Order(2).Total);
    }

    [Fact]
    public void Order_follows_the_procure_to_pay_lifecycle()
    {
        var order = Received();
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal("JE-1", order.ReceiptJournalNumber);

        order.MarkBilled(new VendorBillId(Guid.NewGuid()));
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);
        Assert.NotNull(order.BillId);
    }

    [Fact]
    public void Order_cannot_skip_states()
    {
        var order = Order();
        Assert.Throws<DomainException>(() => order.MarkReceived(new DateOnly(2026, 11, 5), "JE-1"));
        order.Open();
        Assert.Throws<DomainException>(() => order.MarkReceived(new DateOnly(2026, 11, 5), "JE-1"));
        order.Release();
        Assert.Throws<DomainException>(() => order.MarkBilled(new VendorBillId(Guid.NewGuid())));
    }

    [Fact]
    public void Empty_order_cannot_be_opened_and_lines_are_validated()
    {
        Assert.Throws<DomainException>(Order(0).Open);
        var order = Order();
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 0m, 1m));
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 1m, -1m));
        order.Open();
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 1m, 1m));
    }

    [Fact]
    public void Received_or_billed_orders_cannot_be_cancelled()
    {
        Assert.Throws<DomainException>(Received().Cancel);
        var draft = Order();
        draft.Cancel();
        Assert.Equal(PurchaseOrderStatus.Cancelled, draft.Status);
        Assert.Throws<DomainException>(draft.Cancel);
    }

    [Fact]
    public void Bill_copies_order_lines_and_computes_due_date()
    {
        var order = Received();
        var bill = VendorBill.Create(Company, "BILL-1", " V-77 ", order, new DateOnly(2026, 11, 10), 45, Guid.NewGuid(), "JE-2", DateTimeOffset.UtcNow);

        Assert.Equal(order.Total, bill.Total);
        Assert.Equal(new DateOnly(2026, 12, 25), bill.DueDate);
        Assert.Equal("V-77", bill.VendorReference);
        Assert.Equal(bill.Total, bill.Balance);
    }

    [Fact]
    public void Bill_payments_settle_the_balance_and_reject_overpayment()
    {
        var bill = VendorBill.Create(Company, "BILL-1", null, Received(), new DateOnly(2026, 11, 10), 30, Guid.NewGuid(), "JE-2", DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => bill.ApplyPayment(bill.Total + 0.01m));
        Assert.Throws<DomainException>(() => bill.ApplyPayment(0m));

        bill.ApplyPayment(100m);
        Assert.Equal(BillStatus.Open, bill.Status);
        bill.ApplyPayment(bill.Balance);
        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal(0m, bill.Balance);
    }

    [Fact]
    public void Vendor_is_normalized_and_validated()
    {
        var vendor = Vendor.Create(Company, " v-001 ", " Acme Supplies ", "", null, 30, DateTimeOffset.UtcNow);
        Assert.Equal("V-001", vendor.Code);
        Assert.Null(vendor.Email);
        Assert.Throws<DomainException>(() => Vendor.Create(Company, "", "x", null, null, 30, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => Vendor.Create(Company, "v", "x", null, null, -1, DateTimeOffset.UtcNow));
    }
}
