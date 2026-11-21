using Erp.Application.Abstractions;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales.Application;

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
public sealed record InvoiceLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal QuantityCredited,
    string? TaxCode, decimal TaxRate, decimal TaxAmount);
public sealed record InvoiceResponse(
    Guid Id, string Number, Guid SalesOrderId, Guid CustomerId, string CustomerName, DateOnly InvoiceDate, DateOnly DueDate,
    decimal Subtotal, decimal TaxTotal, decimal Total, decimal AmountPaid, decimal AmountCredited, decimal Balance, InvoiceStatus Status, bool IsOverdue, string JournalNumber,
    IReadOnlyList<InvoiceLineResponse> Lines);

public sealed record RecordPaymentRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, string? Reference);
public sealed record PaymentResponse(
    Guid Id, string Number, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, string CustomerName,
    DateOnly PaymentDate, decimal Amount, PaymentMethod Method, string Reference, string JournalNumber);

/// <param name="ReturnToStock">True when the goods physically came back and should re-enter stock (at their original cost).</param>
public sealed record CreditNoteLineRequest(Guid ItemId, decimal Quantity, bool ReturnToStock);
public sealed record CreateCreditNoteRequest(DateOnly? Date, string Reason, IReadOnlyList<CreditNoteLineRequest> Lines);
public sealed record CreditNoteLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal, bool ReturnedToStock,
    decimal TaxRate, decimal TaxAmount);
public sealed record RefundRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, string? Reference);
public sealed record RefundResponse(
    Guid Id, string Number, Guid CreditNoteId, string CreditNoteNumber, Guid CustomerId, string CustomerName,
    DateOnly RefundDate, decimal Amount, PaymentMethod Method, string Reference, string JournalNumber);
public sealed record CreditNoteResponse(
    Guid Id, string Number, Guid SalesInvoiceId, string InvoiceNumber, Guid CustomerId, string CustomerName, DateOnly CreditDate,
    string Reason, decimal Subtotal, decimal TaxTotal, decimal Total, decimal AmountApplied, decimal AmountRefunded, decimal RefundDue, decimal ReturnedCost,
    string JournalNumber, IReadOnlyList<CreditNoteLineResponse> Lines, IReadOnlyList<RefundResponse> Refunds);

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
    /// <summary>Open invoices whose due date is before <paramref name="today"/>, oldest due first.</summary>
    Task<IReadOnlyList<SalesInvoice>> ListOverdueInvoicesAsync(CompanyId companyId, DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesInvoice>> ListInvoicesAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<SalesInvoice?> GetInvoiceAsync(CompanyId companyId, SalesInvoiceId id, CancellationToken cancellationToken);

    Task AddCreditNoteAsync(CreditNote note, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditNote>> ListCreditNotesAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<CreditNote?> GetCreditNoteAsync(CompanyId companyId, CreditNoteId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditNote>> ListCreditNotesForInvoiceAsync(CompanyId companyId, SalesInvoiceId invoiceId, CancellationToken cancellationToken);
    Task AddRefundAsync(CustomerRefund refund, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerRefund>> ListRefundsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);

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

public interface ICreditNoteService
{
    Task<CreditNoteResponse?> CreateAsync(SalesInvoiceId invoiceId, CreateCreditNoteRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditNoteResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<CreditNoteResponse?> GetAsync(CreditNoteId id, CancellationToken cancellationToken);
    Task<RefundResponse?> RefundAsync(CreditNoteId id, RefundRequest request, CancellationToken cancellationToken);
}

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<ICreditNoteService, CreditNoteService>();
        services.AddScoped<IReminderJob, OverdueInvoiceReminder>();
        return services;
    }
}
