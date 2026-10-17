using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Contracts.Inventory;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Inventory.Api;
using MiniShop.ServiceDefaults;
using Serilog;

// Krok 10B: služba skladu – samostatný proces. Objednávky (MiniShop.Web) o ní ví jen dvojí:
// jméno fronty v RabbitMQ (příkazy ReserveStockV1/ReleaseStockV1) a adresu HTTP API (/api/products).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    var connectionString = builder.Configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException("Chybí connection string 'Inventory' v appsettings.json.");

    // Jen modul Inventory: aplikační služba, handlery příkazů ságy a infrastruktura (inventory.db, outbox, inbox).
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
    builder.Services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);
    builder.Services.AddIntegrationMessageHandler<SyncCatalogV1, SyncCatalogHandler>(IntegrationModules.Inventory);
    builder.Services.AddInfrastructureCore();
    builder.Services.AddInventoryInfrastructure(connectionString);

    // Outbox worker + RabbitMQ (fronta minishop.inventory).
    builder.Services.AddMessaging(builder.Configuration);

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    await InventoryDatabaseInitializer.InitializeAsync(app.Services, app.Services.GetRequiredService<TimeProvider>());

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI dokument: /openapi/v1.json
        app.MapOpenApi();
    }

    // Request logging vně exception handleru – loguje výsledný status (404/400), ne výjimku.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    app.MapControllers();
    app.MapDefaultEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Službu skladu se nepodařilo spustit");
}
finally
{
    await Log.CloseAndFlushAsync();
}
