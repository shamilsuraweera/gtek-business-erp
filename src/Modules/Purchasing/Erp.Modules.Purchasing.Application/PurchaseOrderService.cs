using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Application;

/// <summary>
/// Purchase order lifecycle. Receiving goods writes the stock ledger at the PO cost and accrues the liability
/// (Dr Inventory / Cr GRNI). Modules own separate databases (ADR 0002), so failures compensate earlier steps.
/// </summary>
public sealed class PurchaseOrderService(
    IPurchasingStore store,
    IInventoryCatalog catalog,
    IInventoryStock inventory,
    IFinancePosting finance,
    IPostingPeriodGuard periodGuard,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IPurchaseOrderService
{
    public const string OrderSequence = "PO";
    public const string ReceiptSequence = "GRN";
    public const string ReturnSequence = "RTV";

    public async Task<PurchaseOrderResponse> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var vendor = await store.GetVendorAsync(companyId, new VendorId(request.VendorId), cancellationToken)
            ?? throw new DomainException("The vendor was not found.");
        if (!vendor.IsActive) throw new DomainException($"Vendor '{vendor.Code}' is inactive.");
        if (request.Lines.Count == 0) throw new DomainException("A purchase order must contain at least one line.");

        var items = (await catalog.GetItemsAsync(request.Lines.Select(x => x.ItemId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(x => x.Id);
        var number = await numbers.NextAsync(OrderSequence, "PO-", cancellationToken);
        var order = PurchaseOrder.Create(
            companyId, number, vendor.Id, request.OrderDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            request.Notes, clock.UtcNow);
        foreach (var line in request.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item)) throw new DomainException("An item on the order was not found.");
            if (!item.IsActive) throw new DomainException($"Item '{item.Sku}' is inactive.");
            order.AddLine(item.Id, item.Sku, item.Name, line.Quantity, line.UnitCost ?? item.UnitCost);
        }

        await store.AddOrderAsync(order, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Purchasing", "order.created", "PurchaseOrder", order.Id.Value.ToString(), order.Number, true,
            new Dictionary<string, object?> { ["Vendor"] = vendor.Code, ["Total"] = order.Total }, cancellationToken);
        return Map(order, vendor.Name, [], [], []);
    }

    public async Task<IReadOnlyList<PurchaseOrderResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var orders = await store.ListOrdersAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var names = (await store.ListVendorsAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        return orders.Select(o => Map(o, names.GetValueOrDefault(o.VendorId, string.Empty), [], [], [])).ToArray();
    }

    public async Task<PurchaseOrderResponse?> GetAsync(PurchaseOrderId id, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var order = await store.GetOrderAsync(companyId, id, cancellationToken);
        if (order is null) return null;
        var vendor = await store.GetVendorAsync(companyId, order.VendorId, cancellationToken);
        return await MapFullAsync(order, vendor?.Name ?? string.Empty, companyId, cancellationToken);
    }

    public async Task<PurchaseOrderResponse?> TransitionAsync(PurchaseOrderId id, PurchaseOrderAction action, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<PurchaseOrderResponse?>(companyId, $"order:{id.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, id, ct);
            if (order is null) return null;
            switch (action)
            {
                case PurchaseOrderAction.Open: order.Open(); break;
                case PurchaseOrderAction.Release: order.Release(); break;
                case PurchaseOrderAction.Cancel: order.Cancel(); break;
                case PurchaseOrderAction.CloseShort: order.CloseShort(); break;
            }

            await store.SaveChangesAsync(ct);
            await audit.RecordAsync("Purchasing", $"order.{action.ToString().ToLowerInvariant()}", "PurchaseOrder",
                order.Id.Value.ToString(), order.Number, true, null, ct);
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct);
            return await MapFullAsync(order, vendor?.Name ?? string.Empty, companyId, ct);
        }, cancellationToken);
    }

    public async Task<PurchaseOrderResponse?> ReceiveAsync(PurchaseOrderId id, ReceiveGoodsRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<PurchaseOrderResponse?>(companyId, $"order:{id.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, id, ct);
            if (order is null) return null;
            if (order.Status is not (PurchaseOrderStatus.Released or PurchaseOrderStatus.PartiallyReceived))
                throw new DomainException("Goods can only be received against released purchase orders that are not fully received.");
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct)
                ?? throw new DomainException("The vendor was not found.");

            // Receive exactly what was asked for (default: everything outstanding). The aggregate rejects over-receipt.
            var wanted = request.Lines is { Count: > 0 }
                ? request.Lines.Select(l => (l.ItemId, l.Quantity)).ToList()
                : order.OutstandingToReceive().Select(x => (x.Key, x.Value)).ToList();
            // Over-delivery tolerance per line: the item's own, else the vendor's, else none.
            var catalogItems = (await catalog.GetItemsAsync(order.Lines.Select(l => l.ItemId).ToArray(), ct)).ToDictionary(x => x.Id);
            var tolerances = order.Lines.ToDictionary(l => l.ItemId, l => ThreeWayMatch.EffectiveQuantityTolerance(
                catalogItems.GetValueOrDefault(l.ItemId)?.QuantityTolerancePercent, vendor.QuantityTolerancePercent));
            var underTolerances = order.Lines.ToDictionary(l => l.ItemId, l => ThreeWayMatch.EffectiveUnderDeliveryTolerance(
                catalogItems.GetValueOrDefault(l.ItemId)?.UnderDeliveryTolerancePercent, vendor.UnderDeliveryTolerancePercent));
            var lines = wanted.Select(w =>
            {
                var line = order.LineFor(w.Item1);
                return new GoodsReceiptLine(line.ItemId, line.Sku, line.Description, w.Item2, line.UnitCost, tolerances[line.ItemId], underTolerances[line.ItemId]);
            }).ToList();

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            order.RecordReceipt(wanted, tolerances, underTolerances); // validates quantities against the outstanding balance (plus tolerance) before anything is posted
            var number = await numbers.NextAsync(ReceiptSequence, "GRN-", ct);

            var received = new List<GoodsReceiptLine>();
            PostedJournal? journal = null;
            GoodsReceipt receipt;
            try
            {
                foreach (var line in lines)
                {
                    await inventory.ReceiveAsync(new StockMovementCommand(
                        line.ItemId, line.Quantity, date, "PurchaseReceipt", $"{order.Number}/{number}", null, line.UnitCost), ct);
                    received.Add(line);
                }

                var total = lines.Sum(x => x.LineTotal);
                journal = await finance.PostJournalAsync(new PostJournalCommand(
                    "Purchasing", number, date, $"Goods received {number} ({order.Number}) - {vendor.Name}",
                    [
                        PostJournalLine.ForRole(AccountRole.Inventory, total, 0, $"Receipt {number}"),
                        PostJournalLine.ForRole(AccountRole.GoodsReceivedNotInvoiced, 0, total, $"Receipt {number}")
                    ]), ct);

                receipt = GoodsReceipt.Create(companyId, number, order.Id, order.VendorId, date, request.Reference, lines,
                    journal.JournalId, journal.Number, clock.UtcNow);
                await store.AddReceiptAsync(receipt, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await CompensateAsync(received, journal, number, ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", "order.received", "PurchaseOrder", order.Id.Value.ToString(), order.Number, true,
                new Dictionary<string, object?> { ["Receipt"] = receipt.Number, ["Total"] = receipt.Total, ["Journal"] = journal.Number, ["Status"] = order.Status.ToString() }, ct);
            return await MapFullAsync(order, vendor.Name, companyId, ct);
        }, cancellationToken);
    }

    private async Task CompensateAsync(List<GoodsReceiptLine> received, PostedJournal? journal, string number, CancellationToken ct)
    {
        // Best effort: never let a compensation failure mask the original error.
        try
        {
            if (journal is not null)
                await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: receipt {number} failed to save", ct);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            foreach (var line in received)
                await inventory.IssueAsync(new StockMovementCommand(line.ItemId, line.Quantity, today, "PurchaseReceiptVoid", number), ct);
        }
        catch
        {
            // Left for manual reconciliation; ledgers and the audit trail retain every posting.
        }
    }

    public async Task<PurchaseOrderResponse?> ReturnAsync(PurchaseOrderId id, ReturnGoodsRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<PurchaseOrderResponse?>(companyId, $"order:{id.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, id, ct);
            if (order is null) return null;
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct)
                ?? throw new DomainException("The vendor was not found.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new DomainException("A reason is required for a return to vendor.");
            if (request.Lines.Count == 0) throw new DomainException("Specify at least one line to return.");

            // Goods leave at the cost they came in at (the order price), so inventory reverses exactly what the receipt booked.
            var wanted = request.Lines.Select(l => (l.ItemId, l.Quantity)).ToList();
            var lines = wanted.Select(w =>
            {
                var line = order.LineFor(w.ItemId);
                return new VendorReturnLine(line.ItemId, line.Sku, line.Description, w.Quantity, line.UnitCost);
            }).ToList();

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            order.RecordReturn(wanted); // validates against the goods we still hold before anything is posted
            var number = await numbers.NextAsync(ReturnSequence, "RTV-", ct);

            var issued = new List<VendorReturnLine>();
            PostedJournal? journal = null;
            VendorReturn vendorReturn;
            try
            {
                foreach (var line in lines)
                {
                    await inventory.IssueAsync(new StockMovementCommand(
                        line.ItemId, line.Quantity, date, "PurchaseReturn", $"{order.Number}/{number}", null, line.UnitCost), ct);
                    issued.Add(line);
                }

                var total = lines.Sum(x => x.LineTotal);
                journal = await finance.PostJournalAsync(new PostJournalCommand(
                    "Purchasing", number, date, $"Return to vendor {number} ({order.Number}) - {vendor.Name}",
                    [
                        PostJournalLine.ForRole(AccountRole.GoodsReceivedNotInvoiced, total, 0, $"Return {number}"),
                        PostJournalLine.ForRole(AccountRole.Inventory, 0, total, $"Return {number}")
                    ]), ct);

                vendorReturn = VendorReturn.Create(companyId, number, order.Id, order.VendorId, date, request.Reason, request.Reference,
                    lines, journal.JournalId, journal.Number, clock.UtcNow);
                await store.AddReturnAsync(vendorReturn, ct);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await CompensateReturnAsync(issued, journal, number, ct);
                throw;
            }

            await audit.RecordAsync("Purchasing", "order.returned", "PurchaseOrder", order.Id.Value.ToString(), order.Number, true,
                new Dictionary<string, object?> { ["Return"] = vendorReturn.Number, ["Total"] = vendorReturn.Total, ["Reason"] = vendorReturn.Reason, ["Journal"] = journal.Number }, ct);
            return await MapFullAsync(order, vendor.Name, companyId, ct);
        }, cancellationToken);
    }

    private async Task CompensateReturnAsync(List<VendorReturnLine> issued, PostedJournal? journal, string number, CancellationToken ct)
    {
        // Best effort: never let a compensation failure mask the original error.
        try
        {
            if (journal is not null)
                await finance.ReverseJournalAsync(journal.JournalId, $"Compensation: return {number} failed to save", ct);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            foreach (var line in issued)
                await inventory.ReceiveAsync(new StockMovementCommand(
                    line.ItemId, line.Quantity, today, "PurchaseReturnVoid", number, null, line.UnitCost), ct);
        }
        catch
        {
            // Left for manual reconciliation; ledgers and the audit trail retain every posting.
        }
    }

    private async Task<PurchaseOrderResponse> MapFullAsync(PurchaseOrder order, string vendorName, CompanyId companyId, CancellationToken ct)
    {
        var receipts = await store.ListReceiptsForOrderAsync(companyId, order.Id, ct);
        var bills = await store.ListBillsForOrderAsync(companyId, order.Id, ct);
        var returns = await store.ListReturnsForOrderAsync(companyId, order.Id, ct);
        // The tolerance a bill would be matched against for each line (item, else vendor, else company default).
        var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct);
        var items = (await catalog.GetItemsAsync(order.Lines.Select(l => l.ItemId).ToArray(), ct)).ToDictionary(x => x.Id);
        var tolerances = order.Lines.ToDictionary(l => l.ItemId,
            l => ThreeWayMatch.EffectiveTolerance(items.GetValueOrDefault(l.ItemId)?.PriceTolerancePercent, vendor?.PriceTolerancePercent));
        var quantityTolerances = order.Lines.ToDictionary(l => l.ItemId,
            l => ThreeWayMatch.EffectiveQuantityTolerance(items.GetValueOrDefault(l.ItemId)?.QuantityTolerancePercent, vendor?.QuantityTolerancePercent));
        var underDeliveryTolerances = order.Lines.ToDictionary(l => l.ItemId,
            l => ThreeWayMatch.EffectiveUnderDeliveryTolerance(items.GetValueOrDefault(l.ItemId)?.UnderDeliveryTolerancePercent, vendor?.UnderDeliveryTolerancePercent));
        return Map(order, vendorName,
            receipts.OrderBy(r => r.ReceiptDate).ThenBy(r => r.Number)
                .Select(r => new ReceiptSummary(r.Id.Value, r.Number, r.ReceiptDate, r.Reference, r.Total, r.JournalNumber)).ToArray(),
            bills.OrderBy(b => b.BillDate).ThenBy(b => b.Number)
                .Select(b => new BillSummary(b.Id.Value, b.Number, b.BillDate, b.Total, b.PriceVariance, b.Status)).ToArray(),
            returns.OrderBy(r => r.ReturnDate).ThenBy(r => r.Number)
                .Select(r => new ReturnSummary(r.Id.Value, r.Number, r.ReturnDate, r.Reason, r.Reference, r.Total, r.JournalNumber)).ToArray(),
            tolerances, quantityTolerances, underDeliveryTolerances);
    }

    internal static PurchaseOrderResponse Map(
        PurchaseOrder o, string vendorName, IReadOnlyList<ReceiptSummary> receipts, IReadOnlyList<BillSummary> bills,
        IReadOnlyList<ReturnSummary> returns, IReadOnlyDictionary<Guid, decimal>? tolerances = null, IReadOnlyDictionary<Guid, decimal>? quantityTolerances = null,
        IReadOnlyDictionary<Guid, decimal>? underDeliveryTolerances = null) => new(
        o.Id.Value, o.Number, o.VendorId.Value, vendorName, o.OrderDate, o.Notes, o.Status, o.Total,
        o.Lines.Select(l => new PurchaseOrderLineResponse(
            l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitCost, l.LineTotal, l.QuantityReceived, l.QuantityBilled,
            l.QuantityReturned, l.QuantityDebited, l.DebitableQuantity,
            tolerances?.GetValueOrDefault(l.ItemId), quantityTolerances?.GetValueOrDefault(l.ItemId), underDeliveryTolerances?.GetValueOrDefault(l.ItemId))).ToArray(),
        receipts, bills, returns);
}
