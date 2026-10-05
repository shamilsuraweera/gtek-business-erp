using Erp.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;

namespace Erp.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DomainException => (StatusCodes.Status400BadRequest, "Invalid company data"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Company conflict"),
            _ => (0, string.Empty)
        };

        if (status == 0)
        {
            logger.LogError(exception, "Unhandled request exception.");
            return false;
        }

        logger.LogWarning(exception, "Handled API exception: {Title}", title);
        context.Response.StatusCode = status;
        await Results.Problem(statusCode: status, title: title, detail: title).ExecuteAsync(context);
        return true;
    }
}
