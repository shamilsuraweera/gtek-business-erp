using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class PurchasingTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly VendorId VendorRef = new(Guid.NewGuid());
    private static readonly Guid ItemA = Guid.NewGuid();
    private static readonly Guid ItemB = Guid.NewGuid();

    /// <summary>A released order: 10 x A @ 25.255 and 4 x B @ 100.</summary>
    private static PurchaseOrder Released()
    {
        var order = PurchaseOrder.Create(Company, "PO-1", VendorRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        order.AddLine(ItemA, "A", "Item A", 10m, 25.255m);
        order.AddLine(ItemB, "B", "Item B", 4m, 100m);
        order.Open();
        order.Release();
        return order;
    }

    private static (Guid, decimal)[] Qty(decimal a, decimal b) => [(ItemA, a), (ItemB, b)];

    // ---- lifecycle -------------------------------------------------------------------------------------------

    [Fact]
    public void Order_total_rounds_each_line() => Assert.Equal(252.55m + 400m, Released().Total);

    [Fact]
    public void Same_item_cannot_appear_twice()
    {
        var order = PurchaseOrder.Create(Company, "PO-2", VendorRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        order.AddLine(ItemA, "A", "Item A", 1m, 1m);
        Assert.Throws<DomainException>(() => order.AddLine(ItemA, "A", "Item A", 2m, 1m));
    }

    [Fact]
    public void Order_cannot_skip_states_and_lines_are_validated()
    {
        var draft = PurchaseOrder.Create(Company, "PO-3", VendorRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(draft.Open);
        Assert.Throws<DomainException>(() => draft.AddLine(ItemA, "A", "A", 0m, 1m));
        Assert.Throws<DomainException>(() => draft.AddLine(ItemA, "A", "A", 1m, -1m));
        draft.AddLine(ItemA, "A", "A", 1m, 1m);
        Assert.Throws<DomainException>(() => draft.RecordReceipt([(ItemA, 1m)]));
        draft.Open();
        Assert.Throws<DomainException>(() => draft.RecordReceipt([(ItemA, 1m)]));
    }

    // ---- partial receipts --------------------------------------------------------------------------------------

    [Fact]
    public void Partial_receipt_moves_order_to_partially_received_and_tracks_outstanding()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 4m)]);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(6m, order.LineFor(ItemA).OutstandingToReceive);
        Assert.Equal(4m, order.LineFor(ItemB).OutstandingToReceive);
        Assert.Equal(4m, order.LineFor(ItemA).QuantityReceived);
    }

    [Fact]
    public void Receipts_accumulate_until_everything_is_in()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 10m), (ItemB, 1m)]);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        order.RecordReceipt([(ItemB, 3m)]);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Empty(order.OutstandingToReceive());
    }

    [Fact]
    public void Over_receipt_unknown_items_and_duplicates_are_rejected_without_changing_anything()
    {
        var order = Released();
        Assert.Throws<DomainException>(() => order.RecordReceipt(Qty(11m, 1m)));
        Assert.Throws<DomainException>(() => order.RecordReceipt([(Guid.NewGuid(), 1m)]));
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 1m), (ItemA, 1m)]));
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 0m)]));
        Assert.Throws<DomainException>(() => order.RecordReceipt([]));

        // A rejected receipt is atomic: the valid first line must not have been applied.
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 5m), (ItemB, 99m)]));
        Assert.Equal(0m, order.LineFor(ItemA).QuantityReceived);
        Assert.Equal(PurchaseOrderStatus.Released, order.Status);
    }

    [Fact]
    public void Fully_received_order_accepts_no_more_goods()
    {
        var order = Released();
        order.RecordReceipt(Qty(10m, 4m));
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 1m)]));
    }

    [Fact]
    public void Close_short_accepts_the_partial_delivery_and_only_applies_to_partial_orders()
    {
        var order = Released();
        Assert.Throws<DomainException>(order.CloseShort);
        order.RecordReceipt([(ItemA, 6m)]);
        order.CloseShort();
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 1m)]));
    }

    [Fact]
    public void Order_with_received_goods_cannot_be_cancelled_but_an_untouched_one_can()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 1m)]);
        Assert.Throws<DomainException>(order.Cancel);

        var fresh = Released();
        fresh.Cancel();
        Assert.Equal(PurchaseOrderStatus.Cancelled, fresh.Status);
        Assert.Throws<DomainException>(fresh.Cancel);
    }

    // ---- billing (quantity leg of the three-way match) ----------------------------------------------------------

    [Fact]
    public void Nothing_can_be_billed_before_it_is_received()
    {
        var order = Released();
        Assert.Throws<DomainException>(() => order.RecordBilling([(ItemA, 1m)]));
    }

    [Fact]
    public void Billing_is_limited_to_received_and_not_yet_billed_quantity()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 6m)]);

        Assert.Throws<DomainException>(() => order.RecordBilling([(ItemA, 7m)]));   // more than received
        order.RecordBilling([(ItemA, 4m)]);
        Assert.Equal(2m, order.LineFor(ItemA).ReceivedNotBilled);
        Assert.Throws<DomainException>(() => order.RecordBilling([(ItemA, 3m)]));   // more than the 2 still unbilled
        Assert.Throws<DomainException>(() => order.RecordBilling([(ItemB, 1m)]));   // B was never received
    }

    [Fact]
    public void Order_is_billed_only_once_fully_received_and_fully_billed()
    {
        var order = Released();
        order.RecordReceipt(Qty(10m, 4m));
        order.RecordBilling([(ItemA, 10m)]);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);

        order.RecordBilling([(ItemB, 4m)]);
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);
    }

    [Fact]
    public void Billing_everything_received_so_far_does_not_close_a_partially_received_order()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 5m)]);
        order.RecordBilling([(ItemA, 5m)]);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
    }

    [Fact]
    public void Closing_short_after_billing_everything_received_completes_the_order()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 5m)]);
        order.RecordBilling([(ItemA, 5m)]);
        order.CloseShort();
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);
    }

    // ---- price leg --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(100, 102, true)]      // exactly at the 2% tolerance
    [InlineData(100, 98, true)]
    [InlineData(100, 102.01, false)]
    [InlineData(100, 97, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    public void Price_tolerance_is_symmetric_and_inclusive(double order, double billed, bool expected) =>
        Assert.Equal(expected, ThreeWayMatch.WithinTolerance((decimal)order, (decimal)billed));

    [Fact]
    public void Variance_percent_is_relative_to_the_order_price() =>
        Assert.Equal(5m, ThreeWayMatch.VariancePercent(200m, 210m));

    [Fact]
    public void Bill_line_splits_the_amount_into_accrual_and_price_variance()
    {
        var line = new VendorBillLine(ItemA, "A", "Item A", 10m, orderUnitCost: 25m, unitCost: 26m);

        Assert.Equal(250m, line.AccruedTotal);
        Assert.Equal(260m, line.LineTotal);
        Assert.Equal(10m, line.PriceVariance);
        Assert.Equal(-5m, new VendorBillLine(ItemA, "A", "A", 10m, 25m, 24.5m).PriceVariance);
        Assert.Throws<DomainException>(() => new VendorBillLine(ItemA, "A", "A", 0m, 25m, 25m));
    }

    // ---- bills and payments -------------------------------------------------------------------------------------

    private static VendorBill Bill(decimal quantity = 10m, decimal orderCost = 25m, decimal billedCost = 26m) =>
        VendorBill.Create(Company, "BILL-1", " V-77 ", Released(), new DateOnly(2026, 11, 10), 45,
            [new VendorBillLine(ItemA, "A", "Item A", quantity, orderCost, billedCost)], Guid.NewGuid(), "JE-2", false, DateTimeOffset.UtcNow);

    [Fact]
    public void Bill_totals_due_date_and_variance()
    {
        var bill = Bill();

        Assert.Equal(260m, bill.Total);
        Assert.Equal(250m, bill.AccruedTotal);
        Assert.Equal(10m, bill.PriceVariance);
        Assert.Equal(new DateOnly(2026, 12, 25), bill.DueDate);
        Assert.Equal("V-77", bill.VendorReference);
        Assert.Equal(bill.Total, bill.Balance);
    }

    [Fact]
    public void Bill_payments_settle_the_balance_and_reject_overpayment()
    {
        var bill = Bill();
        Assert.Throws<DomainException>(() => bill.ApplyPayment(bill.Total + 0.01m));
        Assert.Throws<DomainException>(() => bill.ApplyPayment(0m));

        bill.ApplyPayment(100m);
        Assert.Equal(BillStatus.Open, bill.Status);
        bill.ApplyPayment(bill.Balance);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void Goods_receipt_requires_lines_and_totals_them()
    {
        var order = Released();
        Assert.Throws<DomainException>(() => GoodsReceipt.Create(
            Company, "GRN-1", order.Id, order.VendorId, new DateOnly(2026, 11, 5), null, [], Guid.NewGuid(), "JE-1", DateTimeOffset.UtcNow));

        var receipt = GoodsReceipt.Create(Company, "GRN-1", order.Id, order.VendorId, new DateOnly(2026, 11, 5), " DN-9 ",
            [new GoodsReceiptLine(ItemA, "A", "Item A", 4m, 25.255m)], Guid.NewGuid(), "JE-1", DateTimeOffset.UtcNow);
        Assert.Equal(101.02m, receipt.Total);
        Assert.Equal("DN-9", receipt.Reference);
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
