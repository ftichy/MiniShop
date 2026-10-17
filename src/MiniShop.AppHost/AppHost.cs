// Krok 10B: popis celé aplikace jako kódu – jaké služby existují a kdo potřebuje koho.
// Aspire je spustí ve správném pořadí a každé předá adresy ostatních (connection stringy, service discovery).
var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ v Dockeru. Persistent = kontejner po zastavení AppHostu běží dál, fronty a zprávy přežijí restart.
// Heslo vygeneruje Aspire a uloží do user secrets AppHostu; služby ho dostanou v ConnectionStrings:messaging.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

// Služba skladu: vlastní proces, vlastní inventory.db, fronta minishop.inventory.
var inventoryApi = builder.AddProject<Projects.MiniShop_Inventory_Api>("inventory-api")
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health");

// Web = služba Ordering + UI. WithReference(inventoryApi) → Web najde sklad pod jménem "inventory-api".
builder.AddProject<Projects.MiniShop_Web>("web")
    .WithReference(messaging)
    .WithReference(inventoryApi)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
