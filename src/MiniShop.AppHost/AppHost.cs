// Krok 10B: popis celé aplikace jako kódu – jaké služby existují a kdo potřebuje koho.
// Aspire je spustí ve správném pořadí a každé předá adresy ostatních (connection stringy, service discovery).
var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ v Dockeru. Persistent = kontejner po zastavení AppHostu běží dál, fronty a zprávy přežijí restart.
// Heslo vygeneruje Aspire a uloží do user secrets AppHostu; služby ho dostanou v ConnectionStrings:messaging.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

// Účty krok 7: servisní klíč skladu. Aspire ho vygeneruje (jako heslo RabbitMQ) a uloží do user secrets AppHostu;
// obě služby ho dostanou v InventoryApi__ApiKey – Web ho posílá, sklad ověřuje.
var inventoryApiKey = builder.AddParameter("inventory-api-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

// Služba skladu: vlastní proces, vlastní inventory.db, fronta minishop.inventory.
var inventoryApi = builder.AddProject<Projects.MiniShop_Inventory_Api>("inventory-api")
    .WithReference(messaging)
    .WithEnvironment("InventoryApi__ApiKey", inventoryApiKey)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health");

// Účty krok 5: "cvičná pošta" Mailpit – zachytí každý e-mail z Webu, nikam ven nic neodejde.
// Pošta je vidět na webovém rozhraní (odkaz u prostředku "mailpit" v dashboardu).
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.31.4")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
    .WithEndpoint(targetPort: 1025, name: "smtp");
var mailpitSmtp = mailpit.GetEndpoint("smtp");

// Web = služba Ordering + UI. WithReference(inventoryApi) → Web najde sklad pod jménem "inventory-api".
builder.AddProject<Projects.MiniShop_Web>("web")
    .WithReference(messaging)
    .WithReference(inventoryApi)
    .WithEnvironment("InventoryApi__ApiKey", inventoryApiKey)
    .WithEnvironment("Email__Host", mailpitSmtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Port", mailpitSmtp.Property(EndpointProperty.Port))
    .WithEnvironment("Email__UseTls", "false")
    .WaitFor(messaging)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
