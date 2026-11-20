using Erp.Api.Authentication;
using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Domain;
using Erp.Modules.Platform.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accounts = endpoints.MapGroup("/api/v1/finance/accounts").WithTags("Finance").RequireCompanyContext();
        accounts.MapGet("/", async (IAccountService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Finance.AccountsRead);
        accounts.MapPost("/", async (CreateAccountRequest request, IAccountService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/finance/accounts/{result.Id}", result);
        }).RequirePermission(Permissions.Finance.AccountsManage);
        accounts.MapPut("/{id:guid}/name", async Task<Results<Ok<AccountResponse>, NotFound>>(
            Guid id, RenameAccountRequest request, IAccountService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(new AccountId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Finance.AccountsManage);
        accounts.MapPost("/{id:guid}/activate", SetActive(true)).RequirePermission(Permissions.Finance.AccountsManage);
        accounts.MapPost("/{id:guid}/deactivate", SetActive(false)).RequirePermission(Permissions.Finance.AccountsManage);

        var mappings = endpoints.MapGroup("/api/v1/finance/account-mappings").WithTags("Finance").RequireCompanyContext();
        mappings.MapGet("/", async (IAccountMappingService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct))).RequirePermission(Permissions.Finance.AccountsRead);
        mappings.MapPut("/{role}", async (string role, SetAccountMappingRequest request, IAccountMappingService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SetAsync(role, request, ct))).RequirePermission(Permissions.Finance.MappingManage);
        mappings.MapDelete("/{role}", async (string role, IAccountMappingService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ResetAsync(role, ct))).RequirePermission(Permissions.Finance.MappingManage);

        var journals = endpoints.MapGroup("/api/v1/finance/journals").WithTags("Finance").RequireCompanyContext();
        journals.MapGet("/", async (int? take, IJournalService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(take ?? 50, ct))).RequirePermission(Permissions.Finance.JournalsRead);
        journals.MapGet("/{id:guid}", async Task<Results<Ok<JournalResponse>, NotFound>>(
            Guid id, IJournalService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new JournalId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Finance.JournalsRead);
        journals.MapPost("/", async (CreateJournalRequest request, IJournalService service, CancellationToken ct) =>
        {
            var result = await service.CreateAndPostAsync(request, ct);
            return TypedResults.Created($"/api/v1/finance/journals/{result.Id}", result);
        }).RequirePermission(Permissions.Finance.JournalsPost);
        journals.MapPost("/{id:guid}/reverse", async Task<Results<Created<JournalResponse>, NotFound>>(
            Guid id, ReverseJournalRequest request, IJournalService service, CancellationToken ct) =>
        {
            var result = await service.ReverseAsync(new JournalId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Created($"/api/v1/finance/journals/{result.Id}", result);
        }).RequirePermission(Permissions.Finance.JournalsPost);

        endpoints.MapGet("/api/v1/finance/trial-balance", async (DateOnly? asOf, IFinanceReportService service, CancellationToken ct) =>
                TypedResults.Ok(await service.TrialBalanceAsync(asOf, ct)))
            .WithTags("Finance").RequireCompanyContext().RequirePermission(Permissions.Finance.ReportsRead);
        return endpoints;
    }

    private static Delegate SetActive(bool active) =>
        async Task<Results<Ok<AccountResponse>, NotFound>>(Guid id, IAccountService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(new AccountId(id), active, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}
