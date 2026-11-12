using System.Net;
using System.Text;
using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Notifications;

/// <summary>E-mail „zboží je zase skladem“ – prostý text a HTML, odkaz na detail produktu.</summary>
public sealed class BackInStockEmailTemplates(NotificationSettings settings)
{
    public EmailMessage BackInStock(CustomerContact to, int productId, string productName)
    {
        var greeting = to.DisplayName.Length > 0 ? $"Dobrý den, {to.DisplayName}," : "Dobrý den,";
        var message = $"produkt „{productName}“ je zase skladem. Žádali jste o upozornění, tohle je jediný e-mail, který kvůli němu pošleme.";
        var link = settings.ProductLink(productId);

        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(message);
        if (link is not null)
        {
            text.AppendLine().AppendLine($"Detail produktu: {link}");
        }

        text.AppendLine().Append("MiniShop");

        var html = new StringBuilder()
            .Append("<p>").Append(WebUtility.HtmlEncode(greeting)).Append("</p>")
            .Append("<p>").Append(WebUtility.HtmlEncode(message)).Append("</p>");
        if (link is not null)
        {
            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\">Zobrazit produkt</a></p>");
        }

        html.Append("<p>MiniShop</p>");

        return new EmailMessage(to.Email, "MiniShop: Produkt je zase skladem", text.ToString(), html.ToString());
    }
}
