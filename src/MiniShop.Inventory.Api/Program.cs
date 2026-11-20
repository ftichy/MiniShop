using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Persistence;
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

    // Účty krok 9: neprozrazovat technologii serveru (hlavička "Server: Kestrel") – útočníkovi by ušetřila práci.
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // Krok 11: výchozí providery pryč – logy píše Serilog a OpenTelemetry (jinak by byly na konzoli dvakrát).
    builder.Logging.ClearProviders();

    builder.AddServiceDefaults();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(),
        // Krok 11: logy i do OpenTelemetry (Aspire dashboard → Structured logs, propojené s trasou).
        writeToProviders: true);

    // Škálování krok 1 a 3C: PostgreSQL (ConnectionStrings:minishop), schéma inventory.
    var database = ModuleDatabase.FromConfiguration(builder.Configuration);

    // Jen modul Inventory: aplikační služba, handlery příkazů ságy a infrastruktura (inventory.db, outbox, inbox).
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddScoped<ICategoryService, CategoryService>();
    builder.Services.AddScoped<ProductImportService>();   // příkaz import-products (issue #115)
    builder.Services.AddIntegrationMessageHandler<ReserveStockV1, ReserveStockHandler>(IntegrationModules.Inventory);
    builder.Services.AddIntegrationMessageHandler<ReleaseStockV1, ReleaseStockHandler>(IntegrationModules.Inventory);
    builder.Services.AddIntegrationMessageHandler<RestockReturnedGoodsV1, RestockReturnedGoodsHandler>(IntegrationModules.Inventory);   // e-shop krok 12: vratky
    builder.Services.AddIntegrationMessageHandler<RestockReturnedGoodsV2, RestockReturnedGoodsHandler>(IntegrationModules.Inventory);   // e-shop krok 22: vratka po částech
    // E-shop krok 6: událost (ne příkaz) – objednávka odešla, rezervace se mění na výdej.
    builder.Services.AddIntegrationMessageHandler<OrderShippedV1, FulfilReservationHandler>(IntegrationModules.Inventory);
    // E-shop krok 26D: událost – dopravce vrátil zásilku nedoručenou, vydané zboží zpět na sklad.
    builder.Services.AddIntegrationMessageHandler<OrderReturnedToSenderV1, RestockReturnedGoodsHandler>(IntegrationModules.Inventory);
    builder.Services.AddIntegrationMessageHandler<SyncCatalogV1, SyncCatalogHandler>(IntegrationModules.Inventory);
    builder.Services.AddInfrastructureCore();
    builder.Services.AddInventoryInfrastructure(database);

    // E-shop krok 2B: obrázky produktů – v kontejneru /data/images (ENV v Dockerfile), lokálně složka images vedle projektu.
    builder.Services.AddFileSystemProductImages(
        Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["ProductImages:Path"] ?? "images"));

    // Škálování krok 5: vedoucí kopie skladu (outbox a úklid zpráv jen na ní) – vlastní zámek, jiné workery než Web.
    builder.Services.AddLeaderElection(database, "minishop.inventory-api");

    // Outbox worker + RabbitMQ (fronta minishop.inventory).
    builder.Services.AddMessaging(builder.Configuration);

    // Účty krok 7: zero trust – /api jen se servisním klíčem Webu (401 bez něj). Bez klíče v konfiguraci se sklad nespustí.
    // Co není výslovně [AllowAnonymous] (health checky), to vyžaduje klíč – i čtení (ServiceDefaults, mikroservisy M1).
    builder.Services.AddServiceApiKeyAuthentication(ServiceApiKey.Read(key => builder.Configuration[key]));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    await InventoryDatabaseInitializer.InitializeAsync(app.Services, app.Services.GetRequiredService<TimeProvider>());

    // Issue #115: "dotnet MiniShop.Inventory.Api.dll import-products" naplní sklad produkty a skončí (službu nespustí).
    if (ImportProductsCommand.IsRequested(args))
    {
        Environment.ExitCode = await ImportProductsCommand.RunAsync(app.Services, args);
        return;
    }

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI dokument: /openapi/v1.json
        app.MapOpenApi().AllowAnonymous();
    }

    // Request logging vně exception handleru – loguje výsledný status (404/400), ne výjimku.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    app.UseAuthentication();
    app.UseAuthorization();

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
