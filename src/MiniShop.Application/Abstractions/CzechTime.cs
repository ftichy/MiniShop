using System.Globalization;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Jediné místo, kde se UTC čas mění na český (Europe/Prague, včetně letního času). V databázi zůstává vždy UTC.
/// </summary>
/// <remarks>
/// Nikdy metoda „ToLocalTime“ na DateTime – ta bere zónu stroje, a kontejner v produkci běží v UTC.
/// Chybí-li v systému časová zóna (image bez tzdata), <see cref="EnsureAvailable"/> při startu službu zastaví;
/// žádný tichý přechod na UTC.
/// </remarks>
public static class CzechTime
{
    public const string ZoneId = "Europe/Prague";

    /// <summary>Výchozí formát data a času (13. 11. 2026 9:14) – výslovný, ne „g“, aby nezávisel na verzi ICU na stroji.</summary>
    public const string DateTimeFormat = "d. M. yyyy H:mm";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly Lazy<TimeZoneInfo> Zone = new(Find);

    /// <summary>Volat při startu: zóna musí existovat, jinak výjimka s jasnou zprávou.</summary>
    public static void EnsureAvailable() => _ = Zone.Value;

    /// <summary>UTC → český čas. <c>Unspecified</c> (z databáze) se bere jako UTC.</summary>
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone.Value);

    /// <summary>Český čas → UTC (např. půlnoc českého dne).</summary>
    public static DateTime ToUtc(DateTime czechLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(czechLocal, DateTimeKind.Unspecified), Zone.Value);

    /// <summary>Text pro člověka v českém čase a české kultuře; výchozí formát „g“ (13. 11. 2026 9:14).</summary>
    public static string Format(DateTime utc, string format = DateTimeFormat) => FromUtc(utc).ToString(format, Culture);

    /// <summary>Pro Razor: <c>@x.ToCzechString()</c>, <c>@x.ToCzechString("d. M. yyyy")</c>.</summary>
    public static string ToCzechString(this DateTime utc, string format = DateTimeFormat) => Format(utc, format);

    /// <summary>Totéž pro <see cref="DateTimeOffset"/> (např. konec zámku účtu).</summary>
    public static string ToCzechString(this DateTimeOffset moment, string format = DateTimeFormat) => Format(moment.UtcDateTime, format);

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Časová zóna {ZoneId} není v systému dostupná (v image chybí balíček tzdata?). " +
                "Bez ní by se časy zobrazovaly špatně, služba se proto nespustí.", ex);
        }
    }
}
