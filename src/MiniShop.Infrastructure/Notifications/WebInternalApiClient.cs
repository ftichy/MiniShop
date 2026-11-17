using System.Net;
using System.Net.Http.Json;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// Mikroservisy M2A: Notifications se ptá Webu na data jiných modulů přes jeho interní API (<c>/internal/…</c>, servisní
/// klíč přidá <c>ServiceApiKeyHandler</c>). Stejná rozhraní jako implementace v procesu – handlery Notifications nepoznají,
/// jestli Web běží vedle nich, nebo ve vedlejším kontejneru. Používá ho až samostatná služba (M2C); Web sám sebe nevolá.
/// </summary>
/// <remarks>
/// Neexistuje (404) = <c>null</c>, jako v procesu. Jiná chyba (Web neběží, 5xx, 401) = výjimka → consumer zprávu zopakuje
/// a nakonec ji odloží do dead-letter fronty (vrátit jde na /Admin/Messages) – e-mail se neztratí ani neodejde bez údajů.
/// </remarks>
public sealed class WebInternalApiClient(HttpClient http) : ICustomerContacts, IStaffDirectory, IProductNames, IInvoiceDocuments
{
    public async Task<CustomerContact?> FindAsync(string customerId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"internal/customers/{Uri.EscapeDataString(customerId)}/contact", cancellationToken);
        return await ReadOrNullAsync<CustomerContact>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> FilterAllowedAsync(StaffAlertKind kind, IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("internal/staff/allowed", new StaffAllowed(kind, userIds), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken) ?? [];
    }

    async Task<string?> IProductNames.FindAsync(int productId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"internal/products/{productId}/name", cancellationToken);
        return (await ReadOrNullAsync<ProductName>(response, cancellationToken))?.Name;
    }

    public Task<EmailAttachment?> GetInvoiceAsync(int orderId, CancellationToken cancellationToken = default) =>
        GetPdfAsync($"internal/orders/{orderId}/invoice", cancellationToken);

    public Task<EmailAttachment?> GetCreditNoteAsync(int orderId, string creditNoteNumber, CancellationToken cancellationToken = default) =>
        GetPdfAsync($"internal/orders/{orderId}/credit-notes/{Uri.EscapeDataString(creditNoteNumber)}", cancellationToken);

    private async Task<EmailAttachment?> GetPdfAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "doklad.pdf";
        return new EmailAttachment(fileName, response.Content.Headers.ContentType?.MediaType ?? "application/pdf",
            await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static async Task<T?> ReadOrNullAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    /// <summary>Tvar těla <c>POST /internal/staff/allowed</c> (Web: <c>StaffAllowedRequest</c>) – hlídá test kontraktu.</summary>
    private sealed record StaffAllowed(StaffAlertKind Kind, IReadOnlyList<string> UserIds);

    /// <summary>Tvar odpovědi <c>GET /internal/products/{id}/name</c> (Web: <c>ProductNameResponse</c>).</summary>
    private sealed record ProductName(string Name);
}
