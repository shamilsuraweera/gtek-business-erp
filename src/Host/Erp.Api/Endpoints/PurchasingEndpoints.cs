using Erp.Api.Authentication;
using Erp.Modules.Platform.Application;
using Erp.Modules.Purchasing.Application;
using Erp.Modules.Purchasing.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class PurchasingEndpoints
{
    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var vendors = endpoints.MapGroup("/api/v1/purchasing/vendors").WithTags("Purchasing").RequireCompanyContext();
        vendors.MapGet("/", async (IVendorService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Purchasing.VendorsRead);
        vendors.MapGet("/{id:guid}", async Task<Results<Ok<VendorResponse>, NotFound>>(Guid id, IVendorService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new VendorId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Purchasing.VendorsRead);
        vendors.MapPost("/", async (CreateVendorRequest request, IVendorService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/purchasing/vendors/{result.Id}", result);
        }).RequirePermission(Permissions.Purchasing.VendorsManage);
        vendors.MapPut("/{id:guid}", async Task<Results<Ok<VendorResponse>, NotFound>>(
            Guid id, UpdateVendorRequest request, IVendorService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(new VendorId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Purchasing.VendorsManage);
        vendors.MapPost("/{id:guid}/activate", SetActive(true)).RequirePermission(Permissions.Purchasing.VendorsManage);
        vendors.MapPost("/{id:guid}/deactivate", SetActive(false)).RequirePermission(Permissions.Purchasing.VendorsManage);

        var orders = endpoints.MapGroup("/api/v1/purchasing/orders").WithTags("Purchasing").RequireCompanyContext();
        orders.MapGet("/", async (int? take, IPurchaseOrderService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(take ?? 100, ct))).RequirePermission(Permissions.Purchasing.OrdersRead);
        orders.MapGet("/{id:guid}", async Task<Results<Ok<PurchaseOrderResponse>, NotFound>>(Guid id, IPurchaseOrderService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new PurchaseOrderId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Purchasing.OrdersRead);
        orders.MapPost("/", async (CreatePurchaseOrderRequest request, IPurchaseOrderService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/purchasing/orders/{result.Id}", result);
        }).RequirePermission(Permissions.Purchasing.OrdersManage);
        orders.MapPost("/{id:guid}/open", Transition(PurchaseOrderAction.Open)).RequirePermission(Permissions.Purchasing.OrdersManage);
        orders.MapPost("/{id:guid}/release", Transition(PurchaseOrderAction.Release)).RequirePermission(Permissions.Purchasing.OrdersManage);
        orders.MapPost("/{id:guid}/cancel", Transition(PurchaseOrderAction.Cancel)).RequirePermission(Permissions.Purchasing.OrdersManage);
        orders.MapPost("/{id:guid}/receive", async Task<Results<Ok<PurchaseOrderResponse>, NotFound>>(
            Guid id, ReceiveGoodsRequest? request, IPurchaseOrderService service, CancellationToken ct) =>
        {
            var result = await service.ReceiveAsync(new PurchaseOrderId(id), request ?? new ReceiveGoodsRequest(null), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Purchasing.OrdersReceive);
        orders.MapPost("/{id:guid}/bill", async Task<Results<Created<BillResponse>, NotFound>>(
            Guid id, CreateBillRequest? request, IBillService service, CancellationToken ct) =>
        {
            var result = await service.CreateFromOrderAsync(new PurchaseOrderId(id), request ?? new CreateBillRequest(null, null), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/purchasing/bills/{result.Id}", result);
        }).RequirePermission(Permissions.Purchasing.BillsPost);

        var bills = endpoints.MapGroup("/api/v1/purchasing/bills").WithTags("Purchasing").RequireCompanyContext();
        bills.MapGet("/", async (int? take, IBillService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(take ?? 100, ct))).RequirePermission(Permissions.Purchasing.BillsRead);
        bills.MapGet("/{id:guid}", async Task<Results<Ok<BillResponse>, NotFound>>(Guid id, IBillService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new VendorBillId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Purchasing.BillsRead);
        bills.MapPost("/{id:guid}/payments", async Task<Results<Created<VendorPaymentResponse>, NotFound>>(
            Guid id, PayBillRequest request, IBillService service, CancellationToken ct) =>
        {
            var result = await service.PayAsync(new VendorBillId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/purchasing/bills/{id}", result);
        }).RequirePermission(Permissions.Purchasing.PaymentsRecord);

        endpoints.MapGet("/api/v1/purchasing/payments", async (int? take, IBillService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListPaymentsAsync(take ?? 100, ct)))
            .WithTags("Purchasing").RequireCompanyContext().RequirePermission(Permissions.Purchasing.BillsRead);
        return endpoints;
    }

    private static Delegate SetActive(bool active) =>
        async Task<Results<Ok<VendorResponse>, NotFound>>(Guid id, IVendorService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(new VendorId(id), active, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };

    private static Delegate Transition(PurchaseOrderAction action) =>
        async Task<Results<Ok<PurchaseOrderResponse>, NotFound>>(Guid id, IPurchaseOrderService service, CancellationToken ct) =>
        {
            var result = await service.TransitionAsync(new PurchaseOrderId(id), action, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}
