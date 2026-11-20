using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.CarrierSimulator;

namespace MiniShop.Tests.CarrierSimulator;

/// <summary>
/// E-shop krok 26A: simulovaný dopravce se chová jako cizí API – klíč, idempotence, trvalé a dočasné chyby, časová osa
/// zásilky a podepsané webhooky včetně „zlobení“ (dvakrát, mimo pořadí, vůbec, nedostupný e-shop).
/// </summary>
public sealed class CarrierSimulatorTests
{
    private static readonly object ValidShipment = new
    {
        reference = "42",
        recipient = new { name = "Jan Novák", street = "Testovací 1", city = "Praha", postalCode = "110 00" },
        weightGrams = 1200
    };

    [Fact]
    public async Task Api_BezKliceNeboSeSpatnymKlicem401_HealthBezKlice()
    {
        using var carrier = new CarrierSimulatorFactory();
        var anonymous = carrier.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/shipments/CS1")).StatusCode);

        anonymous.DefaultRequestHeaders.Add(ShipmentsApi.ApiKeyHeader, new string('x', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/shipments/CS1")).StatusCode);
    }

    [Fact]
    public async Task Vytvoreni_VraticisloAZasilkaJeCreated()
    {
        using var carrier = new CarrierSimulatorFactory();
        var client = carrier.CreateAuthorizedClient();

        var response = await PostAsync(client, "klic-1", ValidShipment);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shipment = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Matches(@"^CS\d{10}$", shipment!.TrackingNumber);
        Assert.Equal("42", shipment.Reference);
        Assert.Equal(CarrierStatuses.Created, shipment.Status);

        var fetched = await client.GetFromJsonAsync<ShipmentResponse>($"/v1/shipments/{shipment.TrackingNumber}");
        Assert.Equal(CarrierStatuses.Created, fetched!.Status);
        Assert.Empty(fetched.Events);
    }

    [Fact]
    public async Task Idempotence_StejnyKlicVratiStejnouZasilku_JinyObsahJeKonflikt_BezKliceChyba()
    {
        using var carrier = new CarrierSimulatorFactory();
        var client = carrier.CreateAuthorizedClient();

        var first = await (await PostAsync(client, "klic-1", ValidShipment)).Content.ReadFromJsonAsync<ShipmentResponse>();
        var again = await PostAsync(client, "klic-1", ValidShipment);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues(ShipmentsApi.ReplayedHeader).Single());
        Assert.Equal(first!.TrackingNumber, (await again.Content.ReadFromJsonAsync<ShipmentResponse>())!.TrackingNumber);

        var other = new { reference = "43", recipient = new { name = "Jan Novák", street = "Jiná 2", city = "Brno", postalCode = "60200" } };
        var conflict = await PostAsync(client, "klic-1", other);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_key_reused", (await ErrorAsync(conflict)).Code);

        var withoutKey = await client.PostAsJsonAsync("/v1/shipments", ValidShipment);
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal("idempotency_key_required", (await ErrorAsync(withoutKey)).Code);
    }

    [Theory]
    [InlineData("1100", 1200, "invalid_postal_code")]
    [InlineData("11000", 40_000, "parcel_too_heavy")]
    public async Task TrvalaChyba_422SKodem(string postalCode, int weightGrams, string code)
    {
        using var carrier = new CarrierSimulatorFactory();
        var client = carrier.CreateAuthorizedClient();

        var response = await PostAsync(client, "klic-1",
            new { reference = "42", recipient = new { name = "Jan", street = "Ulice 1", city = "Praha", postalCode }, weightGrams });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(code, (await ErrorAsync(response)).Code);
    }

    [Fact]
    public async Task Zlobeni503_NicNevznikne()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:Chaos:FailureRate"] = "1" });
        var client = carrier.CreateAuthorizedClient();

        var response = await PostAsync(client, "klic-1", ValidShipment);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, carrier.Services.GetRequiredService<ShipmentStore>().Read(s => s.Shipments.Count));
    }

    [Fact]
    public async Task ZtracenaOdpoved_ZasilkaVznikla_OpakovaniJiNajde()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:Chaos:LostResponseRate"] = "1" });
        var client = carrier.CreateAuthorizedClient();

        var lost = await PostAsync(client, "klic-1", ValidShipment);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);

        var retry = await PostAsync(client, "klic-1", ValidShipment);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);   // opakování = stejná zásilka, žádná druhá
        Assert.Equal(1, carrier.Services.GetRequiredService<ShipmentStore>().Read(s => s.Shipments.Count));
    }

    [Fact]
    public async Task CasovaOsa_StavyPoKrocich_WebhookyVPoradiSPlatnymPodpisem()
    {
        using var carrier = new CarrierSimulatorFactory();
        var client = carrier.CreateAuthorizedClient();
        var tracking = await CreateAsync(client);

        await carrier.ProcessAsync();
        Assert.Empty(carrier.Webhooks.Received);   // ještě nic nenastalo

        for (var step = 1; step <= CarrierStatuses.Timeline.Length; step++)
        {
            await carrier.AdvanceStepsAsync();
        }

        var webhooks = carrier.Webhooks.Received;
        Assert.Equal(CarrierStatuses.Timeline, webhooks.Select(w => Data(w).GetProperty("status").GetString()));
        Assert.Equal([1, 2, 3, 4], webhooks.Select(w => Data(w).GetProperty("sequence").GetInt32()));
        Assert.All(webhooks, w =>
        {
            Assert.Equal(CarrierSimulatorFactory.WebhookUrl, w.Url);
            Assert.Equal(tracking, Data(w).GetProperty("trackingNumber").GetString());
            Assert.Equal("42", Data(w).GetProperty("reference").GetString());
            Assert.True(SignatureIsValid(w), "Podpis webhooku nesedí");
        });
        Assert.DoesNotContain(webhooks, w => w.Body.Contains("Novák"));   // adresa příjemce do webhooku nepatří

        var shipment = await client.GetFromJsonAsync<ShipmentResponse>($"/v1/shipments/{tracking}");
        Assert.Equal(CarrierStatuses.Delivered, shipment!.Status);
        Assert.Equal(4, shipment.Events.Count);
    }

    [Fact]
    public async Task VypadekSimulatoru_DozeneStavySPlanovanymiCasy()
    {
        using var carrier = new CarrierSimulatorFactory();
        var client = carrier.CreateAuthorizedClient();
        var tracking = await CreateAsync(client);

        await carrier.AdvanceStepsAsync(10);   // plánovač „neběžel“ – všechny stavy naráz

        var shipment = await client.GetFromJsonAsync<ShipmentResponse>($"/v1/shipments/{tracking}");
        Assert.Equal(CarrierStatuses.Timeline, shipment!.Events.Select(e => e.Status));
        Assert.Equal(shipment.CreatedAt.AddSeconds(CarrierSimulatorFactory.StepSeconds), shipment.Events[0].OccurredAt);
        Assert.Equal(CarrierStatuses.Timeline, carrier.Webhooks.Received.Select(w => Data(w).GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ZlobeniDvakrat_StejneIdUdalostiPrijdeDvakrat()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:Chaos:DuplicateRate"] = "1" });
        var client = carrier.CreateAuthorizedClient();
        await CreateAsync(client);

        await carrier.AdvanceStepsAsync(1);
        await carrier.AdvanceStepsAsync(1);

        var accepted = carrier.Webhooks.Received.Where(w => Data(w).GetProperty("status").GetString() == CarrierStatuses.Accepted).ToList();
        Assert.Equal(2, accepted.Count);
        Assert.Equal(Id(accepted[0]), Id(accepted[1]));
        Assert.Equal(accepted[0].Body, accepted[1].Body);
    }

    [Fact]
    public async Task ZlobeniMimoPoradi_DorucenoPrijdePredDorucujeSe()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:Chaos:ReorderRate"] = "1" });
        var client = carrier.CreateAuthorizedClient();
        await CreateAsync(client);

        for (var step = 0; step < 6; step++)
        {
            await carrier.AdvanceStepsAsync();
        }

        // Každý stav (kromě posledního) čeká za následujícím → poslední „doručeno“ předběhne „doručuje se“.
        var sequences = carrier.Webhooks.Received.Select(w => Data(w).GetProperty("sequence").GetInt32()).ToList();
        Assert.Equal([1, 2, 4, 3], sequences);
    }

    [Fact]
    public async Task ZlobeniVubec_ZadnyWebhook_StavJenPresGet()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:Chaos:DropRate"] = "1" });
        var client = carrier.CreateAuthorizedClient();
        var tracking = await CreateAsync(client);

        await carrier.AdvanceStepsAsync(5);

        Assert.Empty(carrier.Webhooks.Received);
        Assert.Equal(CarrierStatuses.Delivered, (await client.GetFromJsonAsync<ShipmentResponse>($"/v1/shipments/{tracking}"))!.Status);
    }

    [Fact]
    public async Task EshopNedostupny_WebhookSeOpakujeSOdstupem_PakVzda()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:MaxWebhookAttempts"] = "3" });
        var client = carrier.CreateAuthorizedClient();
        await CreateAsync(client);
        carrier.Webhooks.NextStatus = HttpStatusCode.InternalServerError;

        await carrier.AdvanceStepsAsync();                      // 1. pokus (accepted) selže
        Assert.Single(carrier.Webhooks.Received);

        carrier.Time.Advance(WebhookDispatcher.RetryDelay(1) - TimeSpan.FromSeconds(1));
        await carrier.ProcessAsync();
        Assert.Single(carrier.Webhooks.Received);               // před odstupem nic

        carrier.Webhooks.NextStatus = HttpStatusCode.OK;
        carrier.Time.Advance(TimeSpan.FromSeconds(1));
        await carrier.ProcessAsync();                           // 2. pokus projde
        Assert.Equal(2, carrier.Webhooks.Received.Count);
        Assert.Equal(Id(carrier.Webhooks.Received[0]), Id(carrier.Webhooks.Received[1]));

        // Po MaxWebhookAttempts neúspěších dopravce webhook vzdá.
        carrier.Webhooks.NextStatus = HttpStatusCode.ServiceUnavailable;
        for (var i = 0; i < 20; i++)
        {
            carrier.Time.Advance(TimeSpan.FromMinutes(10));
            await carrier.ProcessAsync();
        }

        Assert.Equal(0, carrier.Services.GetRequiredService<ShipmentStore>().Read(s => s.Deliveries.Count));
    }

    [Fact]
    public async Task BezAdresyWebhooku_NicNeposila_StavyPokracuji()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:WebhookUrl"] = "" });
        var client = carrier.CreateAuthorizedClient();
        var tracking = await CreateAsync(client);

        await carrier.AdvanceStepsAsync(4);

        Assert.Empty(carrier.Webhooks.Received);
        Assert.Equal(CarrierStatuses.Delivered, (await client.GetFromJsonAsync<ShipmentResponse>($"/v1/shipments/{tracking}"))!.Status);
    }

    [Fact]
    public async Task Uklid_DorucenaZasilkaPoRetenciZmizi()
    {
        using var carrier = new CarrierSimulatorFactory(new Dictionary<string, string?> { ["Carrier:RetentionDays"] = "30" });
        var client = carrier.CreateAuthorizedClient();
        var tracking = await CreateAsync(client);
        await carrier.AdvanceStepsAsync(4);

        carrier.Time.Advance(TimeSpan.FromDays(31));
        await carrier.ProcessAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/shipments/{tracking}")).StatusCode);
    }

    [Fact]
    public async Task Restart_ZasilkyACekajiciWebhookyPrezijiVSouboru()
    {
        using var carrier = new CarrierSimulatorFactory();
        var tracking = await CreateAsync(carrier.CreateAuthorizedClient());

        var file = Path.Combine(carrier.DataPath, "carrier-state.json");
        var state = JsonSerializer.Deserialize<CarrierState>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(tracking, Assert.Single(state!.Shipments).TrackingNumber);
    }

    [Fact]
    public void Simulator_NereferencujeProjektyMiniShopu()
    {
        // „Cizí firma“: s e-shopem jen přes HTTP – žádná sdílená knihovna (kontrakty, doména, infrastruktura).
        var references = typeof(ShipmentStore).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(references, name => name.StartsWith("MiniShop.", StringComparison.Ordinal));
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(body) };
        request.Headers.Add(ShipmentsApi.IdempotencyKeyHeader, idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<string> CreateAsync(HttpClient client)
    {
        var response = await PostAsync(client, Guid.NewGuid().ToString(), ValidShipment);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShipmentResponse>())!.TrackingNumber;
    }

    private static async Task<ErrorResponse> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!;

    private static JsonElement Data(ReceivedWebhook webhook) => JsonDocument.Parse(webhook.Body).RootElement.GetProperty("data");

    private static string Id(ReceivedWebhook webhook) => JsonDocument.Parse(webhook.Body).RootElement.GetProperty("id").GetString()!;

    /// <summary>Ověření podpisu tak, jak ho bude dělat e-shop (krok 26C): HMAC-SHA256 tajemství nad „t.tělo“.</summary>
    private static bool SignatureIsValid(ReceivedWebhook webhook)
    {
        var parts = webhook.Signature.Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(CarrierSimulatorFactory.WebhookSecret),
            Encoding.UTF8.GetBytes($"{parts["t"]}.{webhook.Body}"));
        return long.TryParse(parts["t"], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts["v1"]));
    }
}
