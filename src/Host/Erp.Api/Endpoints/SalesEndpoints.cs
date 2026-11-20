using Erp.Api.Authentication;
using Erp.Modules.Platform.Application;
using Erp.Modules.Sales.Application;
using Erp.Modules.Sales.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/api/v1/sales/customers").WithTags("Sales").RequireCompanyContext();
        customers.MapGet("/", async (ICustomerService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Sales.CustomersRead);
        customers.MapGet("/{id:guid}", async Task<Results<Ok<CustomerResponse>, NotFound>>(Guid id, ICustomerService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new CustomerId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Sales.CustomersRead);
        customers.MapPost("/", async (CreateCustomerRequest request, ICustomerService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/sales/customers/{result.Id}", result);
        }).RequirePermission(Permissions.Sales.CustomersManage);
        customers.MapPut("/{id:guid}", async Task<Results<Ok<CustomerResponse>, NotFound>>(
            Guid id, UpdateCustomerRequest request, ICustomerService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(new CustomerId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Sales.CustomersManage);
        customers.MapPost("/{id:guid}/activate", SetActive(true)).RequirePermission(Permissions.Sales.CustomersManage);
        customers.MapPost("/{id:guid}/deactivate", SetActive(false)).RequirePermission(Permissions.Sales.CustomersManage);

        var orders = endpoints.MapGroup("/api/v1/sales/orders").WithTags("Sales").RequireCompanyContext();
        orders.MapGet("/", async (int? take, IOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(take ?? 100, ct))).RequirePermission(Permissions.Sales.OrdersRead);
        orders.MapGet("/{id:guid}", async Task<Results<Ok<OrderResponse>, NotFound>>(Guid id, IOrderService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new SalesOrderId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Sales.OrdersRead);
        orders.MapPost("/", async (CreateOrderRequest request, IOrderService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/sales/orders/{result.Id}", result);
        }).RequirePermission(Permissions.Sales.OrdersManage);
        orders.MapPost("/{id:guid}/open", Transition(OrderAction.Open)).RequirePermission(Permissions.Sales.OrdersManage);
        orders.MapPost("/{id:guid}/release", Transition(OrderAction.Release)).RequirePermission(Permissions.Sales.OrdersManage);
        orders.MapPost("/{id:guid}/cancel", Transition(OrderAction.Cancel)).RequirePermission(Permissions.Sales.OrdersManage);
        orders.MapPost("/{id:guid}/invoice", async Task<Results<Created<InvoiceResponse>, NotFound>>(
            Guid id, CreateInvoiceRequest? request, IInvoiceService service, CancellationToken ct) =>
        {
            var result = await service.CreateFromOrderAsync(new SalesOrderId(id), request ?? new CreateInvoiceRequest(null), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/sales/invoices/{result.Id}", result);
        }).RequirePermission(Permissions.Sales.InvoicesPost);

        var invoices = endpoints.MapGroup("/api/v1/sales/invoices").WithTags("Sales").RequireCompanyContext();
        invoices.MapGet("/", async (int? take, IInvoiceService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(take ?? 100, ct))).RequirePermission(Permissions.Sales.InvoicesRead);
        invoices.MapGet("/{id:guid}", async Task<Results<Ok<InvoiceResponse>, NotFound>>(Guid id, IInvoiceService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new SalesInvoiceId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Sales.InvoicesRead);
        invoices.MapPost("/{id:guid}/payments", async Task<Results<Created<PaymentResponse>, NotFound>>(
            Guid id, RecordPaymentRequest request, IInvoiceService service, CancellationToken ct) =>
        {
            var result = await service.RecordPaymentAsync(new SalesInvoiceId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/sales/invoices/{id}", result);
        }).RequirePermission(Permissions.Sales.PaymentsRecord);

        endpoints.MapGet("/api/v1/sales/payments", async (int? take, IInvoiceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListPaymentsAsync(take ?? 100, ct)))
            .WithTags("Sales").RequireCompanyContext().RequirePermission(Permissions.Sales.InvoicesRead);
        return endpoints;
    }

    private static Delegate SetActive(bool active) =>
        async Task<Results<Ok<CustomerResponse>, NotFound>>(Guid id, ICustomerService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(new CustomerId(id), active, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };

    private static Delegate Transition(OrderAction action) =>
        async Task<Results<Ok<OrderResponse>, NotFound>>(Guid id, IOrderService service, CancellationToken ct) =>
        {
            var result = await service.TransitionAsync(new SalesOrderId(id), action, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}
