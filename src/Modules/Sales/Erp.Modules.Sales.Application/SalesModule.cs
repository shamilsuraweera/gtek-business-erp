using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales.Application;

/// <summary>GL account codes used by sales postings. MVP: fixed chart-of-accounts mapping (seeded by the dev seeder).</summary>
public static class SalesAccounts
{
    public const string Cash = "1000";
    public const string Bank = "1010";
    public const string Receivable = "1100";
    public const string Inventory = "1200";
    public const string Revenue = "4000";
    public const string CostOfGoodsSold = "5000";
}

public sealed record CustomerResponse(Guid Id, string Code, string Name, string? Email, string? Phone, int PaymentTermsDays, bool IsActive);
public sealed record CreateCustomerRequest(string Code, string Name, string? Email, string? Phone, int? PaymentTermsDays);
public sealed record UpdateCustomerRequest(string Name, string? Email, string? Phone, int? PaymentTermsDays);

public sealed record OrderLineRequest(Guid ItemId, decimal Quantity, decimal? UnitPrice);
public sealed record CreateOrderRequest(Guid CustomerId, DateOnly? OrderDate, string? Notes, IReadOnlyList<OrderLineRequest> Lines);
public sealed record OrderLineResponse(Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record OrderResponse(
    Guid Id, string Number, Guid CustomerId, string CustomerName, DateOnly OrderDate, string? Notes,
    SalesOrderStatus Status, decimal Total, Guid? InvoiceId, IReadOnlyList<OrderLineResponse> Lines);

public sealed record CreateInvoiceRequest(DateOnly? Date);
public sealed record InvoiceLineResponse(Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record InvoiceResponse(
    Guid Id, string Number, Guid SalesOrderId, Guid CustomerId, string CustomerName, DateOnly InvoiceDate, DateOnly DueDate,
    decimal Total, decimal AmountPaid, decimal Balance, InvoiceStatus Status, bool IsOverdue, string JournalNumber,
    IReadOnlyList<InvoiceLineResponse> Lines);

public sealed record RecordPaymentRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, string? Reference);
public sealed record PaymentResponse(
    Guid Id, string Number, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, string CustomerName,
    DateOnly PaymentDate, decimal Amount, PaymentMethod Method, string Reference, string JournalNumber);

public interface ISalesStore
{
    Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> ListCustomersAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<Customer?> GetCustomerAsync(CompanyId companyId, CustomerId id, CancellationToken cancellationToken);
    Task<Customer?> GetCustomerByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken);

    Task AddOrderAsync(SalesOrder order, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesOrder>> ListOrdersAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<SalesOrder?> GetOrderAsync(CompanyId companyId, SalesOrderId id, CancellationToken cancellationToken);

    Task AddInvoiceAsync(SalesInvoice invoice, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesInvoice>> ListInvoicesAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<SalesInvoice?> GetInvoiceAsync(CompanyId companyId, SalesInvoiceId id, CancellationToken cancellationToken);

    Task AddPaymentAsync(Payment payment, CancellationToken cancellationToken);
    Task<IReadOnlyList<Payment>> ListPaymentsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> in a transaction holding an exclusive lock on <paramref name="lockKey"/>.</summary>
    Task<T> InLockedTransactionAsync<T>(CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerResponse>> ListAsync(CancellationToken cancellationToken);
    Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken);
    Task<CustomerResponse?> UpdateAsync(CustomerId id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse?> SetActiveAsync(CustomerId id, bool active, CancellationToken cancellationToken);
}

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<OrderResponse?> GetAsync(SalesOrderId id, CancellationToken cancellationToken);
    Task<OrderResponse?> TransitionAsync(SalesOrderId id, OrderAction action, CancellationToken cancellationToken);
}

public enum OrderAction { Open, Release, Cancel }

public interface IInvoiceService
{
    Task<InvoiceResponse?> CreateFromOrderAsync(SalesOrderId orderId, CreateInvoiceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InvoiceResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<InvoiceResponse?> GetAsync(SalesInvoiceId id, CancellationToken cancellationToken);
    Task<PaymentResponse?> RecordPaymentAsync(SalesInvoiceId id, RecordPaymentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentResponse>> ListPaymentsAsync(int take, CancellationToken cancellationToken);
}

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        return services;
    }
}
