namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 7: veřejná adresa webu pro odkazy v e-mailech (konfigurace <c>Notifications:PublicBaseUrl</c>,
/// v produkci https://minishop-tichy.cz). Handler běží za frontou zpráv, ne v HTTP požadavku – adresu webu nezná.
/// </summary>
/// <param name="PublicBaseUrl">Bez nastavení = e-mail bez odkazu (raději žádný než špatný).</param>
public sealed record NotificationSettings(string? PublicBaseUrl)
{
    /// <summary>Odkaz na detail objednávky, nebo null.</summary>
    public string? OrderLink(int orderId) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl) ? null : $"{PublicBaseUrl.TrimEnd('/')}/Orders/{orderId}";
}
