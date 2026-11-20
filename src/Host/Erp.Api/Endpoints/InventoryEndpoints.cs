using Erp.Api.Authentication;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Platform.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var items = endpoints.MapGroup("/api/v1/inventory/items").WithTags("Inventory").RequireCompanyContext();
        items.MapGet("/", async (IItemService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Inventory.ItemsRead);
        items.MapGet("/{id:guid}", async Task<Results<Ok<ItemResponse>, NotFound>>(Guid id, IItemService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new ItemId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Inventory.ItemsRead);
        items.MapPost("/", async (CreateItemRequest request, IItemService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/inventory/items/{result.Id}", result);
        }).RequirePermission(Permissions.Inventory.ItemsManage);
        items.MapPut("/{id:guid}", async Task<Results<Ok<ItemResponse>, NotFound>>(
            Guid id, UpdateItemRequest request, IItemService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(new ItemId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Inventory.ItemsManage);
        items.MapPost("/{id:guid}/activate", SetActive(true)).RequirePermission(Permissions.Inventory.ItemsManage);
        items.MapPost("/{id:guid}/deactivate", SetActive(false)).RequirePermission(Permissions.Inventory.ItemsManage);

        var locations = endpoints.MapGroup("/api/v1/inventory/locations").WithTags("Inventory").RequireCompanyContext();
        locations.MapGet("/", async (ILocationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Inventory.ItemsRead);
        locations.MapPost("/", async (CreateLocationRequest request, ILocationService service, CancellationToken ct) =>
            TypedResults.Created("/api/v1/inventory/locations", await service.CreateAsync(request, ct)))
            .RequirePermission(Permissions.Inventory.ItemsManage);

        var stock = endpoints.MapGroup("/api/v1/inventory/stock").WithTags("Inventory").RequireCompanyContext();
        stock.MapGet("/", async (IStockService service, CancellationToken ct) =>
            TypedResults.Ok(await service.OnHandAsync(ct))).RequirePermission(Permissions.Inventory.StockRead);
        stock.MapGet("/ledger", async (Guid? itemId, int? take, IStockService service, CancellationToken ct) =>
            TypedResults.Ok(await service.LedgerAsync(itemId, take ?? 100, ct))).RequirePermission(Permissions.Inventory.StockRead);
        stock.MapPost("/receipts", async (ReceiveStockRequest request, IStockService service, CancellationToken ct) =>
            TypedResults.Created("/api/v1/inventory/stock/ledger", await service.ReceiveAsync(request, ct)))
            .RequirePermission(Permissions.Inventory.StockManage);
        stock.MapPost("/adjustments", async (AdjustStockRequest request, IStockService service, CancellationToken ct) =>
            TypedResults.Created("/api/v1/inventory/stock/ledger", await service.AdjustAsync(request, ct)))
            .RequirePermission(Permissions.Inventory.StockManage);
        return endpoints;
    }

    private static Delegate SetActive(bool active) =>
        async Task<Results<Ok<ItemResponse>, NotFound>>(Guid id, IItemService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(new ItemId(id), active, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}
