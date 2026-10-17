using System.Globalization;
using Microsoft.AspNetCore.Localization;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Persistence;
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

    // Serilog místo výchozího logování: konzole + denní soubor logs/minishop-RRRRMMDD.log.
    // Úrovně a cíle se nastavují v sekci "Serilog" v appsettings.json.
    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    var connectionString = builder.Configuration.GetConnectionString("MiniShop")
        ?? throw new InvalidOperationException("Chybí connection string 'MiniShop' v appsettings.json.");

    // Registrace vrstev (composition root).
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(connectionString);

    // Krok 5: doručování událostí z outboxu na pozadí.
    builder.Services.AddHostedService<OutboxWorker>();

    // Krok 8: integrační události do RabbitMQ (nebo jen do logu), viz sekce "Messaging".
    builder.Services.AddMessaging(builder.Configuration);

    builder.Services.AddControllers();
    builder.Services.AddRazorPages();
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    await DbInitializer.InitializeAsync(app.Services);

    app.UseExceptionHandler();

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
    app.UseSerilogRequestLogging();

    app.UseRouting();

    app.MapControllers();
    app.MapRazorPages();

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
