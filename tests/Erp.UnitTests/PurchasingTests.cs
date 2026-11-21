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

    [Theory]
    [InlineData(100, 104, 5, true)]       // a looser tolerance accepts what the default would reject
    [InlineData(100, 104, 2, false)]
    [InlineData(100, 100.01, 0, false)]   // zero means exact prices only
    [InlineData(100, 100, 0, true)]
    [InlineData(100, 97, 3, true)]        // inclusive at the boundary, both directions
    public void Tolerance_can_be_set_per_line(double order, double billed, double tolerance, bool expected) =>
        Assert.Equal(expected, ThreeWayMatch.WithinTolerance((decimal)order, (decimal)billed, (decimal)tolerance));

    [Theory]
    [InlineData(1.5, 4.0, 1.5)]       // the item wins over the vendor
    [InlineData(0.0, 4.0, 0.0)]          // an item set to exact prices is not "unset"
    [InlineData(null, 4.0, 4.0)]      // otherwise the vendor
    [InlineData(null, 0.0, 0.0)]
    [InlineData(null, null, 2.0)]     // otherwise the company default
    public void Effective_tolerance_prefers_item_then_vendor_then_default(double? item, double? vendor, double expected) =>
        Assert.Equal((decimal)expected, ThreeWayMatch.EffectiveTolerance((decimal?)item, (decimal?)vendor));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Tolerance_outside_zero_to_one_hundred_is_rejected(double value)
    {
        Assert.Throws<DomainException>(() => ThreeWayMatch.ValidateTolerance((decimal)value));
        Assert.Throws<DomainException>(() => Vendor.Create(new CompanyId(Guid.NewGuid()), "V1", "Vendor", null, null, 30, DateTimeOffset.UtcNow, (decimal)value));
    }

    [Fact]
    public void Vendor_tolerance_is_optional_and_can_be_cleared()
    {
        var vendor = Vendor.Create(new CompanyId(Guid.NewGuid()), "V1", "Vendor", null, null, 30, DateTimeOffset.UtcNow);
        Assert.Null(vendor.PriceTolerancePercent);
        vendor.Update("Vendor", null, null, 30, 5m);
        Assert.Equal(5m, vendor.PriceTolerancePercent);
        vendor.Update("Vendor", null, null, 30, null);
        Assert.Null(vendor.PriceTolerancePercent);
    }

    [Fact]
    public void Bill_line_keeps_the_tolerance_it_was_matched_against()
    {
        var line = new VendorBillLine(ItemA, "A", "Item A", 10m, orderUnitCost: 100m, unitCost: 104m, tolerancePercent: 5m);
        Assert.Equal(5m, line.TolerancePercent);
        Assert.True(line.WithinTolerance);
        Assert.False(new VendorBillLine(ItemA, "A", "Item A", 10m, 100m, 104m).WithinTolerance); // default 2%
    }

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

    // ---- returns to vendor and debit notes ---------------------------------------------------------------------

    /// <summary>10 x A received and billed in full (order cost 25.255, billed at 26) so returns have something to reverse.</summary>
    private static PurchaseOrder ReceivedAndBilled()
    {
        var order = Released();
        order.RecordReceipt(Qty(10m, 4m));
        order.RecordBilling(Qty(10m, 4m));
        return order;
    }

    [Fact]
    public void Returning_unbilled_goods_reduces_what_can_be_billed_and_needs_no_debit_note()
    {
        var order = Released();
        order.RecordReceipt(Qty(10m, 4m));
        order.RecordBilling([(ItemA, 4m)]);                       // 6 of A still unbilled

        order.RecordReturn([(ItemA, 3m)]);                        // taken from the unbilled 6

        var a = order.LineFor(ItemA);
        Assert.Equal(7m, a.NetReceived);
        Assert.Equal(3m, a.ReceivedNotBilled);                    // 7 held - 4 billed
        Assert.Equal(0m, a.DebitableQuantity);                    // nothing billed was returned
        Assert.Throws<DomainException>(() => order.RecordDebit([(ItemA, 1m)]));
    }

    [Fact]
    public void Returning_billed_goods_makes_them_debitable_and_a_debit_note_clears_it()
    {
        var order = ReceivedAndBilled();
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);

        order.RecordReturn([(ItemA, 3m)]);

        var a = order.LineFor(ItemA);
        Assert.Equal(3m, a.DebitableQuantity);                    // billed 10, now hold 7
        Assert.Equal(0m, a.ReceivedNotBilled);
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);

        order.RecordDebit([(ItemA, 3m)]);
        Assert.Equal(0m, a.DebitableQuantity);
        Assert.Equal(7m, a.NetBilled);
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);
    }

    [Fact]
    public void Debit_is_limited_to_returned_billed_goods_and_is_atomic()
    {
        var order = ReceivedAndBilled();
        order.RecordReturn([(ItemA, 2m)]);

        Assert.Throws<DomainException>(() => order.RecordDebit([(ItemA, 3m)]));              // only 2 were returned
        Assert.Throws<DomainException>(() => order.RecordDebit([(ItemB, 1m)]));              // B was never returned
        Assert.Throws<DomainException>(() => order.RecordDebit([(ItemA, 1m), (ItemB, 1m)])); // second line invalid
        Assert.Equal(0m, order.LineFor(ItemA).QuantityDebited);                              // nothing applied
        Assert.Throws<DomainException>(() => order.RecordDebit([]));
    }

    [Fact]
    public void Returns_are_limited_to_the_goods_still_held_and_validated_atomically()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 6m)]);

        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemA, 7m)]));             // only 6 received
        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemB, 1m)]));             // B not received
        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemA, 0m)]));
        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemA, 1m), (ItemA, 1m)]));

        order.RecordReturn([(ItemA, 4m)]);
        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemA, 3m)]));             // only 2 still held
        Assert.Equal(2m, order.LineFor(ItemA).NetReceived);
    }

    [Fact]
    public void Nothing_can_be_returned_before_receipt_and_a_return_does_not_reopen_the_order()
    {
        var order = Released();
        Assert.Throws<DomainException>(() => order.RecordReturn([(ItemA, 1m)]));

        order.RecordReceipt([(ItemA, 10m)]);
        order.RecordReturn([(ItemA, 4m)]);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);                   // B still outstanding
        Assert.Equal(0m, order.LineFor(ItemA).OutstandingToReceive);                         // returned goods are not re-expected
        Assert.Throws<DomainException>(() => order.RecordReceipt([(ItemA, 1m)]));
    }

    [Fact]
    public void Returning_unbilled_goods_can_complete_an_otherwise_fully_billed_order()
    {
        var order = Released();
        order.RecordReceipt(Qty(10m, 4m));
        order.RecordBilling(Qty(7m, 4m));                                                    // 3 of A not billed yet
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);

        order.RecordReturn([(ItemA, 3m)]);                                                   // held 7 == billed 7
        Assert.Equal(PurchaseOrderStatus.Billed, order.Status);
    }

    [Fact]
    public void Billing_after_a_return_covers_only_the_goods_still_held()
    {
        var order = Released();
        order.RecordReceipt([(ItemA, 10m)]);
        order.RecordReturn([(ItemA, 4m)]);

        Assert.Throws<DomainException>(() => order.RecordBilling([(ItemA, 7m)]));            // 6 held
        order.RecordBilling([(ItemA, 6m)]);
        Assert.Equal(0m, order.LineFor(ItemA).ReceivedNotBilled);
    }

    // ---- documents ----------------------------------------------------------------------------------------------

    private static DebitNote Note(VendorBill bill, decimal quantity, decimal orderCost, decimal billedCost, decimal applied, string reason = "Faulty goods returned") =>
        DebitNote.Create(Company, "DN-1", bill, new DateOnly(2026, 11, 20), reason,
            [new DebitNoteLine(ItemA, "A", "Item A", quantity, orderCost, billedCost)], Guid.NewGuid(), "JE-5", applied, DateTimeOffset.UtcNow);

    [Fact]
    public void Debit_note_splits_into_accrual_and_price_variance_at_the_billed_price()
    {
        var bill = Bill(10m, 25m, 26m);                          // total 260
        var note = Note(bill, 2m, 25m, 26m, applied: 52m);

        Assert.Equal(52m, note.Total);                           // the vendor credits what it billed
        Assert.Equal(50m, note.AccruedTotal);                    // GRNI is cleared at the order price
        Assert.Equal(2m, note.PriceVariance);
        Assert.Equal(0m, note.RefundDue);
    }

    [Fact]
    public void Debit_note_requires_a_reason_lines_and_a_valid_applied_amount()
    {
        var bill = Bill();
        var line = new DebitNoteLine(ItemA, "A", "Item A", 1m, 25m, 26m);

        Assert.Throws<DomainException>(() => DebitNote.Create(Company, "DN-1", bill, new DateOnly(2026, 11, 20), " ", [line], Guid.NewGuid(), "JE", 26m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => DebitNote.Create(Company, "DN-1", bill, new DateOnly(2026, 11, 20), "x", [], Guid.NewGuid(), "JE", 0m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => DebitNote.Create(Company, "DN-1", bill, new DateOnly(2026, 11, 20), "x", [line], Guid.NewGuid(), "JE", 26.01m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new DebitNoteLine(ItemA, "A", "Item A", 0m, 25m, 26m));
    }

    [Fact]
    public void Debit_reduces_the_bill_balance_and_a_fully_debited_unpaid_bill_becomes_debited()
    {
        var bill = Bill(10m, 25m, 26m);                          // 260
        bill.ApplyDebit(60m);
        Assert.Equal(200m, bill.Balance);
        Assert.Equal(BillStatus.Open, bill.Status);

        bill.ApplyDebit(200m);
        Assert.Equal(0m, bill.Balance);
        Assert.Equal(BillStatus.Debited, bill.Status);
    }

    [Fact]
    public void Debit_that_settles_a_part_paid_bill_marks_it_paid_and_payments_respect_debits()
    {
        var bill = Bill(10m, 25m, 26m);
        bill.ApplyPayment(200m);
        bill.ApplyDebit(60m);                                                                 // 260 - 200 paid = 60 open
        Assert.Equal(BillStatus.Paid, bill.Status);

        var open = Bill(10m, 25m, 26m);
        open.ApplyDebit(60m);
        Assert.Throws<DomainException>(() => open.ApplyPayment(200.01m));                    // only 200 is still open
        Assert.Throws<DomainException>(() => open.ApplyDebit(200.01m));
        Assert.Throws<DomainException>(() => open.ApplyDebit(-1m));
    }

    [Fact]
    public void Debit_on_an_already_paid_bill_applies_nothing_and_is_fully_refundable_by_the_vendor()
    {
        var bill = Bill(10m, 25m, 26m);
        bill.ApplyPayment(260m);
        var note = Note(bill, 2m, 25m, 26m, applied: 0m);

        Assert.Equal(52m, note.RefundDue);
        note.ApplyRefund(20m);
        note.ApplyRefund(32m);
        Assert.Equal(0m, note.RefundDue);
        Assert.Throws<DomainException>(() => note.ApplyRefund(0.01m));
        Assert.Throws<DomainException>(() => Note(bill, 2m, 25m, 26m, 0m).ApplyRefund(0m));
    }

    [Fact]
    public void Vendor_return_requires_a_reason_and_lines_and_totals_at_the_order_cost()
    {
        var order = Released();
        Assert.Throws<DomainException>(() => VendorReturn.Create(Company, "RTV-1", order.Id, order.VendorId, new DateOnly(2026, 11, 20), "", null, [new VendorReturnLine(ItemA, "A", "A", 1m, 25m)], Guid.NewGuid(), "JE", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => VendorReturn.Create(Company, "RTV-1", order.Id, order.VendorId, new DateOnly(2026, 11, 20), "damaged", null, [], Guid.NewGuid(), "JE", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new VendorReturnLine(ItemA, "A", "A", 0m, 25m));

        var ret = VendorReturn.Create(Company, "RTV-1", order.Id, order.VendorId, new DateOnly(2026, 11, 20), " damaged ", " RMA-9 ",
            [new VendorReturnLine(ItemA, "A", "A", 4m, 25.255m)], Guid.NewGuid(), "JE-6", DateTimeOffset.UtcNow);
        Assert.Equal(101.02m, ret.Total);
        Assert.Equal("damaged", ret.Reason);
        Assert.Equal("RMA-9", ret.Reference);
    }

    [Fact]
    public void Vendor_refund_requires_a_positive_amount() =>
        Assert.Throws<DomainException>(() => VendorRefund.Create(
            Company, "VRFD-1", new DebitNoteId(Guid.NewGuid()), VendorRef, new DateOnly(2026, 11, 20), 0m, PaymentMethod.Bank, null, "JE-7", DateTimeOffset.UtcNow));
}
