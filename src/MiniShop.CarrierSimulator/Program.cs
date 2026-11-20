// E-shop krok 26A: simulovaný dopravce. Chová se jako API cizí přepravní firmy: přijme zásilku (POST /v1/shipments,
// Idempotency-Key), přidělí číslo a pak sám posílá webhooky o stavu – převzato, na cestě, doručuje se, doručeno.
// Volitelně „zlobí“ (Carrier:Chaos): 503, ztracená odpověď, webhook dvakrát, mimo pořadí, vůbec.
// S MiniShopem nesdílí kód ani databázi – jen HTTP. Telemetrii do Grafany neposílá (cizí firma by ji nedala).
using MiniShop.CarrierSimulator;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<CarrierOptions>()
    .Bind(builder.Configuration.GetSection(CarrierOptions.SectionName))
    .Validate(o => { o.Validate(); return true; })
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShipmentStore>();
builder.Services.AddSingleton<ChaosDice>();
builder.Services.AddSingleton<WebhookDispatcher>();
builder.Services.AddHostedService<CarrierWorker>();
builder.Services.AddHttpClient(WebhookDispatcher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

// Healthcheck Compose (bash /dev/tcp hledá „ 200 “) – bez klíče, nic neprozradí.
app.MapGet("/health", () => Results.Text("Healthy"));
app.MapShipmentsApi();

var settings = app.Services.GetRequiredService<IOptions<CarrierOptions>>().Value;
app.Logger.LogInformation(
    "Simulátor dopravce: krok {StepSeconds} s, webhooky {Webhooks}, zlobení 503 {Failure}, ztracená odpověď {Lost}, dvakrát {Duplicate}, mimo pořadí {Reorder}, vůbec {Drop}",
    settings.StepSeconds, string.IsNullOrWhiteSpace(settings.WebhookUrl) ? "vypnuté" : "zapnuté",
    settings.Chaos.FailureRate, settings.Chaos.LostResponseRate, settings.Chaos.DuplicateRate, settings.Chaos.ReorderRate, settings.Chaos.DropRate);

app.Run();
