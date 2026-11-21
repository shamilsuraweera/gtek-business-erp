using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class TaxTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());

    // ---- arithmetic ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 18, 18.00)]
    [InlineData(9.99, 8, 0.80)]       // 0.7992 -> 0.80
    [InlineData(0.05, 10, 0.01)]      // 0.005 rounds away from zero
    [InlineData(250, 0, 0)]
    public void Tax_is_rounded_to_cents_away_from_zero(double net, double rate, double expected) =>
        Assert.Equal((decimal)expected, TaxMath.Tax((decimal)net, (decimal)rate));

    [Fact]
    public void Partial_credits_of_a_line_add_up_to_the_tax_charged_on_it()
    {
        // 3 units, 9.99 net, 8% => 0.80 tax. Crediting one unit at a time must not drift by a cent.
        const decimal lineTax = 0.80m;
        var first = TaxMath.Portion(lineTax, 3m, 0m, 0m, 1m);
        var second = TaxMath.Portion(lineTax, 3m, 1m, first, 1m);
        var third = TaxMath.Portion(lineTax, 3m, 2m, first + second, 1m);

        Assert.Equal(lineTax, first + second + third);
        Assert.Equal(0.27m, first);
        Assert.Equal(0.27m, second);
        Assert.Equal(0.26m, third);   // the completing credit takes the remainder
    }

    [Fact]
    public void A_credit_never_takes_more_than_the_tax_left_on_the_line() =>
        Assert.Equal(0.10m, TaxMath.Portion(1.00m, 10m, 5m, 0.90m, 4m));

    // ---- tax codes ----------------------------------------------------------------------------------------------

    [Fact]
    public void Tax_code_is_normalized_and_validated()
    {
        var code = TaxCode.Create(Company, " vat18 ", " VAT 18% ", 18m, DateTimeOffset.UtcNow);
        Assert.Equal("VAT18", code.Code);
        Assert.Equal("VAT 18%", code.Name);
        Assert.True(code.IsActive);
        Assert.Throws<DomainException>(() => TaxCode.Create(Company, "", "x", 5m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => TaxCode.Create(Company, "x", "", 5m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => TaxCode.Create(Company, "x", "x", -1m, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => TaxCode.Create(Company, "x", "x", 100.01m, DateTimeOffset.UtcNow));
        Assert.Equal(0m, TaxCode.Create(Company, "ZERO", "Zero rated", 0m, DateTimeOffset.UtcNow).RatePercent);
    }

    [Fact]
    public void Tax_roles_default_to_a_liability_and_an_asset_account()
    {
        var payable = AccountRoleCatalog.Get(AccountRole.TaxPayable);
        var receivable = AccountRoleCatalog.Get(AccountRole.TaxReceivable);
        Assert.Equal([AccountType.Liability], payable.AllowedTypes);
        Assert.Equal([AccountType.Asset], receivable.AllowedTypes);
        Assert.NotEqual(payable.DefaultCode, receivable.DefaultCode);
    }

    // ---- sales invoice and credit note ---------------------------------------------------------------------------

    private static SalesInvoice TaxedInvoice(Guid item)
    {
        var order = SalesOrder.Create(Company, "SO-T", new CustomerId(Guid.NewGuid()), new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        order.AddLine(item, "SKU-T", "Taxed", 4m, 100m);
        order.Open();
        order.Release();
        return SalesInvoice.Create(Company, "INV-T", order, new DateOnly(2026, 11, 2), 30,
            [new SalesInvoiceLine(item, "SKU-T", "Taxed", 4m, 100m, 60m, "VAT18", 18m, TaxMath.Tax(400m, 18m))],
            Guid.NewGuid(), "JE-T", DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Invoice_total_is_subtotal_plus_tax_and_the_customer_owes_the_gross()
    {
        var invoice = TaxedInvoice(Guid.NewGuid());

        Assert.Equal(400m, invoice.Subtotal);
        Assert.Equal(72m, invoice.TaxTotal);
        Assert.Equal(472m, invoice.Total);
        Assert.Equal(472m, invoice.Balance);
        invoice.ApplyPayment(472m);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void Untaxed_lines_leave_the_invoice_exactly_as_before()
    {
        var line = new SalesInvoiceLine(Guid.NewGuid(), "S", "d", 2m, 50m, 30m);
        Assert.Null(line.TaxCode);
        Assert.Equal(0m, line.TaxAmount);
        Assert.Equal(line.LineTotal, line.LineGross);
    }

    [Fact]
    public void Credit_note_carries_its_share_of_tax_and_can_clear_the_whole_gross_invoice()
    {
        var item = Guid.NewGuid();
        var invoice = TaxedInvoice(item);
        var tax = TaxMath.Portion(72m, 4m, 0m, 0m, 4m);
        var note = CreditNote.Create(Company, "CN-T", invoice, new DateOnly(2026, 11, 10), "returned",
            [new CreditNoteLine(item, "SKU-T", "Taxed", 4m, 100m, 60m, true, 18m, tax)],
            Guid.NewGuid(), "JE-C", invoice.Balance, DateTimeOffset.UtcNow);
        invoice.ApplyCredit(note.AmountApplied);

        Assert.Equal(400m, note.Subtotal);
        Assert.Equal(72m, note.TaxTotal);
        Assert.Equal(472m, note.Total);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Credited, invoice.Status);
    }

    // ---- vendor bill and debit note ------------------------------------------------------------------------------

    private static VendorBill TaxedBill(Guid item, decimal orderCost, decimal billedCost)
    {
        var order = PurchaseOrder.Create(Company, "PO-T", new VendorId(Guid.NewGuid()), new DateOnly(2026, 11, 1), null, DateTimeOffset.UtcNow);
        order.AddLine(item, "SKU-T", "Taxed", 10m, orderCost);
        order.Open();
        order.Release();
        var net = decimal.Round(10m * billedCost, 2);
        return VendorBill.Create(Company, "BILL-T", "V-1", order, new DateOnly(2026, 11, 3), 30,
            [new VendorBillLine(item, "SKU-T", "Taxed", 10m, orderCost, billedCost, "VAT18", 18m, TaxMath.Tax(net, 18m))],
            Guid.NewGuid(), "JE-B", false, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Bill_tax_is_charged_on_the_billed_price_and_variance_excludes_it()
    {
        var bill = TaxedBill(Guid.NewGuid(), orderCost: 25m, billedCost: 26m);

        Assert.Equal(260m, bill.Subtotal);
        Assert.Equal(46.80m, bill.TaxTotal);
        Assert.Equal(306.80m, bill.Total);
        Assert.Equal(250m, bill.AccruedTotal);
        Assert.Equal(10m, bill.PriceVariance);      // billed 260 vs order 250, tax excluded
        Assert.Equal(306.80m, bill.Balance);
    }

    [Fact]
    public void Debit_note_returns_input_tax_and_can_clear_the_gross_bill()
    {
        var item = Guid.NewGuid();
        var bill = TaxedBill(item, 25m, 26m);
        var tax = TaxMath.Portion(46.80m, 10m, 0m, 0m, 10m);
        var note = DebitNote.Create(Company, "DN-T", bill, new DateOnly(2026, 11, 12), "faulty",
            [new DebitNoteLine(item, "SKU-T", "Taxed", 10m, 25m, 26m, 18m, tax)], Guid.NewGuid(), "JE-D", bill.Balance, DateTimeOffset.UtcNow);
        bill.ApplyDebit(note.AmountApplied);

        Assert.Equal(260m, note.Subtotal);
        Assert.Equal(46.80m, note.TaxTotal);
        Assert.Equal(306.80m, note.Total);
        Assert.Equal(10m, note.PriceVariance);
        Assert.Equal(0m, bill.Balance);
        Assert.Equal(BillStatus.Debited, bill.Status);
    }
}
