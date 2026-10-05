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
using Erp.Api.Endpoints;
using Erp.Api.Middleware;
using Erp.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddLocalAuthentication(builder.Configuration, builder.Environment);

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
app.UseAuthentication();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/api/v1/health").WithMetadata(new SystemEndpointMetadata());
app.MapCompanyEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapGet("/api/v1/finance/accounts", () => TypedResults.Ok(Array.Empty<object>()))
    .RequireCompanyContext()
    .WithSummary("List finance accounts")
    .WithDescription("Representative company-scoped finance endpoint.")
    .Produces<object[]>(StatusCodes.Status200OK);

app.Run();

public partial class Program;
