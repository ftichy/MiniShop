using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 7: texty e-mailů modulu Notifications (dřív EmailOrderNotifier v Orderingu, účty krok 5).
/// Prostý text i HTML, odkaz na detail objednávky, kódování HTML, kratší e-mail u objednávky bez uloženého příjemce.
/// </summary>
public sealed class OrderEmailTemplatesTests
{
    private readonly OrderEmailTemplates _templates = new(new NotificationSettings("https://minishop.test/"));

    private static OrderRecipient Recipient(string shipping = "Courier", string name = "Jan Novák") =>
        new(7, "zakaznik-1", name, "jan@example.com",
            [new OrderRecipientItem("Klávesnice", 2, 2_000m)],
            shipping, shipping == "Courier" ? 99m : 0m, shipping == "Courier" ? "Dlouhá 5, 110 00 Praha" : null, 2_099m);

    [Fact]
    public void Potvrzeni_SouhrnPolozekDopravaAOdkaz()
    {
        var email = _templates.Confirmed(OrderMailRecipient.From(Recipient()));

        Assert.Equal("jan@example.com", email.To);
        Assert.Equal("MiniShop: Objednávka č. 7 je potvrzená", email.Subject);
        var text = email.TextBody.Replace(' ', ' ').Replace(' ', ' ');   // česká čísla mají nezlomitelnou mezeru
        Assert.Contains("Dobrý den, Jan Novák,", text);
        Assert.Contains("2 × Klávesnice … 2 000,00 Kč", text);
        Assert.Contains("Kurýr … 99,00 Kč", text);
        Assert.Contains("Doručovací adresa: Dlouhá 5, 110 00 Praha", text);
        Assert.Contains("Celkem: 2 099,00 Kč", text);
        Assert.Contains("Detail objednávky: https://minishop.test/Orders/7", text);   // lomítko navíc v konfiguraci nevadí
        Assert.Contains("<a href=\"https://minishop.test/Orders/7\">", email.HtmlBody);
    }

    [Fact]
    public void Html_JmenoSeKoduje()
    {
        var email = _templates.Placed(OrderMailRecipient.From(Recipient(name: "<script>Jan</script>")));

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;Jan&lt;/script&gt;", email.HtmlBody);
    }

    [Theory]
    [InlineData("Courier", "je na cestě", "předali dopravci PPL, číslo zásilky ZAS-42.")]
    [InlineData("Pickup", "je připravená k vyzvednutí", "můžete si ji vyzvednout")]
    public void Odeslani_ZasilkaNeboVyzvednuti(string shipping, string subject, string text)
    {
        var email = _templates.Shipped(OrderMailRecipient.From(Recipient(shipping)), "PPL", "ZAS-42");

        Assert.Contains(subject, email.Subject);
        Assert.Contains(text, email.TextBody);
    }

    [Fact]
    public void StaraObjednavkaBezPrijemce_KratsiEmailBezJmenaASouhrnu()
    {
        var email = _templates.Delivered(new OrderMailRecipient(3, "eva@example.com", Order: null));

        Assert.Equal("eva@example.com", email.To);
        Assert.StartsWith("Dobrý den,\r\n", email.TextBody.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.DoesNotContain("Celkem", email.TextBody);
    }

    [Fact]
    public void BezVerejneAdresy_EmailBezOdkazu()
    {
        var email = new OrderEmailTemplates(new NotificationSettings(null)).Cancelled(OrderMailRecipient.From(Recipient()));

        Assert.DoesNotContain("Detail objednávky", email.TextBody);
        Assert.DoesNotContain("<a ", email.HtmlBody);
    }
}
