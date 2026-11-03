using System.Globalization;
using System.Text;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Účty krok 5: oznámení zákazníkovi e-mailem (dříve LoggingOrderNotifier, který jen psal do logu).
/// Volají ho handlery doménových událostí za outboxem – když SMTP chvíli nejde, výjimka způsobí opakování
/// (max. 5×), aplikační vrstva se kvůli e-mailu nezměnila.
/// </summary>
/// <remarks>
/// Odesílá se na e-mail uložený v objednávce (kopie z účtu v době objednání). Známé omezení: při pádu mezi
/// odesláním a zápisem do inboxu odejde e-mail znovu (at-least-once).
/// </remarks>
public sealed class EmailOrderNotifier(IEmailSender emailSender) : IOrderNotifier
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken = default) =>
        SendAsync(order, $"Objednávka č. {order.Id} je potvrzená",
            $"zboží máme zarezervované a objednávku připravujeme.\n\n{Summary(order)}", cancellationToken);

    public Task OrderRejectedAsync(Order order, CancellationToken cancellationToken = default) =>
        SendAsync(order, $"Objednávku č. {order.Id} nelze vyřídit",
            $"omlouváme se, objednávku nemůžeme vyřídit: {order.RejectionReason}\n\n{Summary(order)}", cancellationToken);

    /// <summary>E-shop krok 6: kurýr – dopravce a číslo zásilky; osobní odběr – můžete si ji vyzvednout.</summary>
    public Task OrderShippedAsync(Order order, CancellationToken cancellationToken = default) =>
        order.ShippingMethod == ShippingMethod.Pickup
            ? SendAsync(order, $"Objednávka č. {order.Id} je připravená k vyzvednutí",
                "objednávka na vás čeká na prodejně, můžete si ji vyzvednout.", cancellationToken)
            : SendAsync(order, $"Objednávka č. {order.Id} je na cestě",
                order.Carrier is null
                    ? "objednávka odešla."
                    : $"objednávku jsme předali dopravci {order.Carrier}" +
                      (order.TrackingNumber is null ? "." : $", číslo zásilky {order.TrackingNumber}."),
                cancellationToken);

    /// <summary>E-shop krok 6: doručeno (osobní odběr: vyzvednuto) – dřív „vyřízeno“.</summary>
    public Task OrderCompletedAsync(Order order, CancellationToken cancellationToken = default) =>
        order.ShippingMethod == ShippingMethod.Pickup
            ? SendAsync(order, $"Objednávka č. {order.Id} je vyzvednutá", "děkujeme za nákup!", cancellationToken)
            : SendAsync(order, $"Objednávka č. {order.Id} je doručená", "objednávka je doručená. Děkujeme za nákup!",
                cancellationToken);

    public Task OrderCancelledAsync(Order order, CancellationToken cancellationToken = default) =>
        SendAsync(order, $"Objednávka č. {order.Id} je zrušená",
            "objednávka byla zrušena, rezervované zboží se vrací na sklad.", cancellationToken);

    private Task SendAsync(Order order, string subject, string text, CancellationToken cancellationToken)
    {
        // Účty krok 9: zákazník mezitím smazal účet (objednávka je anonymizovaná) – není komu psát.
        // Bez téhle kontroly by handler za outboxem padal na prázdné adrese a zbytečně se opakoval.
        if (string.IsNullOrEmpty(order.CustomerEmail))
        {
            return Task.CompletedTask;
        }

        return emailSender.SendAsync(new EmailMessage(
            order.CustomerEmail,
            $"MiniShop: {subject}",
            $"Dobrý den, {order.CustomerName},\n\n{text}\n\nMiniShop"), cancellationToken);
    }

    private static string Summary(Order order)
    {
        var text = new StringBuilder();
        foreach (var item in order.Items)
        {
            text.AppendLine(string.Format(Czech, "{0} × {1} … {2:N2} Kč", item.Quantity, item.ProductName, item.LineTotal));
        }

        // E-shop krok 4: doprava z pokladny (starší objednávky ji nemají).
        if (order.ShippingMethod != ShippingMethod.None)
        {
            var name = order.ShippingMethod == ShippingMethod.Courier ? "Kurýr" : "Osobní odběr";
            text.AppendLine(string.Format(Czech, "{0} … {1:N2} Kč", name, order.ShippingPrice));
            if (order.DeliveryAddress is not null)
            {
                text.AppendLine($"Doručovací adresa: {order.DeliveryAddress}");
            }
        }

        text.Append(string.Format(Czech, "Celkem: {0:N2} Kč", order.TotalPrice));
        return text.ToString();
    }
}
