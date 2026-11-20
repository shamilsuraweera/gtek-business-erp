using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class SalesTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly CustomerId CustomerRef = new(Guid.NewGuid());

    private static SalesOrder Order(int lines = 1)
    {
        var order = SalesOrder.Create(Company, "SO-1", CustomerRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        for (var i = 0; i < lines; i++) order.AddLine(Guid.NewGuid(), $"SKU-{i}", "Item", 2m, 100.255m);
        return order;
    }

    private static SalesInvoice Invoice(SalesOrder order) => SalesInvoice.Create(
        Company, "INV-1", order, new DateOnly(2026, 11, 2), 30,
        order.Lines.Select(l => new SalesInvoiceLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, 60m)).ToArray(),
        Guid.NewGuid(), "JE-1", DateTimeOffset.UtcNow);

    [Fact]
    public void Order_total_rounds_each_line_to_two_decimals() =>
        Assert.Equal(200.51m, Order().Total);

    [Fact]
    public void Order_follows_draft_open_release_invoiced_lifecycle()
    {
        var order = Order();
        order.Open();
        order.Release();
        order.MarkInvoiced(new SalesInvoiceId(Guid.NewGuid()));

        Assert.Equal(SalesOrderStatus.Invoiced, order.Status);
        Assert.NotNull(order.InvoiceId);
    }

    [Fact]
    public void Order_cannot_skip_states_or_be_invoiced_early()
    {
        var order = Order();
        Assert.Throws<DomainException>(order.Release);
        Assert.Throws<DomainException>(() => order.MarkInvoiced(new SalesInvoiceId(Guid.NewGuid())));
        order.Open();
        Assert.Throws<DomainException>(() => order.MarkInvoiced(new SalesInvoiceId(Guid.NewGuid())));
    }

    [Fact]
    public void Empty_order_cannot_be_opened() =>
        Assert.Throws<DomainException>(Order(0).Open);

    [Fact]
    public void Lines_cannot_be_added_after_opening_and_must_be_valid()
    {
        var order = Order();
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 0m, 1m));
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 1m, -1m));
        order.Open();
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), "X", "X", 1m, 1m));
    }

    [Fact]
    public void Invoiced_order_cannot_be_cancelled()
    {
        var order = Order();
        order.Open();
        order.Release();
        order.MarkInvoiced(new SalesInvoiceId(Guid.NewGuid()));

        Assert.Throws<DomainException>(order.Cancel);
    }

    [Fact]
    public void Invoice_due_date_follows_payment_terms_and_computes_cost()
    {
        var invoice = Invoice(Order());

        Assert.Equal(new DateOnly(2026, 12, 2), invoice.DueDate);
        Assert.Equal(120m, invoice.CostOfGoods);
        Assert.Equal(invoice.Total, invoice.Balance);
    }

    [Fact]
    public void Payments_reduce_balance_and_mark_invoice_paid_when_settled()
    {
        var invoice = Invoice(Order());
        invoice.ApplyPayment(100m);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);
        Assert.Equal(invoice.Total - 100m, invoice.Balance);

        invoice.ApplyPayment(invoice.Balance);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void Overpayment_and_non_positive_payments_are_rejected()
    {
        var invoice = Invoice(Order());
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(invoice.Total + 0.01m));
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(0m));
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(-5m));
    }

    [Fact]
    public void Customer_is_normalized_and_validated()
    {
        var customer = Customer.Create(Company, " c-001 ", " Acme ", " ", null, 30, DateTimeOffset.UtcNow);
        Assert.Equal("C-001", customer.Code);
        Assert.Null(customer.Email);
        Assert.Throws<DomainException>(() => Customer.Create(Company, "", "x", null, null, 30, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => Customer.Create(Company, "c", "x", null, null, 400, DateTimeOffset.UtcNow));
    }

    // ---- credit notes -------------------------------------------------------------------------------------------

    private static (SalesInvoice Invoice, Guid Item) InvoiceOf(decimal quantity = 4m, decimal price = 100m, decimal cost = 60m)
    {
        var item = Guid.NewGuid();
        var order = SalesOrder.Create(Company, "SO-9", CustomerRef, new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        order.AddLine(item, "SKU-X", "Item X", quantity, price);
        order.Open();
        order.Release();
        var invoice = SalesInvoice.Create(Company, "INV-9", order, new DateOnly(2026, 11, 2), 30,
            [new SalesInvoiceLine(item, "SKU-X", "Item X", quantity, price, cost)], Guid.NewGuid(), "JE-9", DateTimeOffset.UtcNow);
        return (invoice, item);
    }

    private static CreditNote Note(SalesInvoice invoice, Guid item, decimal quantity, bool returned, decimal applied) =>
        CreditNote.Create(Company, "CN-1", invoice, new DateOnly(2026, 11, 10), " damaged in transit ",
            [new CreditNoteLine(item, "SKU-X", "Item X", quantity, 100m, 60m, returned)], Guid.NewGuid(), "JE-10", applied, DateTimeOffset.UtcNow);

    [Fact]
    public void Duplicate_items_are_rejected_on_a_sales_order()
    {
        var order = Order();
        var item = Guid.NewGuid();
        order.AddLine(item, "DUP", "Dup", 1m, 1m);
        Assert.Throws<DomainException>(() => order.AddLine(item, "DUP", "Dup", 2m, 1m));
    }

    [Fact]
    public void Credit_note_totals_and_returned_cost_only_count_goods_that_came_back()
    {
        var (invoice, item) = InvoiceOf();

        var returned = Note(invoice, item, 3m, returned: true, applied: 300m);
        Assert.Equal(300m, returned.Total);
        Assert.Equal(180m, returned.ReturnedCost);           // 3 x original cost 60

        var allowance = Note(invoice, item, 3m, returned: false, applied: 300m);
        Assert.Equal(0m, allowance.ReturnedCost);            // price adjustment: goods stay with the customer
    }

    [Fact]
    public void Credit_note_requires_a_reason_lines_and_a_valid_applied_amount()
    {
        var (invoice, item) = InvoiceOf();
        var line = new CreditNoteLine(item, "SKU-X", "Item X", 1m, 100m, 60m, true);

        Assert.Throws<DomainException>(() => CreditNote.Create(Company, "CN-1", invoice, new DateOnly(2026, 11, 10), "  ", [line], Guid.NewGuid(), "JE", 100m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => CreditNote.Create(Company, "CN-1", invoice, new DateOnly(2026, 11, 10), "x", [], Guid.NewGuid(), "JE", 0m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => CreditNote.Create(Company, "CN-1", invoice, new DateOnly(2026, 11, 10), "x", [line], Guid.NewGuid(), "JE", 100.01m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new CreditNoteLine(item, "SKU-X", "Item X", 0m, 100m, 60m, true));
    }

    [Fact]
    public void Credit_reduces_the_invoice_balance_and_fully_credited_unpaid_invoice_becomes_credited()
    {
        var (invoice, _) = InvoiceOf();           // total 400
        invoice.ApplyCredit(100m);
        Assert.Equal(300m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);

        invoice.ApplyCredit(300m);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Credited, invoice.Status);
    }

    [Fact]
    public void Credit_that_settles_a_part_paid_invoice_marks_it_paid()
    {
        var (invoice, _) = InvoiceOf();
        invoice.ApplyPayment(250m);
        invoice.ApplyCredit(150m);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void Credit_cannot_exceed_the_open_balance_and_payments_respect_credits()
    {
        var (invoice, _) = InvoiceOf();
        invoice.ApplyPayment(300m);
        Assert.Throws<DomainException>(() => invoice.ApplyCredit(100.01m));
        Assert.Throws<DomainException>(() => invoice.ApplyCredit(-1m));

        invoice.ApplyCredit(60m);
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(40.01m));   // only 40 is still open
        invoice.ApplyPayment(40m);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void Credit_on_an_already_paid_invoice_applies_nothing_and_is_fully_refundable()
    {
        var (invoice, item) = InvoiceOf();
        invoice.ApplyPayment(400m);                                   // fully paid
        var note = Note(invoice, item, 1m, returned: true, applied: 0m);

        Assert.Equal(100m, note.RefundDue);
        note.ApplyRefund(40m);
        Assert.Equal(60m, note.RefundDue);
        note.ApplyRefund(60m);
        Assert.Equal(0m, note.RefundDue);
        Assert.Throws<DomainException>(() => note.ApplyRefund(0.01m));
    }

    [Fact]
    public void Refund_is_limited_to_the_credit_beyond_what_reduced_the_invoice()
    {
        var (invoice, item) = InvoiceOf();
        invoice.ApplyPayment(350m);                                   // 50 still open
        var note = Note(invoice, item, 1m, returned: false, applied: 50m);   // credit 100: 50 applied, 50 owed

        Assert.Equal(50m, note.RefundDue);
        Assert.Throws<DomainException>(() => note.ApplyRefund(50.01m));
        Assert.Throws<DomainException>(() => note.ApplyRefund(0m));
        note.ApplyRefund(50m);
        Assert.Equal(50m, note.AmountRefunded);
    }

    [Fact]
    public void Refund_record_requires_a_positive_amount() =>
        Assert.Throws<DomainException>(() => CustomerRefund.Create(
            Company, "RFD-1", new CreditNoteId(Guid.NewGuid()), CustomerRef, new DateOnly(2026, 11, 11), 0m, PaymentMethod.Bank, null, "JE-11", DateTimeOffset.UtcNow));
}
