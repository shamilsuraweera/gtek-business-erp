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
        return Map(order, vendor.Name, [], []);
    }

    public async Task<IReadOnlyList<PurchaseOrderResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var orders = await store.ListOrdersAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var names = (await store.ListVendorsAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        return orders.Select(o => Map(o, names.GetValueOrDefault(o.VendorId, string.Empty), [], [])).ToArray();
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
            var lines = wanted.Select(w =>
            {
                var line = order.LineFor(w.Item1);
                return new GoodsReceiptLine(line.ItemId, line.Sku, line.Description, w.Item2, line.UnitCost);
            }).ToList();

            var date = request.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await periodGuard.EnsurePostingAllowedAsync(date, ct);
            order.RecordReceipt(wanted); // validates quantities against the outstanding balance before anything is posted
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
                        new PostJournalLine(PurchasingAccounts.Inventory, total, 0, $"Receipt {number}"),
                        new PostJournalLine(PurchasingAccounts.GoodsReceivedNotInvoiced, 0, total, $"Receipt {number}")
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

    private async Task<PurchaseOrderResponse> MapFullAsync(PurchaseOrder order, string vendorName, CompanyId companyId, CancellationToken ct)
    {
        var receipts = await store.ListReceiptsForOrderAsync(companyId, order.Id, ct);
        var bills = await store.ListBillsForOrderAsync(companyId, order.Id, ct);
        return Map(order, vendorName,
            receipts.OrderBy(r => r.ReceiptDate).ThenBy(r => r.Number)
                .Select(r => new ReceiptSummary(r.Id.Value, r.Number, r.ReceiptDate, r.Reference, r.Total, r.JournalNumber)).ToArray(),
            bills.OrderBy(b => b.BillDate).ThenBy(b => b.Number)
                .Select(b => new BillSummary(b.Id.Value, b.Number, b.BillDate, b.Total, b.PriceVariance, b.Status)).ToArray());
    }

    internal static PurchaseOrderResponse Map(
        PurchaseOrder o, string vendorName, IReadOnlyList<ReceiptSummary> receipts, IReadOnlyList<BillSummary> bills) => new(
        o.Id.Value, o.Number, o.VendorId.Value, vendorName, o.OrderDate, o.Notes, o.Status, o.Total,
        o.Lines.Select(l => new PurchaseOrderLineResponse(
            l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitCost, l.LineTotal, l.QuantityReceived, l.QuantityBilled)).ToArray(),
        receipts, bills);
}
