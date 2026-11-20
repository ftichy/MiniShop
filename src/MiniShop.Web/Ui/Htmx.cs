namespace MiniShop.Web.Ui;

/// <summary>
/// Vzhled krok F1: požadavky z htmx (<c>wwwroot/lib/htmx</c>) – formulář poslaný na pozadí, odpověď vymění jen kousek
/// stránky (<c>hx-swap-oob</c>). Bez JavaScriptu přijde tentýž formulář jako obyčejný POST a stránka se chová jako dřív.
/// </summary>
public static class Htmx
{
    /// <summary>Hlavička, kterou htmx přidá ke každému svému požadavku.</summary>
    public const string RequestHeader = "HX-Request";

    /// <summary>Odpověď „přejdi celou stránkou jinam“ (např. na přihlášení) – přesměrování 302 by htmx provedl potichu na pozadí.</summary>
    public const string RedirectHeader = "HX-Redirect";

    public static bool IsHtmxRequest(this HttpRequest request) =>
        string.Equals(request.Headers[RequestHeader], "true", StringComparison.OrdinalIgnoreCase);
}
