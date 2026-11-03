using System.Globalization;
using System.Net;
using System.Text;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 7: komu psát o jedné objednávce. <paramref name="Order"/> = uložená kopie z OrderPlacedV2 (jméno, souhrn);
/// null u objednávek starších než Notifications – pak jde kratší e-mail jen na adresu z události.
/// </summary>
public sealed record OrderMailRecipient(int OrderId, string Email, OrderRecipient? Order)
{
    public static OrderMailRecipient From(OrderRecipient order) => new(order.OrderId, order.CustomerEmail, order);
}

/// <summary>
/// E-shop krok 7: texty e-mailů o objednávce – prostý text a jednoduché HTML, s odkazem na detail objednávky.
/// Jediné místo, kde se rozhoduje, jak oznámení vypadá; handler jen vybere šablonu podle události.
/// </summary>
/// <remarks>
/// Do HTML jde všechno přes <see cref="WebUtility.HtmlEncode"/> – jméno, názvy produktů i adresa pocházejí od lidí.
/// Dřív EmailOrderNotifier v Orderingu (účty krok 5).
/// </remarks>
public sealed class OrderEmailTemplates(NotificationSettings settings)
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public EmailMessage Placed(OrderMailRecipient to) =>
        Build(to, $"Objednávku č. {to.OrderId} jsme přijali",
            "děkujeme, objednávku jsme přijali. Teď ověřujeme, že máme všechno skladem – potvrzení přijde během chvilky.",
            withSummary: true);

    public EmailMessage Confirmed(OrderMailRecipient to) =>
        Build(to, $"Objednávka č. {to.OrderId} je potvrzená",
            "zboží máme zarezervované a objednávku připravujeme.", withSummary: true);

    public EmailMessage Rejected(OrderMailRecipient to, string reason) =>
        Build(to, $"Objednávku č. {to.OrderId} nelze vyřídit",
            $"omlouváme se, objednávku nemůžeme vyřídit: {reason}", withSummary: true);

    /// <summary>Kurýr: dopravce a číslo zásilky; osobní odběr: můžete si ji vyzvednout.</summary>
    public EmailMessage Shipped(OrderMailRecipient to, string? carrier, string? trackingNumber) =>
        to.Order?.IsPickup == true
            ? Build(to, $"Objednávka č. {to.OrderId} je připravená k vyzvednutí",
                "objednávka na vás čeká na prodejně, můžete si ji vyzvednout.")
            : Build(to, $"Objednávka č. {to.OrderId} je na cestě",
                carrier is null
                    ? "objednávka odešla."
                    : $"objednávku jsme předali dopravci {carrier}" + (trackingNumber is null ? "." : $", číslo zásilky {trackingNumber}."));

    /// <summary>Doručeno (osobní odběr: vyzvednuto) – OrderCompletedV1.</summary>
    public EmailMessage Delivered(OrderMailRecipient to) =>
        to.Order?.IsPickup == true
            ? Build(to, $"Objednávka č. {to.OrderId} je vyzvednutá", "děkujeme za nákup!")
            : Build(to, $"Objednávka č. {to.OrderId} je doručená", "objednávka je doručená. Děkujeme za nákup!");

    /// <summary>E-shop krok 8: platba přišla po termínu (objednávka už byla zamítnutá) – peníze se vracejí.</summary>
    public EmailMessage Refunded(OrderMailRecipient to, decimal amount) =>
        Build(to, $"Platbu za objednávku č. {to.OrderId} vracíme",
            string.Format(Czech, "platba {0:N2} Kč přišla až po termínu – objednávka už byla zrušená. Peníze vám vracíme.", amount));

    public EmailMessage Cancelled(OrderMailRecipient to) =>
        Build(to, $"Objednávka č. {to.OrderId} je zrušená", "objednávka byla zrušena, rezervované zboží se vrací na sklad.");

    private EmailMessage Build(OrderMailRecipient to, string subject, string message, bool withSummary = false)
    {
        var greeting = to.Order is { CustomerName.Length: > 0 } order ? $"Dobrý den, {order.CustomerName}," : "Dobrý den,";
        var summary = withSummary && to.Order is not null ? SummaryLines(to.Order) : [];
        var link = settings.OrderLink(to.OrderId);

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine(message);
        if (summary.Count > 0)
        {
            text.AppendLine();
            summary.ForEach(line => text.AppendLine(line));
        }

        if (link is not null)
        {
            text.AppendLine().AppendLine($"Detail objednávky: {link}");
        }

        text.AppendLine().Append("MiniShop");

        var html = new StringBuilder()
            .Append("<p>").Append(Encode(greeting)).Append("</p>")
            .Append("<p>").Append(Encode(message)).Append("</p>");
        if (summary.Count > 0)
        {
            html.Append("<ul>");
            summary.ForEach(line => html.Append("<li>").Append(Encode(line)).Append("</li>"));
            html.Append("</ul>");
        }

        if (link is not null)
        {
            html.Append("<p><a href=\"").Append(Encode(link)).Append("\">Detail objednávky č. ")
                .Append(to.OrderId).Append("</a></p>");
        }

        html.Append("<p>MiniShop</p>");

        return new EmailMessage(to.Email, $"MiniShop: {subject}", text.ToString(), html.ToString());
    }

    private static List<string> SummaryLines(OrderRecipient order)
    {
        var lines = order.Items
            .Select(i => string.Format(Czech, "{0} × {1} … {2:N2} Kč", i.Quantity, i.ProductName, i.LineTotal))
            .ToList();

        if (order.ShippingMethod != "None")
        {
            var name = order.ShippingMethod == "Courier" ? "Kurýr" : "Osobní odběr";
            lines.Add(string.Format(Czech, "{0} … {1:N2} Kč", name, order.ShippingPrice));
            if (order.DeliveryAddress is not null)
            {
                lines.Add($"Doručovací adresa: {order.DeliveryAddress}");
            }
        }

        lines.Add(string.Format(Czech, "Celkem: {0:N2} Kč", order.TotalPrice));
        return lines;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
