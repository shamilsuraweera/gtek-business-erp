using Microsoft.AspNetCore.Diagnostics;
using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Infrastructure;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Infrastructure;
using Erp.Modules.Purchasing.Application;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Modules.Sales.Application;
using Erp.Modules.Sales.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Erp")
    ?? "Host=localhost;Port=5432;Database=gtek_erp;Username=postgres";

builder.Services
    .AddPlatformModule()
    .AddFinanceModule()
    .AddSalesModule()
    .AddPurchasingModule()
    .AddInventoryModule()
    .AddPlatformInfrastructure(connectionString)
    .AddFinanceInfrastructure(connectionString)
    .AddSalesInfrastructure(connectionString)
    .AddPurchasingInfrastructure(connectionString)
    .AddInventoryInfrastructure(connectionString);

var app = builder.Build();
app.UseExceptionHandler();
app.MapOpenApi();

app.MapHealthChecks("/api/v1/health");
app.MapGet("/api/v1/companies", () => Results.Ok(Array.Empty<object>()));
app.MapGet("/api/v1/finance/accounts", () => Results.Ok(Array.Empty<object>()));

app.Run();

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled request exception.");
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem("An unexpected error occurred.", statusCode: 500).ExecuteAsync(httpContext);
        return true;
    }
}

public partial class Program;
