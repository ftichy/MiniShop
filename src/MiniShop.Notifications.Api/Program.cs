using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Email;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Notifications.Api;
using MiniShop.ServiceDefaults;
using Serilog;

// Mikroservisy M2C: Notifications (e-maily a oznámení push) jako samostatná služba. Do M2B běžel modul ve Webu;
// teď má vlastní proces. Data dostává ze zpráv (fronta minishop.notifications); kontakt zákazníka, role zaměstnanců,
// název produktu a PDF faktur bere z interního API Webu (M2A). Web se jí ptá kvůli stránkám (zařízení push, oznámení
// k práci, „Dejte mi vědět“) přes /api se servisním klíčem.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
    builder.Logging.ClearProviders();
    builder.AddServiceDefaults();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(),
        writeToProviders: true);

    // Stejná PostgreSQL databáze (ConnectionStrings:minishop), schéma notifications – nic se nepřevádí.
    var database = ModuleDatabase.FromConfiguration(builder.Configuration);

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddNotificationsModule(builder.Configuration);
    builder.Services.AddWebInternalApiClient(builder.Configuration);
    builder.Services.AddInfrastructureCore();
    builder.Services.AddNotificationsInfrastructure(database);

    // E-maily přes SMTP (sekce "Email"; bez Email:Host jen do logu) – produkce Gmail (production.env + secret SMTP_PASSWORD).
    builder.Services.AddEmail(builder.Configuration);

    // Vedoucí kopie (úklid zpráv, měření stavu) – vlastní zámek.
    builder.Services.AddLeaderElection(database, "minishop.notifications-api");

    // RabbitMQ (fronta minishop.notifications).
    builder.Services.AddMessaging(builder.Configuration);

    // Zero trust: /api jen se servisním klíčem Webu (NotificationsApi:ApiKey, secret NOTIFICATIONS_API_KEY).
    builder.Services.AddServiceApiKeyAuthentication(
        ServiceApiKey.Read(key => builder.Configuration[key], ServiceApiKey.NotificationsConfigurationKey));

    builder.Services.AddControllers();
    builder.Services.AddProblemDetails();

    var app = builder.Build();

    await NotificationsDatabaseInitializer.InitializeAsync(app.Services);

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
    Log.Fatal(ex, "Službu Notifications se nepodařilo spustit");
}
finally
{
    await Log.CloseAndFlushAsync();
}
