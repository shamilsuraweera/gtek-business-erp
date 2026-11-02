using Erp.Modules.Platform.Application;
using Erp.Api.Endpoints;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Api.Middleware;

public sealed class CompanyContextMiddleware(RequestDelegate next, ILogger<CompanyContextMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ActiveCompanyContext context,
        ICompanyService companyService,
        ICompanyAccessAuthorizer accessAuthorizer,
        ICurrentUser currentUser)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<CompanyScopedEndpointMetadata>() is null)
        {
            await next(httpContext);
            return;
        }

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        if (currentUser.UserId is not { } userId)
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status401Unauthorized, "Authentication is required.");
            return;
        }

        if (!httpContext.Request.Headers.TryGetValue("X-Company-Id", out var header) ||
            !Guid.TryParse(header.SingleOrDefault(), out var companyId))
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status400BadRequest, "A valid X-Company-Id header is required.");
            return;
        }

        var company = await companyService.GetByIdAsync(new CompanyId(companyId), httpContext.RequestAborted);
        if (company is null)
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status404NotFound, "The requested company was not found.");
            return;
        }

        if (company.Status != CompanyStatus.Active)
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status409Conflict, "The requested company is inactive.");
            return;
        }

        if (!await accessAuthorizer.CanAccessCompanyAsync(userId, new CompanyId(companyId), httpContext.RequestAborted))
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status403Forbidden, "You do not have access to the requested company.");
            return;
        }

        context.Set(new CompanyId(companyId));
        using (logger.BeginScope(new Dictionary<string, object> { ["CompanyId"] = companyId }))
        {
            await next(httpContext);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        return Results.Problem(statusCode: statusCode, detail: detail, title: "Company context error")
            .ExecuteAsync(context);
    }
}
