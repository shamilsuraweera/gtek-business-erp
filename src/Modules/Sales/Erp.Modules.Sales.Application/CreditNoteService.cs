using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Sales.Application;

/// <summary>
/// Credit notes and customer refunds. A credit note reverses revenue (Dr Sales Returns / Cr Receivable) and, for goods that
/// came back, stock and cost of sales at the original issue cost (Dr Inventory / Cr COGS). The credit first reduces the invoice
/// balance; anything beyond it is owed to the customer and settled with a refund (Dr Receivable / Cr Cash or Bank).
/// Modules own separate databases (ADR 0002), so a failed step compensates the earlier ones.
/// </summary>
public sealed class CreditNoteService(
    ISalesStore store,
    IInventoryStock inventory,
    IFinancePosting finance,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : ICreditNoteService
{
    public const string CreditNoteSequence = "CN";
    public const string RefundSequence = "RFD";

    public async Task<CreditNoteResponse?> CreateAsync(SalesInvoiceId invoiceId, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        // Same lock key as payments, so a credit cannot race a payment on the same invoice.
        return await store.InLockedTransactionAsync<CreditNoteResponse?>(companyId, $"invoice:{invoiceId.Value}", async ct =>
        {
            var invoice = await store.GetInvoiceAsync(companyId, invoiceId, ct);
            if (invoice is null) return null;
            var customer = await store.GetCustomerAsync(companyId, invoice.CustomerId, ct)
                ?? throw new DomainException("The customer was not found.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new DomainException("A reason is required for a credit note.");
            if (request.Lines.Count == 0) throw new DomainException("A credit note must contain at least one line.");
            if (request.Lines.Select(l => l.ItemId).Distinct().Count() != request.Lines.Count)
                throw new DomainException("Each item can appear only once on a credit note.");

            // Quantity limit: never credit more than was invoiced, counting earlier credit notes.
            var already = InvoiceService.CreditedQuantities(await store.ListCreditNotesForInvoiceAsync(companyId, invoice.Id, ct));
            var lines = new List<CreditNoteLine>();
            foreach (var r in request.Lines)
            {
                var invoiceLine = invoice.Lines.SingleOrDefault(l => l.ItemId == r.ItemId)
                    ?? throw new DomainException("An item on the credit note is not on the invoice.");
                var remaining = invoiceLine.Quantity - already.GetValueOrDefault((invoice.Id.Value, r.ItemId));
                if (r.Quantity <= 0) throw new DomainException($"The credited quantity for '{invoiceLine.Sku}' must be positive.");
                if (r.Quantity > remaining)
                    throw new DomainException($"Cannot credit {r.Quantity:0.####} of '{invoiceLine.Sku}': only {remaining:0.####} remain creditable on {invoice.Number}.");
                lines.Add(new CreditNoteLine(invoiceLine.ItemId, invoiceLine.Sku, invoiceLine.Description, r.Quantity,
                    invoiceLine.UnitPrice, invoiceLine.UnitCost, r.ReturnToStock));
            }

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(CreditNoteSequence, "CN-", ct);

            var total = lines.Sum(l => l.LineTotal);
            var returnedCost = lines.Sum(l => l.ReturnedCost);
            var applied = Math.Min(total, invoice.Balance);

            var returned = new List<CreditNoteLine>();
            PostedJournal? journal = null;
            CreditNote note;
            try
            {
                foreach (var line in lines.Where(l => l.ReturnedToStock))
                {
                    // Returned goods re-enter stock at the cost they left at, so inventory and COGS reverse exactly.
                    await inventory.ReceiveAsync(new StockMovementCommand(
                        line.ItemId, line.Quantity, date, "SalesReturn", number, null, line.UnitCost), ct);
                    returned.Add(line);
                }

                var journalLines = new List<PostJournalLine>
                {
                    new(SalesAccounts.SalesReturns, total, 0, $"Credit note {number}"),
                    new(SalesAccounts.Receivable, 0, total, $"Credit note {number}")
                };
                if (returnedCost > 0)
                {
                    journalLines.Add(new(SalesAccounts.Inventory, returnedCost, 0, $"Returned goods {number}"));
                    journalLines.Add(new(SalesAccounts.CostOfGoodsSold, 0, returnedCost, $"Returned goods {number}"));
                }

                journal = await finance.PostJournalAsync(new PostJournalCommand(
                    "Sales", number, date, $"Credit note {number} for invoice {invoice.Number} - {customer.Name}", journalLines), ct);

                note = CreditNote.Create(companyId, number, invoice, date, request.Reason, lines, journal.JournalId, journal.Number, applied, clock.UtcNow);
                invoice.ApplyCredit(applied);
                await store.AddCreditNoteAsync(note, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await CompensateAsync(returned, journal, number, ct);
                throw;
            }

            await audit.RecordAsync("Sales", "credit-note.created", "CreditNote", note.Id.Value.ToString(), note.Number, true,
                new Dictionary<string, object?>
                {
                    ["Invoice"] = invoice.Number, ["Total"] = note.Total, ["Applied"] = note.AmountApplied,
                    ["RefundDue"] = note.RefundDue, ["ReturnedCost"] = note.ReturnedCost, ["Journal"] = journal.Number
                }, ct);
            return Map(note, invoice.Number, customer.Name, []);
        }, cancellationToken);
    }

    public async Task<RefundResponse?> RefundAsync(CreditNoteId id, RefundRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<RefundResponse?>(companyId, $"creditnote:{id.Value}", async ct =>
        {
            var note = await store.GetCreditNoteAsync(companyId, id, ct);
            if (note is null) return null;
            var customer = await store.GetCustomerAsync(companyId, note.CustomerId, ct);
            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            if (request.Amount <= 0) throw new DomainException("Refund amount must be positive.");
            if (request.Amount > note.RefundDue)
                throw new DomainException($"Refund exceeds the amount due to the customer of {note.RefundDue:N2}.");
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(RefundSequence, "RFD-", ct);

            var account = request.Method == PaymentMethod.Cash ? SalesAccounts.Cash : SalesAccounts.Bank;
            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Sales", number, date, $"Refund {number} for credit note {note.Number}",
                [
                    new PostJournalLine(SalesAccounts.Receivable, request.Amount, 0, $"Credit note {note.Number}"),
                    new PostJournalLine(account, 0, request.Amount, $"Refund {number}")
                ]), ct);

            CustomerRefund refund;
            try
            {
                note.ApplyRefund(request.Amount);
                refund = CustomerRefund.Create(companyId, number, note.Id, note.CustomerId, date, request.Amount,
                    request.Method, request.Reference, journal.Number, clock.UtcNow);
                await store.AddRefundAsync(refund, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                try { await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: refund {number} failed to save", ct); }
                catch { /* left for manual reconciliation */ }
                throw;
            }

            await audit.RecordAsync("Sales", "refund.recorded", "CustomerRefund", refund.Id.Value.ToString(), refund.Number, true,
                new Dictionary<string, object?> { ["CreditNote"] = note.Number, ["Amount"] = refund.Amount }, ct);
            return MapRefund(refund, note.Number, customer?.Name ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditNoteResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var notes = await store.ListCreditNotesAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var customers = (await store.ListCustomersAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var invoices = (await store.ListInvoicesAsync(companyId, 500, cancellationToken)).ToDictionary(x => x.Id, x => x.Number);
        var refunds = (await store.ListRefundsAsync(companyId, 500, cancellationToken)).ToLookup(x => x.CreditNoteId);
        return notes.Select(n => Map(
            n, invoices.GetValueOrDefault(n.SalesInvoiceId, string.Empty), customers.GetValueOrDefault(n.CustomerId, string.Empty),
            refunds[n.Id].Select(r => MapRefund(r, n.Number, customers.GetValueOrDefault(n.CustomerId, string.Empty))).ToArray())).ToArray();
    }

    public async Task<CreditNoteResponse?> GetAsync(CreditNoteId id, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var note = await store.GetCreditNoteAsync(companyId, id, cancellationToken);
        if (note is null) return null;
        var customer = await store.GetCustomerAsync(companyId, note.CustomerId, cancellationToken);
        var invoice = await store.GetInvoiceAsync(companyId, note.SalesInvoiceId, cancellationToken);
        var name = customer?.Name ?? string.Empty;
        var refunds = (await store.ListRefundsAsync(companyId, 500, cancellationToken)).Where(r => r.CreditNoteId == note.Id)
            .Select(r => MapRefund(r, note.Number, name)).ToArray();
        return Map(note, invoice?.Number ?? string.Empty, name, refunds);
    }

    private async Task CompensateAsync(List<CreditNoteLine> returned, PostedJournal? journal, string number, CancellationToken ct)
    {
        // Best effort: never let a compensation failure mask the original error.
        try
        {
            if (journal is not null)
                await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: credit note {number} failed to save", ct);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            foreach (var line in returned)
                await inventory.IssueAsync(new StockMovementCommand(line.ItemId, line.Quantity, today, "SalesReturnVoid", number), ct);
        }
        catch
        {
            // Left for manual reconciliation; ledgers and the audit trail retain every posting.
        }
    }

    private static CreditNoteResponse Map(CreditNote n, string invoiceNumber, string customerName, IReadOnlyList<RefundResponse> refunds) => new(
        n.Id.Value, n.Number, n.SalesInvoiceId.Value, invoiceNumber, n.CustomerId.Value, customerName, n.CreditDate, n.Reason,
        n.Total, n.AmountApplied, n.AmountRefunded, n.RefundDue, n.ReturnedCost, n.JournalNumber,
        n.Lines.Select(l => new CreditNoteLineResponse(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, l.LineTotal, l.ReturnedToStock)).ToArray(),
        refunds);

    private static RefundResponse MapRefund(CustomerRefund r, string creditNoteNumber, string customerName) => new(
        r.Id.Value, r.Number, r.CreditNoteId.Value, creditNoteNumber, r.CustomerId.Value, customerName,
        r.RefundDate, r.Amount, r.Method, r.Reference, r.JournalNumber);
}
