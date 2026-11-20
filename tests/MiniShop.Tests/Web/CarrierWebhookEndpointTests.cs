using System.Net;
using System.Text;
using MiniShop.CarrierSimulator;
using MiniShop.Infrastructure.Shipping;
using MiniShop.Tests.CarrierSimulator;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 26C: endpoint webhooku dopravce přes HTTP – bez přihlášení, ale jen s platným podpisem a čerstvým časem.
/// Logiku (duplicity, pořadí, doručení) testuje OrderServiceTests.Shipping.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class CarrierWebhookEndpointTests(MiniShopWebFactory web)
{
    private const string Url = "/api/shipping/carrier-webhook";

    private static readonly string Body =
        """{"id":"evt_1","type":"shipment.status_changed","created":"2026-11-20T10:00:30Z","data":{"trackingNumber":"CS0000000001","reference":"1","status":"accepted","sequence":1,"occurredAt":"2026-11-20T10:00:30Z"}}""";

    [Fact]
    public async Task BezPodpisu401_StaraZprava401_SPlatnymPodpisemNeznamaZasilka200()
    {
        var client = web.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, Body, signature: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, Body,
            WebhookDispatcher.Sign(CarrierSimulatorFactory.WebhookSecret, Body, DateTimeOffset.UtcNow.AddMinutes(-10)))).StatusCode);

        var fresh = WebhookDispatcher.Sign(CarrierSimulatorFactory.WebhookSecret, Body, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, Body, fresh)).StatusCode);   // cizí zásilka – převzato, nic se nezmění
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(CarrierWebhookProcessor.SignatureHeaderName, signature);
        }

        return client.SendAsync(request);
    }
}
