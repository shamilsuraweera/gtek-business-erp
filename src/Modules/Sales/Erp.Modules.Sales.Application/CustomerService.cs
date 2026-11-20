using Erp.Application.Abstractions;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Sales.Application;

internal static class SalesCompany
{
    public static CompanyId Require(ICompanyContext context) =>
        context.HasCompany ? context.CompanyId : throw new InvalidOperationException("An active company context is required.");
}

public sealed class CustomerService(ISalesStore store, ICompanyContext companyContext, IClock clock, IAuditRecorder audit) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        if (await store.GetCustomerByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("A customer with this code already exists for the company.");
        var customer = Customer.Create(companyId, request.Code, request.Name, request.Email, request.Phone,
            request.PaymentTermsDays ?? 30, clock.UtcNow);
        await store.AddCustomerAsync(customer, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Sales", "customer.created", "Customer", customer.Id.Value.ToString(), customer.Code, true, null, cancellationToken);
        return Map(customer);
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListCustomersAsync(SalesCompany.Require(companyContext), cancellationToken)).Select(Map).ToArray();

    public async Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken)
    {
        var customer = await store.GetCustomerAsync(SalesCompany.Require(companyContext), id, cancellationToken);
        return customer is null ? null : Map(customer);
    }

    public async Task<CustomerResponse?> UpdateAsync(CustomerId id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await store.GetCustomerAsync(SalesCompany.Require(companyContext), id, cancellationToken);
        if (customer is null) return null;
        customer.Update(request.Name, request.Email, request.Phone, request.PaymentTermsDays ?? customer.PaymentTermsDays);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Sales", "customer.updated", "Customer", customer.Id.Value.ToString(), customer.Code, true, null, cancellationToken);
        return Map(customer);
    }

    public async Task<CustomerResponse?> SetActiveAsync(CustomerId id, bool active, CancellationToken cancellationToken)
    {
        var customer = await store.GetCustomerAsync(SalesCompany.Require(companyContext), id, cancellationToken);
        if (customer is null) return null;
        if (active) customer.Activate(); else customer.Deactivate();
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Sales", active ? "customer.activated" : "customer.deactivated", "Customer",
            customer.Id.Value.ToString(), customer.Code, true, null, cancellationToken);
        return Map(customer);
    }

    private static CustomerResponse Map(Customer c) => new(c.Id.Value, c.Code, c.Name, c.Email, c.Phone, c.PaymentTermsDays, c.IsActive);
}
