// Krok 10B: popis celé aplikace jako kódu – jaké služby existují a kdo potřebuje koho.
// Aspire je spustí ve správném pořadí a každé předá adresy ostatních (connection stringy, service discovery).
var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ v Dockeru. Persistent = kontejner po zastavení AppHostu běží dál, fronty a zprávy přežijí restart.
// Heslo vygeneruje Aspire a uloží do user secrets AppHostu; služby ho dostanou v ConnectionStrings:messaging.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

// Škálování krok 1 a 3C: databáze PostgreSQL v Dockeru (persistentní kontejner + volume s daty, heslo v user secrets
// AppHostu) – jedna databáze "minishop", každý modul ve vlastním schématu. Služby dostanou ConnectionStrings:minishop.
var database = builder.AddPostgres("postgres")
    .WithDataVolume("minishop-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("minishop");

// Účty krok 7: servisní klíč skladu. Aspire ho vygeneruje (jako heslo RabbitMQ) a uloží do user secrets AppHostu;
// obě služby ho dostanou v InventoryApi__ApiKey – Web ho posílá, sklad ověřuje.
var inventoryApiKey = builder.AddParameter("inventory-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

// E-shop krok 8: sdílené tajemství s platební bránou (podpis webhooku HMAC). Simulace brány i ověření běží ve Webu.
var paymentsWebhookSecret = builder.AddParameter("payments-webhook-secret",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

// Služba skladu: vlastní proces, vlastní inventory.db, fronta minishop.inventory.
var inventoryApi = builder.AddProject<Projects.MiniShop_Inventory_Api>("inventory-api")
    .WithReference(messaging)
    .WithEnvironment("InventoryApi__ApiKey", inventoryApiKey)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    .WithModuleDatabase(database);

// Mikroservisy M1: přehled tržeb jako samostatná služba (fronta minishop.reporting, schéma reporting) s vlastním
// servisním klíčem – uniklý klíč skladu ji neotevře.
var reportingApiKey = builder.AddParameter("reporting-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var reportingApi = builder.AddProject<Projects.MiniShop_Reporting_Api>("reporting-api")
    .WithReference(messaging)
    .WithEnvironment("ReportingApi__ApiKey", reportingApiKey)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    .WithModuleDatabase(database);

// Mikroservisy M2A: klíč interního API Webu (/internal/…) – služby (od M2C Notifications) se jím ptají Webu.
var webInternalApiKey = builder.AddParameter("web-internal-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

// Účty krok 5: "cvičná pošta" Mailpit – zachytí každý e-mail z Webu, nikam ven nic neodejde.
// Pošta je vidět na webovém rozhraní (odkaz u prostředku "mailpit" v dashboardu).
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.31.4")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
    .WithEndpoint(targetPort: 1025, name: "smtp");
var mailpitSmtp = mailpit.GetEndpoint("smtp");

// Mikroservisy M2C: Notifications (e-maily, oznámení push) jako samostatná služba – e-maily do Mailpitu, data jiných modulů
// z interního API Webu (reference na Web níže – Web a Notifications se znají navzájem, proto bez WaitFor).
var notificationsApiKey = builder.AddParameter("notifications-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var notificationsApi = builder.AddProject<Projects.MiniShop_Notifications_Api>("notifications-api")
    .WithReference(messaging)
    .WithEnvironment("NotificationsApi__ApiKey", notificationsApiKey)
    .WithEnvironment("WebInternalApi__ApiKey", webInternalApiKey)
    .WithEnvironment("Email__Host", mailpitSmtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Port", mailpitSmtp.Property(EndpointProperty.Port))
    .WithEnvironment("Email__UseTls", "false")
    .WaitFor(messaging)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health")
    .WithModuleDatabase(database);

// Web = služba Ordering + UI. WithReference(inventoryApi) → Web najde sklad pod jménem "inventory-api".
var web = builder.AddProject<Projects.MiniShop_Web>("web")
    .WithReference(messaging)
    .WithReference(inventoryApi)
    .WithReference(reportingApi)
    .WithReference(notificationsApi)
    .WithEnvironment("InventoryApi__ApiKey", inventoryApiKey)
    .WithEnvironment("ReportingApi__ApiKey", reportingApiKey)
    .WithEnvironment("WebInternalApi__ApiKey", webInternalApiKey)
    .WithEnvironment("NotificationsApi__ApiKey", notificationsApiKey)
    .WithEnvironment("Payments__WebhookSecret", paymentsWebhookSecret)
    .WithEnvironment("Email__Host", mailpitSmtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Port", mailpitSmtp.Property(EndpointProperty.Port))
    .WithEnvironment("Email__UseTls", "false")
    .WaitFor(messaging)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithModuleDatabase(database);

notificationsApi.WithReference(web);   // interní API Webu (/internal/…)

// E-shop krok 26A: simulovaný dopravce („cizí firma“ – bez databáze MiniShopu a bez RabbitMQ). Klíč e-shopu u dopravce
// a tajemství podpisu webhooků generuje Aspire. Web ho volá od kroku 26B, webhooky přijímá od 26C.
var carrierApiKey = builder.AddParameter("carrier-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var carrierWebhookSecret = builder.AddParameter("carrier-webhook-secret",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var carrierSim = builder.AddProject<Projects.MiniShop_CarrierSimulator>("carrier-sim")
    .WithEnvironment("Carrier__ApiKey", carrierApiKey)
    .WithEnvironment("Carrier__WebhookSecret", carrierWebhookSecret)
    // E-shop krok 26C: kam posílat webhooky o stavu zásilky (HTTP adresa Webu, kterou Aspire přidělí).
    .WithEnvironment("Carrier__WebhookUrl",
        ReferenceExpression.Create($"{web.GetEndpoint("http")}/api/shipping/carrier-webhook"))
    .WithHttpHealthCheck("/health");

// E-shop krok 26B: Web zakládá zásilky u dopravce (bez WaitFor – Web běží i bez dopravce, předání se zopakuje);
// 26C: Web ověřuje podpis webhooků stejným tajemstvím.
web.WithReference(carrierSim)
    .WithEnvironment("Carrier__ApiKey", carrierApiKey)
    .WithEnvironment("Carrier__WebhookSecret", carrierWebhookSecret);

builder.Build().Run();

internal static class ModuleDatabaseResourceExtensions
{
    /// <summary>Škálování krok 1: služba dostane PostgreSQL (ConnectionStrings:minishop) a počká, až poběží.</summary>
    public static IResourceBuilder<ProjectResource> WithModuleDatabase(
        this IResourceBuilder<ProjectResource> service, IResourceBuilder<PostgresDatabaseResource> database) =>
        service.WithReference(database).WaitFor(database);
}
