using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Sales.Application;

/// <summary>
/// Order-to-cash posting. Invoicing issues stock (standard cost) and posts one GL journal
/// (Dr Receivable / Cr Revenue, Dr COGS / Cr Inventory). Modules own separate databases (ADR 0002), so
/// a failure after an earlier step compensates it (stock returned, journal reversed) rather than relying on one transaction.
/// </summary>
public sealed class InvoiceService(
    ISalesStore store,
    IInventoryStock inventory,
    IFinancePosting finance,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IInvoiceService
{
    public const string InvoiceSequence = "INV";
    public const string PaymentSequence = "PAY";

    public async Task<InvoiceResponse?> CreateFromOrderAsync(SalesOrderId orderId, CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<InvoiceResponse?>(companyId, $"order:{orderId.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, orderId, ct);
            if (order is null) return null;
            if (order.Status != SalesOrderStatus.Released)
                throw new DomainException("Only released sales orders can be invoiced.");
            var customer = await store.GetCustomerAsync(companyId, order.CustomerId, ct)
                ?? throw new DomainException("The customer was not found.");

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(InvoiceSequence, "INV-", ct);

            var issued = new List<(SalesOrderLine Line, StockMovementResult Result)>();
            PostedJournal? journal = null;
            SalesInvoice invoice;
            try
            {
                foreach (var line in order.Lines)
                {
                    var result = await inventory.IssueAsync(
                        new StockMovementCommand(line.ItemId, line.Quantity, date, "SalesInvoice", number), ct);
                    issued.Add((line, result));
                }

                var revenue = order.Total;
                var cost = decimal.Round(issued.Sum(x => x.Result.TotalCost), 2);
                var lines = new List<PostJournalLine>
                {
                    new(SalesAccounts.Receivable, revenue, 0, $"Invoice {number}"),
                    new(SalesAccounts.Revenue, 0, revenue, $"Invoice {number}")
                };
                if (cost > 0)
                {
                    lines.Add(new(SalesAccounts.CostOfGoodsSold, cost, 0, $"COGS {number}"));
                    lines.Add(new(SalesAccounts.Inventory, 0, cost, $"COGS {number}"));
                }

                journal = await finance.PostJournalAsync(new PostJournalCommand(
                    "Sales", number, date, $"Sales invoice {number} - {customer.Name}", lines), ct);

                var invoiceLines = issued
                    .Select(x => new SalesInvoiceLine(x.Line.ItemId, x.Line.Sku, x.Line.Description, x.Line.Quantity, x.Line.UnitPrice, x.Result.UnitCost))
                    .ToArray();
                invoice = SalesInvoice.Create(companyId, number, order, date, customer.PaymentTermsDays,
                    invoiceLines, journal.JournalId, journal.Number, clock.UtcNow);
                order.MarkInvoiced(invoice.Id);
                await store.AddInvoiceAsync(invoice, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await CompensateAsync(issued, journal, number, ct);
                throw;
            }

            // Committed from here on: never compensate for a failure after this point.
            await audit.RecordAsync("Sales", "invoice.created", "SalesInvoice", invoice.Id.Value.ToString(), invoice.Number, true,
                new Dictionary<string, object?> { ["Order"] = order.Number, ["Total"] = invoice.Total, ["Journal"] = journal.Number }, ct);
            return Map(invoice, customer.Name, CreditedQuantities([]));
        }, cancellationToken);
    }

    public async Task<PaymentResponse?> RecordPaymentAsync(SalesInvoiceId id, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<PaymentResponse?>(companyId, $"invoice:{id.Value}", async ct =>
        {
            var invoice = await store.GetInvoiceAsync(companyId, id, ct);
            if (invoice is null) return null;
            var customer = await store.GetCustomerAsync(companyId, invoice.CustomerId, ct);
            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            if (request.Amount <= 0) throw new DomainException("Payment amount must be positive.");
            if (request.Amount > invoice.Balance) throw new DomainException($"Payment exceeds the invoice balance of {invoice.Balance:N2}.");
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(PaymentSequence, "PAY-", ct);

            var account = request.Method == PaymentMethod.Cash ? SalesAccounts.Cash : SalesAccounts.Bank;
            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Sales", number, date, $"Payment {number} for invoice {invoice.Number}",
                [
                    new PostJournalLine(account, request.Amount, 0, $"Payment {number}"),
                    new PostJournalLine(SalesAccounts.Receivable, 0, request.Amount, $"Invoice {invoice.Number}")
                ]), ct);
            Payment payment;
            try
            {
                invoice.ApplyPayment(request.Amount);
                payment = Payment.Create(companyId, number, invoice.Id, invoice.CustomerId, date, request.Amount,
                    request.Method, request.Reference, journal.Number, clock.UtcNow);
                await store.AddPaymentAsync(payment, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: payment {number} failed to save", ct);
                throw;
            }

            await audit.RecordAsync("Sales", "payment.recorded", "Payment", payment.Id.Value.ToString(), payment.Number, true,
                new Dictionary<string, object?> { ["Invoice"] = invoice.Number, ["Amount"] = payment.Amount }, ct);
            return MapPayment(payment, invoice.Number, customer?.Name ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var invoices = await store.ListInvoicesAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var names = (await store.ListCustomersAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var credited = CreditedQuantities(await store.ListCreditNotesAsync(companyId, 500, cancellationToken));
        return invoices.Select(i => Map(i, names.GetValueOrDefault(i.CustomerId, string.Empty), credited)).ToArray();
    }

    public async Task<InvoiceResponse?> GetAsync(SalesInvoiceId id, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var invoice = await store.GetInvoiceAsync(companyId, id, cancellationToken);
        if (invoice is null) return null;
        var customer = await store.GetCustomerAsync(companyId, invoice.CustomerId, cancellationToken);
        var credited = CreditedQuantities(await store.ListCreditNotesForInvoiceAsync(companyId, invoice.Id, cancellationToken));
        return Map(invoice, customer?.Name ?? string.Empty, credited);
    }

    public async Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var payments = await store.ListPaymentsAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var customers = (await store.ListCustomersAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var invoices = (await store.ListInvoicesAsync(companyId, 500, cancellationToken)).ToDictionary(x => x.Id, x => x.Number);
        return payments.Select(p => MapPayment(
            p, invoices.GetValueOrDefault(p.InvoiceId, string.Empty), customers.GetValueOrDefault(p.CustomerId, string.Empty))).ToArray();
    }

    private async Task CompensateAsync(
        List<(SalesOrderLine Line, StockMovementResult Result)> issued, PostedJournal? journal, string number, CancellationToken ct)
    {
        // Best effort: never let a compensation failure mask the original error.
        try
        {
            if (journal is not null)
                await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: invoice {number} failed to save", ct);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            foreach (var (line, _) in issued)
                await inventory.ReceiveAsync(new StockMovementCommand(line.ItemId, line.Quantity, today, "SalesInvoiceVoid", number), ct);
        }
        catch
        {
            // Left for manual reconciliation; the audit trail and ledgers retain every posting.
        }
    }

    /// <summary>Quantity credited so far, keyed by (invoice, item).</summary>
    internal static IReadOnlyDictionary<(Guid Invoice, Guid Item), decimal> CreditedQuantities(IEnumerable<CreditNote> notes) =>
        notes.SelectMany(n => n.Lines.Select(l => (Invoice: n.SalesInvoiceId.Value, Item: l.ItemId, l.Quantity)))
            .GroupBy(x => (x.Invoice, x.Item))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    private InvoiceResponse Map(SalesInvoice i, string customerName, IReadOnlyDictionary<(Guid Invoice, Guid Item), decimal> credited) => new(
        i.Id.Value, i.Number, i.SalesOrderId.Value, i.CustomerId.Value, customerName, i.InvoiceDate, i.DueDate,
        i.Total, i.AmountPaid, i.AmountCredited, i.Balance, i.Status,
        i.Status == InvoiceStatus.Open && i.DueDate < DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
        i.JournalNumber,
        i.Lines.Select(l => new InvoiceLineResponse(
            l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, l.LineTotal, credited.GetValueOrDefault((i.Id.Value, l.ItemId)))).ToArray());

    private static PaymentResponse MapPayment(Payment p, string invoiceNumber, string customerName) => new(
        p.Id.Value, p.Number, p.InvoiceId.Value, invoiceNumber, p.CustomerId.Value, customerName,
        p.PaymentDate, p.Amount, p.Method, p.Reference, p.JournalNumber);
}
