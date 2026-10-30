using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Email;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Ordering;
using MiniShop.ServiceDefaults;
using MiniShop.Web.Accounts;
using MiniShop.Web.BackgroundServices;
using MiniShop.Web.Documentation;
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

    // Krok 11: výchozí providery (Console, Debug…) pryč – logy píše Serilog a OpenTelemetry (ServiceDefaults).
    // Jinak by je Serilog s writeToProviders přeposlal i výchozí konzoli a každý řádek by byl vidět dvakrát.
    builder.Logging.ClearProviders();

    // Krok 10B: health checky, service discovery (najde Inventory.Api) a odolnost HttpClientů.
    builder.AddServiceDefaults();

    // Serilog místo výchozího logování: konzole + denní soubor logs/minishop-RRRRMMDD.log.
    // Úrovně a cíle se nastavují v sekci "Serilog" v appsettings.json.
    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(),
        // Krok 11: logy i do OpenTelemetry (Aspire dashboard → Structured logs, propojené s trasou).
        writeToProviders: true);

    // Krok 10B: Web je služba Ordering – jen databáze objednávek. Sklad má vlastní službu i databázi.
    var orderingConnectionString = builder.Configuration.GetConnectionString("Ordering")
        ?? throw new InvalidOperationException("Chybí connection string 'Ordering' v appsettings.json.");

    // Krok 12: průběh zakládání objednávek (modul Journeys) má vlastní databázi.
    var journeysConnectionString = builder.Configuration.GetConnectionString("Journeys")
        ?? throw new InvalidOperationException("Chybí connection string 'Journeys' v appsettings.json.");

    // Účty krok 1: uživatelské účty (modul Accounts) mají vlastní databázi.
    var accountsConnectionString = builder.Configuration.GetConnectionString("Accounts")
        ?? throw new InvalidOperationException("Chybí connection string 'Accounts' v appsettings.json.");

    // Registrace vrstev (composition root).
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructureCore();
    builder.Services.AddOrderingInfrastructure(orderingConnectionString);
    builder.Services.AddJourneysInfrastructure(journeysConnectionString);
    builder.Services.AddAccounts(accountsConnectionString);

    // Účty krok 5: e-maily přes SMTP (sekce "Email"). Bez Email:Host jen do logu.
    // Lokálně server dodá Aspire (Mailpit), v produkci Gmail (production.env + secret SMTP_PASSWORD).
    builder.Services.AddEmail(builder.Configuration);

    // Krok 9: hlídání termínů ságy (sklad neodpověděl včas → zamítnout).
    builder.Services.AddHostedService<OrderSagaTimeoutWorker>();

    // Prázdná kopie katalogu po startu (nová ordering.db) → požádat sklad o celý katalog.
    builder.Services.AddHostedService<CatalogSyncOnStartupWorker>();

    // Kroky 5 a 8: outbox worker + RabbitMQ (fronty minishop.ordering a minishop.audit), viz sekce "Messaging".
    builder.Services.AddMessaging(builder.Configuration);

    // Cloud krok 1: klíče, kterými ASP.NET podepisuje antiforgery token formulářů. V kontejneru se jinak
    // drží jen v paměti → po restartu (každém nasazení) by otevřený formulář objednávky skončil chybou 400.
    // Cestu nastavuje image (/data/keys na volume); lokálně prázdná = výchozí chování.
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        builder.Services.AddDataProtection()
            .SetApplicationName("MiniShop.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
    }

    // Příručka v aplikaci (/Docs): Markdown z docs/prirucka → HTML, diagramy Mermaid.
    builder.Services.AddDocumentation(builder.Configuration, builder.Environment);

    builder.Services.AddControllers();
    // Účty krok 6: celá složka /Admin jen pro admina – pojistka navíc k [Authorize] na stránkách.
    builder.Services.AddRazorPages(options =>
        options.Conventions.AuthorizeFolder("/Admin", AuthorizationPolicies.RequireAdmin));
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    // Účty krok 2: "dotnet MiniShop.Web.dll create-admin --email …" jen založí prvního admina a skončí (web nespustí).
    if (CreateAdminCommand.IsRequested(args))
    {
        Environment.ExitCode = await CreateAdminCommand.RunAsync(app.Services, args);
        return;
    }

    await OrderingDatabaseInitializer.InitializeAsync(app.Services);
    await JourneysDatabaseInitializer.InitializeAsync(app.Services);

    // Účty krok 2: role + první admin. AdminBootstrap je nastavený jen ve vývoji (appsettings.Development.json),
    // v produkci admina založí příkaz create-admin.
    await AccountsDatabaseInitializer.InitializeAsync(app.Services,
        builder.Configuration.GetSection("AdminBootstrap").Get<AdminBootstrapSettings>());

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

    // Účty krok 1: z cookie přečte přihlášeného uživatele (User), autorizace pak rozhodne, kam smí.
    app.UseAuthentication();
    app.UseMustChangePassword();   // krok 2: první admin se známým heslem si ho musí nejdřív změnit
    app.UseAuthorization();

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
