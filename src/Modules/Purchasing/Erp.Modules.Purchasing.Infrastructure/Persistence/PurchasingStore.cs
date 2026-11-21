using Erp.Modules.Purchasing.Application;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingStore(PurchasingDbContext db) : IPurchasingStore
{
    public Task AddVendorAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        db.Vendors.Add(vendor);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Vendor>> ListVendorsAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Vendors.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public Task<Vendor?> GetVendorAsync(CompanyId companyId, VendorId id, CancellationToken cancellationToken) =>
        db.Vendors.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<Vendor?> GetVendorByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = Vendor.NormalizeCode(code);
        return db.Vendors.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken)
    {
        db.Orders.Add(order);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<PurchaseOrder?> GetOrderAsync(CompanyId companyId, PurchaseOrderId id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken)
    {
        db.Receipts.Add(receipt);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<GoodsReceipt>> ListReceiptsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken) =>
        await db.Receipts.AsNoTracking().Where(x => x.CompanyId == companyId && x.PurchaseOrderId == orderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<VendorBill>> ListBillsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken) =>
        await db.Bills.AsNoTracking().Where(x => x.CompanyId == companyId && x.PurchaseOrderId == orderId).ToListAsync(cancellationToken);

    public Task<bool> BillExistsForVendorReferenceAsync(CompanyId companyId, VendorId vendorId, string vendorReference, CancellationToken cancellationToken) =>
        // Vendor invoice numbers are compared case-insensitively: "inv-7" and "INV-7" are the same document.
        db.Bills.AnyAsync(
            x => x.CompanyId == companyId && x.VendorId == vendorId && x.VendorReference != null &&
                 x.VendorReference.ToLower() == vendorReference.ToLower(),
            cancellationToken);

    public Task AddReturnAsync(VendorReturn vendorReturn, CancellationToken cancellationToken)
    {
        db.Returns.Add(vendorReturn);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VendorReturn>> ListReturnsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken) =>
        await db.Returns.AsNoTracking().Where(x => x.CompanyId == companyId && x.PurchaseOrderId == orderId).ToListAsync(cancellationToken);

    public async Task<PurchaseOrderId?> GetBillOrderIdAsync(CompanyId companyId, VendorBillId billId, CancellationToken cancellationToken)
    {
        var ids = await db.Bills.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == billId)
            .Select(x => x.PurchaseOrderId).ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }

    public Task AddDebitNoteAsync(DebitNote note, CancellationToken cancellationToken)
    {
        db.DebitNotes.Add(note);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DebitNote>> ListDebitNotesAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.DebitNotes.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<DebitNote?> GetDebitNoteAsync(CompanyId companyId, DebitNoteId id, CancellationToken cancellationToken) =>
        db.DebitNotes.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DebitNote>> ListDebitNotesForBillAsync(CompanyId companyId, VendorBillId billId, CancellationToken cancellationToken) =>
        await db.DebitNotes.AsNoTracking().Where(x => x.CompanyId == companyId && x.VendorBillId == billId).ToListAsync(cancellationToken);

    public Task AddVendorRefundAsync(VendorRefund refund, CancellationToken cancellationToken)
    {
        db.VendorRefunds.Add(refund);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VendorRefund>> ListVendorRefundsAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.VendorRefunds.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task AddBillAsync(VendorBill bill, CancellationToken cancellationToken)
    {
        db.Bills.Add(bill);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VendorBill>> ListOverdueBillsAsync(CompanyId companyId, DateOnly today, CancellationToken cancellationToken) =>
        await db.Bills.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == BillStatus.Open && x.DueDate < today)
            .OrderBy(x => x.DueDate).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<VendorBill>> ListBillsAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Bills.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<VendorBill?> GetBillAsync(CompanyId companyId, VendorBillId id, CancellationToken cancellationToken) =>
        db.Bills.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task AddPaymentAsync(VendorPayment payment, CancellationToken cancellationToken)
    {
        db.Payments.Add(payment);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VendorPayment>> ListPaymentsAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Payments.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<T> InLockedTransactionAsync<T>(
        CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        InLockedTransactionAsync(companyId, [lockKey], work, cancellationToken);

    public async Task<T> InLockedTransactionAsync<T>(
        CompanyId companyId, IReadOnlyCollection<string> lockKeys, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var lockKey in lockKeys.Distinct().Order(StringComparer.Ordinal))
        {
            var key = $"purchasing:{companyId.Value}:{lockKey}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }

        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task AddToleranceRequestAsync(ToleranceRequest request, CancellationToken cancellationToken)
    {
        db.ToleranceRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task<ToleranceRequest?> GetToleranceRequestAsync(CompanyId companyId, ToleranceRequestId id, CancellationToken cancellationToken) =>
        db.ToleranceRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public async Task<PurchaseOrderId?> GetToleranceRequestOrderIdAsync(CompanyId companyId, ToleranceRequestId id, CancellationToken cancellationToken)
    {
        var ids = await db.ToleranceRequests.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == id)
            .Select(x => x.PurchaseOrderId).ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }

    public async Task<IReadOnlyList<ToleranceRequest>> ListToleranceRequestsAsync(
        CompanyId companyId, ToleranceRequestStatus? status, PurchaseOrderId? orderId, int take, CancellationToken cancellationToken)
    {
        var query = db.ToleranceRequests.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (status is { } s) query = query.Where(x => x.Status == s);
        if (orderId is { } o) query = query.Where(x => x.PurchaseOrderId == o);
        return await query.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);
    }

    public Task<bool> HasPendingToleranceRequestAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken) =>
        db.ToleranceRequests.AnyAsync(x => x.CompanyId == companyId && x.PurchaseOrderId == orderId && x.Status == ToleranceRequestStatus.Pending, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
