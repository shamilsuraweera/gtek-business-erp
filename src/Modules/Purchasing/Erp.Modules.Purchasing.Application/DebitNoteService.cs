using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Application;

/// <summary>
/// Vendor debit notes and vendor refunds. Goods returned after they were billed leave GRNI over-cleared; the vendor's credit puts it
/// right: Dr Payables (billed price) / Cr GRNI (order price), with the difference to Purchase Price Variance. The credit first
/// reduces the bill's balance; anything beyond it is owed back by the vendor and settled with a refund (Dr Cash or Bank / Cr Payables).
/// </summary>
public sealed class DebitNoteService(
    IPurchasingStore store,
    IFinancePosting finance,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IDebitNoteService
{
    public const string DebitNoteSequence = "DN";
    public const string RefundSequence = "VRFD";

    public async Task<DebitNoteResponse?> CreateAsync(VendorBillId billId, CreateDebitNoteRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        // The order's quantities and the bill's balance both change, so serialise with receipts/returns (order) and payments (bill).
        var orderId = await store.GetBillOrderIdAsync(companyId, billId, cancellationToken);
        if (orderId is not { } purchaseOrderId) return null;

        return await store.InLockedTransactionAsync<DebitNoteResponse?>(
            companyId, [$"order:{purchaseOrderId.Value}", $"bill:{billId.Value}"], async ct =>
        {
            var bill = await store.GetBillAsync(companyId, billId, ct);
            if (bill is null) return null;
            var order = await store.GetOrderAsync(companyId, bill.PurchaseOrderId, ct)
                ?? throw new DomainException("The purchase order was not found.");
            var vendor = await store.GetVendorAsync(companyId, bill.VendorId, ct)
                ?? throw new DomainException("The vendor was not found.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new DomainException("A reason is required for a debit note.");
            if (request.Lines.Count == 0) throw new DomainException("A debit note must contain at least one line.");
            if (request.Lines.Select(l => l.ItemId).Distinct().Count() != request.Lines.Count)
                throw new DomainException("Each item can appear only once on a debit note.");

            // Per-bill limit: never debit more than this bill charged, counting earlier debit notes against it.
            var already = BillService.DebitedQuantities(await store.ListDebitNotesForBillAsync(companyId, bill.Id, ct));
            var lines = new List<DebitNoteLine>();
            foreach (var r in request.Lines)
            {
                var billLine = bill.Lines.SingleOrDefault(l => l.ItemId == r.ItemId)
                    ?? throw new DomainException("An item on the debit note is not on the bill.");
                var remaining = billLine.Quantity - already.GetValueOrDefault((bill.Id.Value, r.ItemId));
                if (r.Quantity <= 0) throw new DomainException($"The debited quantity for '{billLine.Sku}' must be positive.");
                if (r.Quantity > remaining)
                    throw new DomainException($"Cannot debit {r.Quantity:0.####} of '{billLine.Sku}': only {remaining:0.####} remain on {bill.Number}.");
                // The vendor credits at the price it billed; the accrual being cleared is at the order price.
                lines.Add(new DebitNoteLine(billLine.ItemId, billLine.Sku, billLine.Description, r.Quantity, billLine.OrderUnitCost, billLine.UnitCost));
            }

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            // Order-level limit: only billed goods that were returned can be debited (rejects debiting goods we still hold).
            order.RecordDebit(lines.Select(l => (l.ItemId, l.Quantity)).ToList());
            var number = await numbers.NextAsync(DebitNoteSequence, "DN-", ct);

            var total = lines.Sum(l => l.LineTotal);
            var accrued = lines.Sum(l => l.AccruedTotal);
            var variance = total - accrued;
            var journalLines = new List<PostJournalLine>
            {
                PostJournalLine.ForRole(AccountRole.AccountsPayable, total, 0, $"Debit note {number}"),
                PostJournalLine.ForRole(AccountRole.GoodsReceivedNotInvoiced, 0, accrued, $"Debit note {number}")
            };
            if (variance > 0) journalLines.Add(PostJournalLine.ForRole(AccountRole.PurchasePriceVariance, 0, variance, $"Price variance reversal {number}"));
            else if (variance < 0) journalLines.Add(PostJournalLine.ForRole(AccountRole.PurchasePriceVariance, -variance, 0, $"Price variance reversal {number}"));

            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Purchasing", number, date, $"Debit note {number} for bill {bill.Number} - {vendor.Name}", journalLines), ct);

            DebitNote note;
            try
            {
                var applied = Math.Min(total, bill.Balance);
                note = DebitNote.Create(companyId, number, bill, date, request.Reason, lines, journal.JournalId, journal.Number, applied, clock.UtcNow);
                bill.ApplyDebit(applied);
                await store.AddDebitNoteAsync(note, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await ReverseQuietlyAsync(journal, $"Compensation: debit note {number} failed to save", ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", "debit-note.created", "DebitNote", note.Id.Value.ToString(), note.Number, true,
                new Dictionary<string, object?>
                {
                    ["Bill"] = bill.Number, ["Total"] = note.Total, ["Applied"] = note.AmountApplied,
                    ["RefundDue"] = note.RefundDue, ["PriceVariance"] = note.PriceVariance, ["Journal"] = journal.Number
                }, ct);
            return Map(note, bill.Number, vendor.Name, []);
        }, cancellationToken);
    }

    public async Task<VendorRefundResponse?> RefundAsync(DebitNoteId id, VendorRefundRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<VendorRefundResponse?>(companyId, $"debitnote:{id.Value}", async ct =>
        {
            var note = await store.GetDebitNoteAsync(companyId, id, ct);
            if (note is null) return null;
            var vendor = await store.GetVendorAsync(companyId, note.VendorId, ct);
            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            if (request.Amount <= 0) throw new DomainException("Refund amount must be positive.");
            if (request.Amount > note.RefundDue)
                throw new DomainException($"Refund exceeds the amount the vendor owes of {note.RefundDue:N2}.");
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(RefundSequence, "VRFD-", ct);

            var account = request.Method == PaymentMethod.Cash ? AccountRole.Cash : AccountRole.Bank;
            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Purchasing", number, date, $"Vendor refund {number} for debit note {note.Number}",
                [
                    PostJournalLine.ForRole(account, request.Amount, 0, $"Refund {number}"),
                    PostJournalLine.ForRole(AccountRole.AccountsPayable, 0, request.Amount, $"Debit note {note.Number}")
                ]), ct);

            VendorRefund refund;
            try
            {
                note.ApplyRefund(request.Amount);
                refund = VendorRefund.Create(companyId, number, note.Id, note.VendorId, date, request.Amount,
                    request.Method, request.Reference, journal.Number, clock.UtcNow);
                await store.AddVendorRefundAsync(refund, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await ReverseQuietlyAsync(journal, $"Compensation: vendor refund {number} failed to save", ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", "vendor-refund.recorded", "VendorRefund", refund.Id.Value.ToString(), refund.Number, true,
                new Dictionary<string, object?> { ["DebitNote"] = note.Number, ["Amount"] = refund.Amount }, ct);
            return MapRefund(refund, note.Number, vendor?.Name ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<DebitNoteResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var notes = await store.ListDebitNotesAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var vendors = (await store.ListVendorsAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var bills = (await store.ListBillsAsync(companyId, 500, cancellationToken)).ToDictionary(x => x.Id, x => x.Number);
        var refunds = (await store.ListVendorRefundsAsync(companyId, 500, cancellationToken)).ToLookup(x => x.DebitNoteId);
        return notes.Select(n => Map(
            n, bills.GetValueOrDefault(n.VendorBillId, string.Empty), vendors.GetValueOrDefault(n.VendorId, string.Empty),
            refunds[n.Id].Select(r => MapRefund(r, n.Number, vendors.GetValueOrDefault(n.VendorId, string.Empty))).ToArray())).ToArray();
    }

    public async Task<DebitNoteResponse?> GetAsync(DebitNoteId id, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var note = await store.GetDebitNoteAsync(companyId, id, cancellationToken);
        if (note is null) return null;
        var vendor = await store.GetVendorAsync(companyId, note.VendorId, cancellationToken);
        var bill = await store.GetBillAsync(companyId, note.VendorBillId, cancellationToken);
        var name = vendor?.Name ?? string.Empty;
        var refunds = (await store.ListVendorRefundsAsync(companyId, 500, cancellationToken)).Where(r => r.DebitNoteId == note.Id)
            .Select(r => MapRefund(r, note.Number, name)).ToArray();
        return Map(note, bill?.Number ?? string.Empty, name, refunds);
    }

    private async Task ReverseQuietlyAsync(PostedJournal journal, string reason, CancellationToken ct)
    {
        try { await finance.ReverseJournalAsync(journal.JournalId, reason, ct); }
        catch { /* left for manual reconciliation; never mask the original error */ }
    }

    private static DebitNoteResponse Map(DebitNote n, string billNumber, string vendorName, IReadOnlyList<VendorRefundResponse> refunds) => new(
        n.Id.Value, n.Number, n.PurchaseOrderId.Value, n.VendorBillId.Value, billNumber, n.VendorId.Value, vendorName, n.DebitDate, n.Reason,
        n.Total, n.AmountApplied, n.AmountRefunded, n.RefundDue, n.PriceVariance, n.JournalNumber,
        n.Lines.Select(l => new DebitNoteLineResponse(l.ItemId, l.Sku, l.Description, l.Quantity, l.OrderUnitCost, l.UnitCost, l.LineTotal, l.PriceVariance)).ToArray(),
        refunds);

    private static VendorRefundResponse MapRefund(VendorRefund r, string debitNoteNumber, string vendorName) => new(
        r.Id.Value, r.Number, r.DebitNoteId.Value, debitNoteNumber, r.VendorId.Value, vendorName,
        r.RefundDate, r.Amount, r.Method, r.Reference, r.JournalNumber);
}
