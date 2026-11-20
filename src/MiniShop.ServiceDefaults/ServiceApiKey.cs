namespace MiniShop.ServiceDefaults;

/// <summary>
/// Účty krok 7: servisní klíč mezi Webem a službou (zero trust – služba nevěří síti, ověří si, že volá Web).
/// Web i volaná služba ho čtou ze stejného klíče konfigurace; hodnotu dodá Aspire, Compose (secret) nebo ve vývoji
/// appsettings.Development.json. Mikroservisy M1: klíč pro každou volanou službu – uniklý klíč otevře jen jednu.
/// </summary>
public static class ServiceApiKey
{
    /// <summary>HTTP hlavička, ve které Web klíč posílá.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Klíč konfigurace skladu (proměnná prostředí <c>InventoryApi__ApiKey</c>, secret <c>INVENTORY_API_KEY</c>).</summary>
    public const string ConfigurationKey = "InventoryApi:ApiKey";

    /// <summary>Mikroservisy M1: klíč konfigurace přehledu tržeb (<c>ReportingApi__ApiKey</c>, secret <c>REPORTING_API_KEY</c>).</summary>
    public const string ReportingConfigurationKey = "ReportingApi:ApiKey";

    /// <summary>
    /// Mikroservisy M2A: klíč interního API Webu (<c>/internal/…</c>, <c>WebInternalApi__ApiKey</c>, secret
    /// <c>WEB_INTERNAL_API_KEY</c>) – služby (od M2C Notifications) se jím ptají Webu na data jiných modulů.
    /// </summary>
    public const string WebInternalConfigurationKey = "WebInternalApi:ApiKey";

    /// <summary>Mikroservisy M2C: klíč služby Notifications (<c>NotificationsApi__ApiKey</c>, secret <c>NOTIFICATIONS_API_KEY</c>).</summary>
    public const string NotificationsConfigurationKey = "NotificationsApi:ApiKey";

    /// <summary>E-shop krok 26B: klíč e-shopu u dopravce (<c>Carrier__ApiKey</c>, secret <c>CARRIER_API_KEY</c>) – Web jím volá <c>carrier-sim</c>.</summary>
    public const string CarrierConfigurationKey = "Carrier:ApiKey";

    private static readonly Dictionary<string, string> Secrets = new()
    {
        [ConfigurationKey] = "INVENTORY_API_KEY",
        [ReportingConfigurationKey] = "REPORTING_API_KEY",
        [WebInternalConfigurationKey] = "WEB_INTERNAL_API_KEY",
        [NotificationsConfigurationKey] = "NOTIFICATIONS_API_KEY",
        [CarrierConfigurationKey] = "CARRIER_API_KEY"
    };

    /// <summary>Kratší klíč by šel uhodnout; produkce má 64 hex znaků (256 bitů).</summary>
    public const int MinLength = 32;

    /// <summary>
    /// Klíč z konfigurace. Chybí-li nebo je krátký, služba se nespustí – chybějící klíč je chyba nasazení,
    /// ne důvod běžet bez zámku (fail fast).
    /// </summary>
    public static string Read(Func<string, string?> configuration, string configurationKey = ConfigurationKey)
    {
        var key = configuration(configurationKey);
        if (string.IsNullOrWhiteSpace(key) || key.Length < MinLength)
        {
            throw new InvalidOperationException(
                $"Chybí servisní klíč '{configurationKey}' (aspoň {MinLength} znaků). " +
                $"V produkci secret {Secrets.GetValueOrDefault(configurationKey, "?")}, lokálně ho dodá Aspire nebo appsettings.Development.json.");
        }

        return key;
    }
}
