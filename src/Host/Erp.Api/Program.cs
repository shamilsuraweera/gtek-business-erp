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
using Erp.Api.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddLocalAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCors(options => options.AddPolicy("DevFrontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

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
app.UseCors("DevFrontend");
app.UseAuthentication();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/api/v1/health").WithMetadata(new SystemEndpointMetadata());
app.MapCompanyEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapPermissionEndpoints();
app.MapAuditEndpoints();
app.MapNumberSequenceEndpoints();
app.MapAccountingPeriodEndpoints();
app.MapFinanceEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();

if (args.Contains("--seed"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("--seed is only allowed in the Development environment.");
    await DevSeeder.RunAsync(app.Services);
    return;
}

app.Run();

public partial class Program;
