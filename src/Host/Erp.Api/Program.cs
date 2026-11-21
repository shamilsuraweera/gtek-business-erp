using Erp.Application.Abstractions;
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

// Secrets as files (Docker and Kubernetes secrets): a file named like the setting, with "__" for ":", holds its value, e.g. /run/secrets/Email__Password.
// Read last, so a secret file wins over appsettings and environment variables. The folder is optional; ERP_SECRETS_DIR moves it (ADR 0038).
builder.Configuration.AddKeyPerFile(Environment.GetEnvironmentVariable("ERP_SECRETS_DIR") ?? "/run/secrets", optional: true);

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

// Notification retention (ADR 0024). A bad value fails at start-up with every problem listed, instead of silently purging the wrong thing.
var retention = builder.Configuration.GetSection(NotificationRetentionOptions.Section).Get<NotificationRetentionOptions>() ?? new NotificationRetentionOptions();
if (retention.Validate() is { Count: > 0 } retentionErrors)
    throw new InvalidOperationException("Invalid notification retention configuration: " + string.Join(" ", retentionErrors));
builder.Services.AddSingleton(retention);

// Email delivery (ADR 0026): off unless configured; when on, a bad value stops start-up with every problem listed.
var email = builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
if (email.Validate() is { Count: > 0 } emailErrors)
    throw new InvalidOperationException("Invalid email configuration: " + string.Join(" ", emailErrors));
builder.Services.AddSingleton(email);
if (email.Enabled) builder.Services.AddHostedService<Erp.Api.Background.EmailDeliveryWorker>();
if (retention.Enabled) builder.Services.AddHostedService<Erp.Api.Background.NotificationRetentionWorker>();

// Scheduled reminders such as overdue invoices and bills (ADR 0029).
var reminders = builder.Configuration.GetSection(ReminderOptions.Section).Get<ReminderOptions>() ?? new ReminderOptions();
if (reminders.Validate() is { Count: > 0 } reminderErrors)
    throw new InvalidOperationException("Invalid reminder configuration: " + string.Join(" ", reminderErrors));
builder.Services.AddSingleton(reminders);
if (reminders.Enabled) builder.Services.AddHostedService<Erp.Api.Background.ReminderWorker>();

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

// Keep the system administrator role in step with the permission catalogue after an upgrade. Data only (no schema change),
// and never fatal: an unreachable or not-yet-migrated database must not stop the API from starting.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<Erp.Modules.Platform.Application.IRolePermissionStore>()
            .SyncSystemAdministratorPermissionsAsync(CancellationToken.None);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(exception, "Could not sync system administrator permissions at startup (is the database migrated?).");
    }
}
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
app.MapPurchasingEndpoints();
app.MapNotificationEndpoints();
app.MapNotificationSettingsEndpoints();

if (args.Contains("--seed"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("--seed is only allowed in the Development environment.");
    await DevSeeder.RunAsync(app.Services);
    return;
}

app.Run();

public partial class Program;
