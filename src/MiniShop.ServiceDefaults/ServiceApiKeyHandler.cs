namespace MiniShop.ServiceDefaults;

/// <summary>
/// Účty krok 7: ke každému požadavku Webu na službu (sklad, přehled tržeb) přidá servisní klíč (hlavička X-Api-Key).
/// Služba bez něj vrátí 401.
/// </summary>
/// <remarks>
/// Je to článek v řetězu HttpClientu (DelegatingHandler) – klient (InventoryApiClient, ReportingApiClient) o klíči nic neví.
/// </remarks>
public sealed class ServiceApiKeyHandler(string apiKey) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ServiceApiKey.HeaderName);
        request.Headers.Add(ServiceApiKey.HeaderName, apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
