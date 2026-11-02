using Erp.Api.Authentication;
using Erp.Modules.Platform.Application;
using Erp.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class NumberSequenceEndpoints
{
    public static IEndpointRouteBuilder MapNumberSequenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/number-sequences")
            .WithTags("Number sequences")
            .RequireCompanyContext();

        group.MapGet("/", async (INumberSequenceService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)))
            .RequirePermission(Permissions.Platform.NumberSequencesRead);

        group.MapGet("/{id:guid}", async Task<Results<Ok<NumberSequenceResponse>, NotFound>>(
            Guid id, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(new NumberSequenceId(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.NumberSequencesRead);

        group.MapGet("/by-code/{code}", async Task<Results<Ok<NumberSequenceResponse>, NotFound>>(
            string code, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.GetByCodeAsync(code, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.NumberSequencesRead);

        group.MapPost("/", async (CreateNumberSequenceRequest request, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/number-sequences/{result.Id}", result);
        }).RequirePermission(Permissions.Platform.NumberSequencesManage);

        group.MapPut("/{id:guid}/name", async Task<Results<Ok<NumberSequenceResponse>, NotFound>>(
            Guid id, RenameNumberSequenceRequest request, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(new NumberSequenceId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.NumberSequencesManage);

        group.MapPut("/{id:guid}/configuration", async Task<Results<Ok<NumberSequenceResponse>, NotFound>>(
            Guid id, ConfigureNumberSequenceRequest request, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.ConfigureAsync(new NumberSequenceId(id), request, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequirePermission(Permissions.Platform.NumberSequencesManage);

        group.MapPost("/{id:guid}/activate", SetStatus(true)).RequirePermission(Permissions.Platform.NumberSequencesManage);
        group.MapPost("/{id:guid}/deactivate", SetStatus(false)).RequirePermission(Permissions.Platform.NumberSequencesManage);

        group.MapPost("/{code}/next", async Task<Results<Ok<GeneratedNumberResponse>, NotFound>>(
            string code, INumberSequenceService service, CancellationToken ct) =>
        {
            try
            {
                return TypedResults.Ok(new GeneratedNumberResponse(await service.GetNextAsync(code, ct)));
            }
            catch (KeyNotFoundException)
            {
                return TypedResults.NotFound();
            }
        }).RequirePermission(Permissions.Platform.NumberSequencesRead);

        return endpoints;
    }

    private static Delegate SetStatus(bool active) =>
        async Task<Results<Ok<NumberSequenceResponse>, NotFound>>(
            Guid id, INumberSequenceService service, CancellationToken ct) =>
        {
            var result = await service.SetStatusAsync(new NumberSequenceId(id), active, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        };
}

public sealed record GeneratedNumberResponse(string Number);
