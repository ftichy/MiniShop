using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Reporting.Api;
using MiniShop.ServiceDefaults;
using Serilog;

// Mikroservisy M1: přehled tržeb jako samostatná služba. Do e-shop kroku 11 až M0 běžel modul Reporting ve Webu;
// teď má vlastní proces – nasazuje se, restartuje a padá sám. Web o něm ví jen adresu HTTP API (reporting-api)
// a servisní klíč; data dostává služba jen ze zpráv (fronta minishop.reporting).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Neprozrazovat technologii serveru (účty krok 9).
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // Logy píše Serilog a OpenTelemetry (EDA krok 11) – výchozí providery pryč, jinak by byly na konzoli dvakrát.
    builder.Logging.ClearProviders();

    builder.AddServiceDefaults();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(),
        writeToProviders: true);

    // Stejná PostgreSQL databáze (ConnectionStrings:minishop), schéma reporting – nic se nepřevádí.
    var database = ModuleDatabase.FromConfiguration(builder.Configuration);

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddReportingModule();
    builder.Services.AddInfrastructureCore();
    builder.Services.AddReportingInfrastructure(database);

    // Vedoucí kopie (outbox, úklid zpráv, měření stavu, přestavění po startu) – vlastní zámek, jiné workery než Web.
    builder.Services.AddLeaderElection(database, "minishop.reporting-api");
    builder.Services.AddLeaderOnlyHostedService<SalesReportOnStartupWorker>();

    // Outbox worker + RabbitMQ (fronta minishop.reporting).
    builder.Services.AddMessaging(builder.Configuration);
    // Proud B2: trvalý čtenář proudu minishop.events – přehled Rychlost vyřízení (záložka v DB, jen vedoucí kopie).
    builder.Services.AddLeaderOnlyHostedService<MiniShop.Infrastructure.Reporting.LeadTimeProjectionWorker>();

    // Zero trust: /api jen se servisním klíčem Webu (ReportingApi:ApiKey, secret REPORTING_API_KEY); bez klíče se služba nespustí.
    builder.Services.AddServiceApiKeyAuthentication(
        ServiceApiKey.Read(key => builder.Configuration[key], ServiceApiKey.ReportingConfigurationKey));

    builder.Services.AddControllers();
    builder.Services.AddProblemDetails();

    var app = builder.Build();

    await ReportingDatabaseInitializer.InitializeAsync(app.Services);

    // Request logging vně exception handleru – loguje výsledný status, ne výjimku.
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
    Log.Fatal(ex, "Službu přehledu tržeb se nepodařilo spustit");
}
finally
{
    await Log.CloseAndFlushAsync();
}
