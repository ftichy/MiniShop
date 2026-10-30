using MiniShop.ServiceDefaults;

namespace MiniShop.Web.Inventory;

/// <summary>
/// Účty krok 7: ke každému požadavku na sklad přidá servisní klíč (hlavička X-Api-Key). Sklad bez něj vrátí 401.
/// </summary>
/// <remarks>
/// Je to článek v řetězu HttpClientu (DelegatingHandler) – InventoryApiClient o klíči nic neví.
/// </remarks>
public sealed class InventoryApiKeyHandler(string apiKey) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ServiceApiKey.HeaderName);
        request.Headers.Add(ServiceApiKey.HeaderName, apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
