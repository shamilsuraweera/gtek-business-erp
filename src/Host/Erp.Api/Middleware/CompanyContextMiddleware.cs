using Erp.Modules.Platform.Application;
using Erp.Api.Endpoints;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Api.Middleware;

public sealed class CompanyContextMiddleware(RequestDelegate next, ILogger<CompanyContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, ActiveCompanyContext context, ICompanyService companyService)
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
