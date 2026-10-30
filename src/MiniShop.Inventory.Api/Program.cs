using MiniShop.Application;
using MiniShop.Application.Inventory;
using MiniShop.Contracts.Inventory;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Inventory.Api;
using Microsoft.AspNetCore.Authorization;
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

    // Účty krok 7: zero trust – /api jen se servisním klíčem Webu (401 bez něj). Bez klíče v konfiguraci se sklad nespustí.
    var apiKey = ServiceApiKey.Read(key => builder.Configuration[key]);
    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.SchemeName, options => options.ApiKey = apiKey);
    builder.Services.AddAuthorizationBuilder()
        // Co není výslovně [AllowAnonymous] (health checky), to vyžaduje klíč – i čtení.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    await InventoryDatabaseInitializer.InitializeAsync(app.Services, app.Services.GetRequiredService<TimeProvider>());

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
