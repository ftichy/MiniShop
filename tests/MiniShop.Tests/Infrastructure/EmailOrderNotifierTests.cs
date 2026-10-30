using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>Účty krok 5: oznámení o objednávce odchází e-mailem na adresu uloženou v objednávce.</summary>
public sealed class EmailOrderNotifierTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Potvrzeni_JdeZakaznikoviSPolozkamiACenou()
    {
        var emails = new RecordingEmailSender();
        var order = new Order("zakaznik-1", "Jan Novák", "jan@example.com", Now);
        order.AddItem(new CatalogProduct(7, "Klávesnice", 1_000m, Now), quantity: 2, discount: 0m);

        await new EmailOrderNotifier(emails).OrderConfirmedAsync(order);

        var email = Assert.Single(emails.Sent);
        Assert.Equal("jan@example.com", email.To);
        Assert.Contains("potvrzená", email.Subject);
        Assert.Contains("Jan Novák", email.TextBody);
        Assert.Contains("2 × Klávesnice", email.TextBody);
        Assert.Contains("Celkem: 2 000,00 Kč", email.TextBody.Replace(' ', ' '));
    }

    [Fact]
    public async Task Zamitnuti_ObsahujeDuvod()
    {
        var emails = new RecordingEmailSender();
        var order = new Order("zakaznik-1", "Jan Novák", "jan@example.com", Now);
        order.Reject("Nedostatek zboží.", Now);

        await new EmailOrderNotifier(emails).OrderRejectedAsync(order);

        Assert.Contains("Nedostatek zboží.", Assert.Single(emails.Sent).TextBody);
    }
}
