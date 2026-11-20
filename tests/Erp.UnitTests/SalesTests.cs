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
}
