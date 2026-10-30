namespace MiniShop.ServiceDefaults;

/// <summary>
/// Účty krok 7: servisní klíč mezi Webem a skladem (zero trust – sklad nevěří síti, ověří si, že volá Web).
/// Obě služby ho čtou ze stejného klíče konfigurace; hodnotu dodá Aspire, Compose (secret INVENTORY_API_KEY)
/// nebo ve vývoji appsettings.Development.json.
/// </summary>
public static class ServiceApiKey
{
    /// <summary>HTTP hlavička, ve které Web klíč posílá.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Klíč konfigurace (proměnná prostředí <c>InventoryApi__ApiKey</c>).</summary>
    public const string ConfigurationKey = "InventoryApi:ApiKey";

    /// <summary>Kratší klíč by šel uhodnout; produkce má 64 hex znaků (256 bitů).</summary>
    public const int MinLength = 32;

    /// <summary>
    /// Klíč z konfigurace. Chybí-li nebo je krátký, služba se nespustí – chybějící klíč je chyba nasazení,
    /// ne důvod běžet bez zámku (fail fast).
    /// </summary>
    public static string Read(Func<string, string?> configuration)
    {
        var key = configuration(ConfigurationKey);
        if (string.IsNullOrWhiteSpace(key) || key.Length < MinLength)
        {
            throw new InvalidOperationException(
                $"Chybí servisní klíč skladu '{ConfigurationKey}' (aspoň {MinLength} znaků). " +
                "V produkci secret INVENTORY_API_KEY, lokálně ho dodá Aspire nebo appsettings.Development.json.");
        }

        return key;
    }
}
