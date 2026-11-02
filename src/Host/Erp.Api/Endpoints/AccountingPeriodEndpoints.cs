using Erp.Api.Authentication;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class AccountingPeriodEndpoints
{
    public static IEndpointRouteBuilder MapAccountingPeriodEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/accounting-periods")
            .WithTags("Accounting periods")
            .RequireCompanyContext();

        group.MapGet("/", async (IAccountingPeriodService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)))
            .RequirePermission(Permissions.Platform.AccountingPeriodsRead);

        group.MapGet("/{id:guid}", async Task<Results<Ok<AccountingPeriodResponse>, NotFound>>(
            Guid id, IAccountingPeriodService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new AccountingPeriodId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.AccountingPeriodsRead);

        group.MapPost("/", async (CreateAccountingPeriodRequest request, IAccountingPeriodService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/accounting-periods/{result.Id}", result);
        }).RequirePermission(Permissions.Platform.AccountingPeriodsManage);

        group.MapPut("/{id:guid}/name", async Task<Results<Ok<AccountingPeriodResponse>, NotFound>>(
            Guid id, RenameAccountingPeriodRequest request, IAccountingPeriodService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(new AccountingPeriodId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.AccountingPeriodsManage);

        group.MapPost("/{id:guid}/close", ChangeStatus(AccountingPeriodStatus.Closed))
            .RequirePermission(Permissions.Platform.AccountingPeriodsManage);
        group.MapPost("/{id:guid}/reopen", ChangeStatus(AccountingPeriodStatus.Open))
            .RequirePermission(Permissions.Platform.AccountingPeriodsManage);
        group.MapPost("/{id:guid}/lock", ChangeStatus(AccountingPeriodStatus.Locked))
            .RequirePermission(Permissions.Platform.AccountingPeriodsManage);

        return endpoints;
    }

    private static Delegate ChangeStatus(AccountingPeriodStatus status) =>
        async Task<Results<Ok<AccountingPeriodResponse>, NotFound>>(
            Guid id, IAccountingPeriodService service, CancellationToken ct) =>
        {
            var result = await service.ChangeStatusAsync(new AccountingPeriodId(id), status, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}
