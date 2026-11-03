using System.Globalization;
using System.Net;
using System.Text;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Carts;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 10: text připomínky opuštěného košíku – prostý text a HTML, s odkazem na košík a na vypnutí připomínek.
/// Texty o objednávkách jsou v <see cref="OrderEmailTemplates"/>.
/// </summary>
/// <remarks>Do HTML jde všechno přes <see cref="WebUtility.HtmlEncode"/> (jméno a názvy produktů pocházejí od lidí).</remarks>
public sealed class CartEmailTemplates(NotificationSettings settings)
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public EmailMessage Reminder(CustomerContact to, IReadOnlyList<CartItemV1> items)
    {
        var greeting = to.DisplayName.Length > 0 ? $"Dobrý den, {to.DisplayName}," : "Dobrý den,";
        const string message = "v košíku na vás pořád čeká zboží. Rádi vám ho pošleme – stačí dokončit objednávku.";
        var lines = items
            .Select(i => string.Format(Czech, "{0} × {1} … {2:N2} Kč", i.Quantity, i.ProductName, i.Quantity * i.UnitPrice))
            .ToList();
        const string priceNote = "Ceny jsou aktuální k dnešku; zboží v košíku není rezervované.";
        var cartLink = settings.CartLink();
        var preferencesLink = settings.EmailPreferencesLink();

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine(message).AppendLine();
        lines.ForEach(line => text.AppendLine(line));
        text.AppendLine().AppendLine(priceNote);
        if (cartLink is not null)
        {
            text.AppendLine().AppendLine($"Košík: {cartLink}");
        }

        if (preferencesLink is not null)
        {
            text.AppendLine().AppendLine($"Připomínky nechcete? Vypněte je tady: {preferencesLink}");
        }

        text.AppendLine().Append("MiniShop");

        var html = new StringBuilder()
            .Append("<p>").Append(Encode(greeting)).Append("</p>")
            .Append("<p>").Append(Encode(message)).Append("</p><ul>");
        lines.ForEach(line => html.Append("<li>").Append(Encode(line)).Append("</li>"));
        html.Append("</ul><p>").Append(Encode(priceNote)).Append("</p>");
        if (cartLink is not null)
        {
            html.Append("<p><a href=\"").Append(Encode(cartLink)).Append("\">Přejít do košíku</a></p>");
        }

        if (preferencesLink is not null)
        {
            html.Append("<p><small>Připomínky nechcete? <a href=\"").Append(Encode(preferencesLink))
                .Append("\">Vypněte je</a>.</small></p>");
        }

        html.Append("<p>MiniShop</p>");

        return new EmailMessage(to.Email, "MiniShop: Ve vašem košíku zůstalo zboží", text.ToString(), html.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
