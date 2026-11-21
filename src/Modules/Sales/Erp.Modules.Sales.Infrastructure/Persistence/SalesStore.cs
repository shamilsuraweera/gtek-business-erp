using Erp.Modules.Sales.Application;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure.Persistence;

public sealed class SalesStore(SalesDbContext db) : ISalesStore
{
    public Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken)
    {
        db.Customers.Add(customer);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Customer>> ListCustomersAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public Task<Customer?> GetCustomerAsync(CompanyId companyId, CustomerId id, CancellationToken cancellationToken) =>
        db.Customers.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<Customer?> GetCustomerByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = Customer.NormalizeCode(code);
        return db.Customers.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public Task AddOrderAsync(SalesOrder order, CancellationToken cancellationToken)
    {
        db.Orders.Add(order);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<SalesOrder?> GetOrderAsync(CompanyId companyId, SalesOrderId id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task AddInvoiceAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        db.Invoices.Add(invoice);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SalesInvoice>> ListOverdueInvoicesAsync(CompanyId companyId, DateOnly today, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == InvoiceStatus.Open && x.DueDate < today)
            .OrderBy(x => x.DueDate).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesInvoice>> ListInvoicesAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<SalesInvoice?> GetInvoiceAsync(CompanyId companyId, SalesInvoiceId id, CancellationToken cancellationToken) =>
        db.Invoices.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task AddCreditNoteAsync(CreditNote note, CancellationToken cancellationToken)
    {
        db.CreditNotes.Add(note);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CreditNote>> ListCreditNotesAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.CreditNotes.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<CreditNote?> GetCreditNoteAsync(CompanyId companyId, CreditNoteId id, CancellationToken cancellationToken) =>
        db.CreditNotes.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> ListCreditNotesForInvoiceAsync(CompanyId companyId, SalesInvoiceId invoiceId, CancellationToken cancellationToken) =>
        await db.CreditNotes.AsNoTracking().Where(x => x.CompanyId == companyId && x.SalesInvoiceId == invoiceId).ToListAsync(cancellationToken);

    public Task AddRefundAsync(CustomerRefund refund, CancellationToken cancellationToken)
    {
        db.Refunds.Add(refund);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CustomerRefund>> ListRefundsAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Refunds.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task AddPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        db.Payments.Add(payment);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Payment>> ListPaymentsAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Payments.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public async Task<T> InLockedTransactionAsync<T>(
        CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"sales:{companyId.Value}:{lockKey}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
