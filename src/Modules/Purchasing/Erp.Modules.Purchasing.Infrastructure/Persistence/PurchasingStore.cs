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

    public Task AddBillAsync(VendorBill bill, CancellationToken cancellationToken)
    {
        db.Bills.Add(bill);
        return Task.CompletedTask;
    }

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

    public async Task<T> InLockedTransactionAsync<T>(
        CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"purchasing:{companyId.Value}:{lockKey}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
