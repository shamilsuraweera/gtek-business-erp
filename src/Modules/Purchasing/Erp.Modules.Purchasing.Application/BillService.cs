using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Application;

/// <summary>Vendor bills (Dr GRNI / Cr Payables) and payments (Dr Payables / Cr Cash or Bank).</summary>
public sealed class BillService(
    IPurchasingStore store,
    IInventoryCatalog catalog,
    ITaxCatalog taxCatalog,
    IFinancePosting finance,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IBillService
{
    public const string BillSequence = "BILL";
    public const string PaymentSequence = "VPAY";

    public async Task<BillResponse?> CreateFromOrderAsync(
        PurchaseOrderId orderId, CreateBillRequest request, bool canOverrideVariance, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<BillResponse?>(companyId, $"order:{orderId.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, orderId, ct);
            if (order is null) return null;
            if (order.Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
                throw new DomainException("Bills can only be entered for purchase orders with received goods.");
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct)
                ?? throw new DomainException("The vendor was not found.");
            if (!string.IsNullOrWhiteSpace(request.VendorReference) &&
                await store.BillExistsForVendorReferenceAsync(companyId, order.VendorId, request.VendorReference.Trim(), ct))
                throw new DomainException($"Vendor invoice '{request.VendorReference.Trim()}' has already been entered for {vendor.Name}.");

            // Quantity leg: bill what was received and not yet billed unless told otherwise. Price leg: default to the order price.
            var requested = request.Lines is { Count: > 0 }
                ? request.Lines.ToList()
                : order.ReceivedNotBilled().Select(x => new BillLineRequest(x.Key, x.Value, null)).ToList();
            var items = (await catalog.GetItemsAsync(order.Lines.Select(l => l.ItemId).ToArray(), ct)).ToDictionary(x => x.Id);
            var taxes = await ResolveTaxAsync(order, items, ct);
            var billLines = new List<VendorBillLine>();
            var overTolerance = new List<string>();
            foreach (var r in requested)
            {
                var line = order.LineFor(r.ItemId);
                var quantity = r.Quantity ?? line.ReceivedNotBilled;
                var unitCost = r.UnitCost ?? line.UnitCost;
                var (taxCode, taxRate) = taxes[line.ItemId];
                var tolerance = ThreeWayMatch.EffectiveTolerance(items.GetValueOrDefault(line.ItemId)?.PriceTolerancePercent, vendor.PriceTolerancePercent);
                var net = decimal.Round(quantity * unitCost, 2);
                billLines.Add(new VendorBillLine(line.ItemId, line.Sku, line.Description, quantity, line.UnitCost, unitCost,
                    taxCode, taxRate, TaxMath.Tax(net, taxRate), tolerance));
                if (!ThreeWayMatch.WithinTolerance(line.UnitCost, unitCost, tolerance))
                    overTolerance.Add($"{line.Sku}: billed {unitCost:N2} vs order {line.UnitCost:N2} ({ThreeWayMatch.VariancePercent(line.UnitCost, unitCost):+0.##;-0.##}%, tolerance {tolerance:0.##}%)");
            }

            var varianceApproved = false;
            if (overTolerance.Count > 0)
            {
                if (!request.OverrideVariance)
                    throw new DomainException(
                        $"Price variance exceeds tolerance — {string.Join("; ", overTolerance)}. " +
                        "Correct the billed price or have someone with approval rights override the variance.");
                if (!canOverrideVariance)
                    throw new DomainException("You do not have permission to override a price variance.");
                varianceApproved = true;
            }

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            order.RecordBilling(billLines.Select(l => (l.ItemId, l.Quantity)).ToList()); // rejects billing more than was received
            var number = await numbers.NextAsync(BillSequence, "BILL-", ct);

            var accrued = billLines.Sum(l => l.AccruedTotal);
            var subtotal = billLines.Sum(l => l.LineTotal);
            var tax = billLines.Sum(l => l.TaxAmount);
            var variance = subtotal - accrued;
            var journalLines = new List<PostJournalLine>
            {
                PostJournalLine.ForRole(AccountRole.GoodsReceivedNotInvoiced, accrued, 0, $"Bill {number}"),
                PostJournalLine.ForRole(AccountRole.AccountsPayable, 0, subtotal + tax, $"Bill {number}")
            };
            if (tax > 0) journalLines.Add(PostJournalLine.ForRole(AccountRole.TaxReceivable, tax, 0, $"Input tax {number}"));
            if (variance > 0) journalLines.Add(PostJournalLine.ForRole(AccountRole.PurchasePriceVariance, variance, 0, $"Price variance {number}"));
            else if (variance < 0) journalLines.Add(PostJournalLine.ForRole(AccountRole.PurchasePriceVariance, 0, -variance, $"Price variance {number}"));

            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Purchasing", number, date, $"Vendor bill {number} ({order.Number}) - {vendor.Name}", journalLines), ct);

            VendorBill bill;
            try
            {
                bill = VendorBill.Create(companyId, number, request.VendorReference, order, date, vendor.PaymentTermsDays,
                    billLines, journal.JournalId, journal.Number, varianceApproved, clock.UtcNow);
                await store.AddBillAsync(bill, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await ReverseQuietlyAsync(journal, $"Compensation: bill {number} failed to save", ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", varianceApproved ? "bill.created.variance-override" : "bill.created", "VendorBill",
                bill.Id.Value.ToString(), bill.Number, true,
                new Dictionary<string, object?> { ["Order"] = order.Number, ["Total"] = bill.Total, ["Tax"] = bill.TaxTotal, ["PriceVariance"] = bill.PriceVariance, ["Journal"] = journal.Number }, ct);
            return Map(bill, vendor.Name, DebitedQuantities([]));
        }, cancellationToken);
    }

    public async Task<VendorPaymentResponse?> PayAsync(VendorBillId id, PayBillRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<VendorPaymentResponse?>(companyId, $"bill:{id.Value}", async ct =>
        {
            var bill = await store.GetBillAsync(companyId, id, ct);
            if (bill is null) return null;
            var vendor = await store.GetVendorAsync(companyId, bill.VendorId, ct);
            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            if (request.Amount <= 0) throw new DomainException("Payment amount must be positive.");
            if (request.Amount > bill.Balance) throw new DomainException($"Payment exceeds the bill balance of {bill.Balance:N2}.");
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            var number = await numbers.NextAsync(PaymentSequence, "VPAY-", ct);

            var account = request.Method == PaymentMethod.Cash ? AccountRole.Cash : AccountRole.Bank;
            var journal = await finance.PostJournalAsync(new PostJournalCommand(
                "Purchasing", number, date, $"Payment {number} for bill {bill.Number}",
                [
                    PostJournalLine.ForRole(AccountRole.AccountsPayable, request.Amount, 0, $"Bill {bill.Number}"),
                    PostJournalLine.ForRole(account, 0, request.Amount, $"Payment {number}")
                ]), ct);

            VendorPayment payment;
            try
            {
                bill.ApplyPayment(request.Amount);
                payment = VendorPayment.Create(companyId, number, bill.Id, bill.VendorId, date, request.Amount,
                    request.Method, request.Reference, journal.Number, clock.UtcNow);
                await store.AddPaymentAsync(payment, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await ReverseQuietlyAsync(journal, $"Compensation: payment {number} failed to save", ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", "payment.recorded", "VendorPayment", payment.Id.Value.ToString(), payment.Number, true,
                new Dictionary<string, object?> { ["Bill"] = bill.Number, ["Amount"] = payment.Amount }, ct);
            return MapPayment(payment, bill.Number, vendor?.Name ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<BillResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var bills = await store.ListBillsAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var names = (await store.ListVendorsAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var debited = DebitedQuantities(await store.ListDebitNotesAsync(companyId, 500, cancellationToken));
        return bills.Select(b => Map(b, names.GetValueOrDefault(b.VendorId, string.Empty), debited)).ToArray();
    }

    public async Task<BillResponse?> GetAsync(VendorBillId id, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var bill = await store.GetBillAsync(companyId, id, cancellationToken);
        if (bill is null) return null;
        var vendor = await store.GetVendorAsync(companyId, bill.VendorId, cancellationToken);
        var debited = DebitedQuantities(await store.ListDebitNotesForBillAsync(companyId, bill.Id, cancellationToken));
        return Map(bill, vendor?.Name ?? string.Empty, debited);
    }

    public async Task<IReadOnlyList<VendorPaymentResponse>> ListPaymentsAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var payments = await store.ListPaymentsAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var vendors = (await store.ListVendorsAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var bills = (await store.ListBillsAsync(companyId, 500, cancellationToken)).ToDictionary(x => x.Id, x => x.Number);
        return payments.Select(p => MapPayment(
            p, bills.GetValueOrDefault(p.BillId, string.Empty), vendors.GetValueOrDefault(p.VendorId, string.Empty))).ToArray();
    }

    /// <summary>The tax code and rate each ordered item carries today. An item with no code is untaxed; a code that is missing or inactive blocks billing.</summary>
    private async Task<Dictionary<Guid, (string? Code, decimal Rate)>> ResolveTaxAsync(PurchaseOrder order, IReadOnlyDictionary<Guid, ItemInfo> items, CancellationToken ct)
    {
        var rates = await taxCatalog.GetAsync(items.Values.Select(x => x.TaxCode).OfType<string>(), ct);
        var result = new Dictionary<Guid, (string?, decimal)>();
        foreach (var line in order.Lines)
        {
            var code = items.GetValueOrDefault(line.ItemId)?.TaxCode;
            if (code is null) { result[line.ItemId] = (null, 0m); continue; }
            if (!rates.TryGetValue(code, out var info) || !info.IsActive)
                throw new DomainException($"Item '{line.Sku}' uses tax code '{code}', which is missing or inactive. Fix the item's tax code before entering the bill.");
            result[line.ItemId] = (info.Code, info.RatePercent);
        }

        return result;
    }

    private async Task ReverseQuietlyAsync(PostedJournal journal, string reason, CancellationToken ct)
    {
        try { await finance.ReverseJournalAsync(journal.JournalId, reason, ct); }
        catch { /* left for manual reconciliation; never mask the original error */ }
    }

    /// <summary>Quantity debited so far, keyed by (bill, item).</summary>
    internal static IReadOnlyDictionary<(Guid Bill, Guid Item), decimal> DebitedQuantities(IEnumerable<DebitNote> notes) =>
        notes.SelectMany(n => n.Lines.Select(l => (Bill: n.VendorBillId.Value, Item: l.ItemId, l.Quantity)))
            .GroupBy(x => (x.Bill, x.Item))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    private BillResponse Map(VendorBill b, string vendorName, IReadOnlyDictionary<(Guid Bill, Guid Item), decimal> debited) => new(
        b.Id.Value, b.Number, b.VendorReference, b.PurchaseOrderId.Value, b.VendorId.Value, vendorName, b.BillDate, b.DueDate,
        b.Subtotal, b.TaxTotal, b.Total, b.AmountPaid, b.AmountDebited, b.Balance, b.Status,
        b.Status == BillStatus.Open && b.DueDate < DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
        b.JournalNumber, b.PriceVariance, b.VarianceOverridden,
        b.Lines.Select(l => new BillLineResponse(
            l.ItemId, l.Sku, l.Description, l.Quantity, l.OrderUnitCost, l.UnitCost, l.LineTotal, l.PriceVariance,
            l.PriceVariance == 0 ? "Matched" : l.WithinTolerance ? "Within tolerance" : "Variance approved",
            debited.GetValueOrDefault((b.Id.Value, l.ItemId)), l.TaxCode, l.TaxRate, l.TaxAmount, l.TolerancePercent)).ToArray());

    private static VendorPaymentResponse MapPayment(VendorPayment p, string billNumber, string vendorName) => new(
        p.Id.Value, p.Number, p.BillId.Value, billNumber, p.VendorId.Value, vendorName,
        p.PaymentDate, p.Amount, p.Method, p.Reference, p.JournalNumber);
}
