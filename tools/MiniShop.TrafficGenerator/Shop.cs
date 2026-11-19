using System.Net;
using System.Text.RegularExpressions;

namespace MiniShop.TrafficGenerator;

/// <summary>Kroky nákupní cesty přes HTTP – stejné stránky a formuláře jako v prohlížeči.</summary>
public static class Shop
{
    public const string EmailDomain = "loadtest.minishop.invalid";

    public static string CustomerEmail(int number) => $"loadtest-{number}@{EmailDomain}";

    public static string StaffEmail => $"loadtest-sklad@{EmailDomain}";

    public static async Task LoginAsync(Session session, string email, string password)
    {
        var page = await session.PostAsync("/Account/Login", "/Account/Login", ("Input.Email", email), ("Input.Password", password));
        if (page.Url.StartsWith("/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase))
        {
            return;   // pokračuje volající s kódem
        }

        if (page.Url.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Přihlášení {email} selhalo – existuje účet (create-loadtest-users) a sedí heslo?");
        }
    }

    public static async Task LoginStaffAsync(Session session, string password, string totpKey)
    {
        var page = await session.PostAsync("/Account/Login", "/Account/Login",
            ("Input.Email", StaffEmail), ("Input.Password", password));
        if (!page.Url.StartsWith("/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Skladník nemá zapnuté 2FA nebo přihlášení selhalo (create-loadtest-users).");
        }

        var done = await session.PostAsync(page, "/Account/LoginWith2fa", [("Input.Code", Totp.Compute(totpKey))]);
        if (done.Url.StartsWith("/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Kód 2FA skladníka nesedí – klíč z create-loadtest-users, hodiny PC?");
        }
    }

    /// <summary>Vybere náhodné 1–2 produkty z výlohy (jen ty, co mají „Do košíku“) a přidá je do košíku.</summary>
    public static async Task AddRandomProductsAsync(Session session, Random random)
    {
        var shop = await session.GetAsync("/Shop");
        var ids = HtmlForms.ProductIds(shop.Html);
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Výloha nemá žádný produkt k nákupu (prázdný katalog, vyprodáno?).");
        }

        foreach (var id in ids.OrderBy(_ => random.Next()).Take(random.Next(1, 3)))
        {
            await session.PostAsync(shop, $"/Cart?handler=Add", [("productId", id.ToString()), ("returnUrl", "/Shop"), ("quantity", "1")]);
        }
    }

    /// <summary>Pokladna: kurýr + dobírka. Vrací číslo objednávky.</summary>
    public static async Task<int> CheckoutCashOnDeliveryAsync(Session session)
    {
        var checkout = await session.GetAsync("/Checkout");
        if (checkout.Url.StartsWith("/Cart", StringComparison.OrdinalIgnoreCase) || !HtmlForms.HiddenInputs(checkout.Html).Any())
        {
            throw new InvalidOperationException("Pokladna není dostupná (prázdný košík?).");
        }

        var fields = HtmlForms.HiddenInputs(checkout.Html).Concat(
        [
            ("Input.ShippingMethod", "Courier"),
            ("Input.PaymentMethod", "CashOnDelivery"),
            ("Input.Street", "Testovací 1"),
            ("Input.City", "Praha"),
            ("Input.PostalCode", "11000")
        ]);
        var result = await session.PostAsync(checkout, "/Checkout", fields);
        return result.OrderId ?? throw new InvalidOperationException(
            "Objednávka nevznikla: " + Regex.Replace(WebUtility.HtmlDecode(Regex.Match(result.Html, "validation-summary[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value), "<[^>]+>", " ").Trim());
    }

    /// <summary>Čeká (obnovováním detailu), až bude stav <paramref name="statusText"/>. Vrací, jak dlouho to trvalo.</summary>
    public static async Task<TimeSpan> WaitForStatusAsync(Session session, int orderId, string statusText, TimeSpan patience)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < patience)
        {
            var page = await session.GetAsync($"/Orders/{orderId}");
            if (Regex.IsMatch(page.Html, "data-track=\"order-detail-status\"[^>]*>\\s*" + Regex.Escape(statusText)))
            {
                return DateTime.UtcNow - started;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Objednávka {orderId} se do {patience.TotalSeconds:0} s nedostala do stavu „{statusText}“.");
    }

    public static async Task CancelAsync(Session session, int orderId) =>
        await session.PostAsync("/Orders", $"/Orders?handler=Cancel&id={orderId}");

    public static async Task RequestReturnAsync(Session session, int orderId) =>
        await session.PostAsync($"/Orders/{orderId}", $"/Orders/{orderId}?handler=Return", ("reason", "Generátor provozu: zboží nevyhovuje"));
}
