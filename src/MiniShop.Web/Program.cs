using System.Globalization;
using Microsoft.AspNetCore.Localization;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Ordering;
using MiniShop.ServiceDefaults;
using MiniShop.Web.BackgroundServices;
using MiniShop.Web.ErrorHandling;
using MiniShop.Web.Extensions;
using Serilog;

// Dočasný logger pro start aplikace: zachytí i chyby dřív, než se načte konfigurace.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Krok 10B: health checky, service discovery (najde Inventory.Api) a odolnost HttpClientů.
    builder.AddServiceDefaults();

    // Serilog místo výchozího logování: konzole + denní soubor logs/minishop-RRRRMMDD.log.
    // Úrovně a cíle se nastavují v sekci "Serilog" v appsettings.json.
    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Krok 10B: Web je služba Ordering – jen databáze objednávek. Sklad má vlastní službu i databázi.
    var orderingConnectionString = builder.Configuration.GetConnectionString("Ordering")
        ?? throw new InvalidOperationException("Chybí connection string 'Ordering' v appsettings.json.");

    // Registrace vrstev (composition root).
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructureCore();
    builder.Services.AddOrderingInfrastructure(orderingConnectionString);

    // Krok 9: hlídání termínů ságy (sklad neodpověděl včas → zamítnout).
    builder.Services.AddHostedService<OrderSagaTimeoutWorker>();

    // Prázdná kopie katalogu po startu (nová ordering.db) → požádat sklad o celý katalog.
    builder.Services.AddHostedService<CatalogSyncOnStartupWorker>();

    // Kroky 5 a 8: outbox worker + RabbitMQ (fronty minishop.ordering a minishop.audit), viz sekce "Messaging".
    builder.Services.AddMessaging(builder.Configuration);

    builder.Services.AddControllers();
    builder.Services.AddRazorPages();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    await OrderingDatabaseInitializer.InitializeAsync(app.Services);

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI dokument: /openapi/v1.json
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    // České formátování čísel a dat v UI (desetinná čárka, "Kč").
    var czech = new CultureInfo("cs-CZ");
    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture(czech),
        SupportedCultures = [czech],
        SupportedUICultures = [czech]
    });

    app.UseStaticFiles();

    // Jeden řádek na HTTP požadavek (metoda, cesta, status, doba trvání). Statické soubory se nelogují.
    // Pořadí je důležité: request logging musí být VNĚ exception handleru. Jinak vidí výjimku dřív,
    // než ji GlobalExceptionHandler převede na 404/400, a zaloguje ji jako 500 se stack tracem.
    app.UseSerilogRequestLogging();

    app.UseExceptionHandler();

    app.UseRouting();

    app.MapControllers();
    app.MapRazorPages();
    app.MapDefaultEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)   // HostAbortedException = běžné ukončení nástroji EF
{
    Log.Fatal(ex, "Aplikaci se nepodařilo spustit");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Umožní později psát integrační testy přes WebApplicationFactory<Program>.
public partial class Program { }
