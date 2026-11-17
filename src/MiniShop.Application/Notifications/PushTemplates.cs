using System.Globalization;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 23: texty oznámení push – krátké (displej je zkrátí) a bez osobních údajů: jen číslo objednávky, stav,
/// dopravce, částka. Jméno, adresa ani důvod zamítnutí od admina (volný text) se nepíšou – oznámení vidí i kdo drží telefon.
/// Klepnutí vede na detail objednávky (cesta, ne celá adresa – otevírá ji service worker na stejném webu).
/// </summary>
public static class PushTemplates
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public static PushMessage Confirmed(int orderId) =>
        Order(orderId, "confirmed", $"Objednávka č. {orderId} je potvrzená", "Zboží máme zarezervované a objednávku připravujeme.");

    public static PushMessage Rejected(int orderId) =>
        Order(orderId, "rejected", $"Objednávku č. {orderId} nelze vyřídit", "Podrobnosti najdete v e-mailu a u objednávky.");

    public static PushMessage Shipped(int orderId, bool pickup, string? carrier) =>
        pickup
            ? Order(orderId, "shipped", $"Objednávka č. {orderId} je připravená", "Můžete si ji vyzvednout na prodejně.")
            : Order(orderId, "shipped", $"Objednávka č. {orderId} je na cestě",
                carrier is null ? "Objednávka odešla." : $"Předali jsme ji dopravci {carrier}.");

    public static PushMessage Delivered(int orderId, bool pickup) =>
        Order(orderId, "delivered", pickup ? $"Objednávka č. {orderId} je vyzvednutá" : $"Objednávka č. {orderId} je doručená",
            "Děkujeme za nákup!");

    public static PushMessage Cancelled(int orderId) =>
        Order(orderId, "cancelled", $"Objednávka č. {orderId} je zrušená", "Podrobnosti najdete v e-mailu a u objednávky.");

    public static PushMessage ReturnCompleted(int orderId, int returnNumber, decimal amount, bool refundedByCard) =>
        Order(orderId, $"return-{returnNumber}-completed", $"Vratka k objednávce č. {orderId} je vyřízená",
            string.Format(Czech, refundedByCard ? "{0:N2} Kč vracíme na kartu." : "{0:N2} Kč pošleme převodem.", amount));

    public static PushMessage ReturnRejected(int orderId, int returnNumber) =>
        Order(orderId, $"return-{returnNumber}-rejected", $"Vratku k objednávce č. {orderId} nelze přijmout",
            "Podrobnosti najdete v e-mailu a u objednávky.");

    /// <summary>„Dejte mi vědět“ – produkt je zase skladem (e-shop krok 18).</summary>
    public static PushMessage BackInStock(int productId, string productName) =>
        new("Zboží je zase skladem", $"{productName} – objednejte, než se vyprodá.", $"/Shop/{productId}", $"back-in-stock-{productId}");

    private static PushMessage Order(int orderId, string state, string title, string body) =>
        new(title, body, $"/Orders/{orderId}", $"order-{orderId}-{state}");
}
