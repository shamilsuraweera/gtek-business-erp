using Erp.Modules.Platform.Application;
using Erp.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/companies").WithTags("Companies");

        group.MapPost("/", async (CreateCompanyRequest request, ICompanyService service, CancellationToken cancellationToken) =>
        {
            var response = await service.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"/api/v1/companies/{response.Id}", response);
        }).WithSummary("Create a company").WithMetadata(new SystemEndpointMetadata());

        group.MapGet("/", async (ICompanyService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListAsync(cancellationToken)))
            .WithSummary("List companies").WithMetadata(new SystemEndpointMetadata());

        group.MapGet("/{id:guid}", async Task<Results<Ok<CompanyResponse>, NotFound>> (
            Guid id, ICompanyService service, CancellationToken cancellationToken) =>
        {
            var response = await service.GetByIdAsync(new CompanyId(id), cancellationToken);
            return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
        }).WithSummary("Get a company").WithMetadata(new SystemEndpointMetadata());

        group.MapGet("/by-code/{code}", async Task<Results<Ok<CompanyResponse>, NotFound>> (
            string code, ICompanyService service, CancellationToken cancellationToken) =>
        {
            var response = await service.GetByCodeAsync(code, cancellationToken);
            return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
        }).WithSummary("Get a company by code").WithMetadata(new SystemEndpointMetadata());

        group.MapPut("/{id:guid}/name", async Task<Results<Ok<CompanyResponse>, NotFound>> (
            Guid id, RenameCompanyRequest request, ICompanyService service, CancellationToken cancellationToken) =>
        {
            var response = await service.RenameAsync(new CompanyId(id), request, cancellationToken);
            return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
        }).WithSummary("Rename a company").WithMetadata(new SystemEndpointMetadata());

        group.MapPost("/{id:guid}/activate", ChangeStatus(activate: true))
            .WithSummary("Activate a company").WithMetadata(new SystemEndpointMetadata());
        group.MapPost("/{id:guid}/deactivate", ChangeStatus(activate: false))
            .WithSummary("Deactivate a company").WithMetadata(new SystemEndpointMetadata());

        return endpoints;
    }

    private static Delegate ChangeStatus(bool activate) =>
        async Task<Results<Ok<CompanyResponse>, NotFound>> (Guid id, ICompanyService service, CancellationToken cancellationToken) =>
        {
            var response = activate
                ? await service.ActivateAsync(new CompanyId(id), cancellationToken)
                : await service.DeactivateAsync(new CompanyId(id), cancellationToken);
            return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
        };
}

public sealed class SystemEndpointMetadata;

public static class EndpointBuilderExtensions
{
    public static TBuilder RequireCompanyContext<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new CompanyScopedEndpointMetadata());
        return builder;
    }
}

public sealed class CompanyScopedEndpointMetadata;
