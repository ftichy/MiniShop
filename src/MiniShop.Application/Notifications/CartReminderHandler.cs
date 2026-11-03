using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Carts;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 10: připomínka opuštěného košíku. Ordering jen oznámí fakt (<see cref="CartAbandonedV1"/>), tenhle
/// odběratel rozhodne, jestli smí psát: jen potvrzenému e-mailu a jen se souhlasem, který platí <b>teď</b>.
/// </summary>
/// <remarks>
/// Bez souhlasu (nebo smazaný účet) handler jen skončí – inbox modulu zprávu odškrtne a nic se neopakuje.
/// Opakované doručení stejné zprávy připomínku znovu nepošle (inbox v notifications.db).
/// </remarks>
public sealed class CartReminderHandler(
    ICustomerContacts contacts,
    IEmailSender emailSender,
    CartEmailTemplates templates) : IIntegrationMessageHandler<CartAbandonedV1>
{
    public async Task HandleAsync(CartAbandonedV1 message, CancellationToken cancellationToken)
    {
        var contact = await contacts.FindAsync(message.CustomerId, cancellationToken);
        if (contact is not { CartRemindersConsent: true, EmailConfirmed: true, Email.Length: > 0 } || message.Items.Count == 0)
        {
            return;
        }

        await emailSender.SendAsync(templates.Reminder(contact, message.Items), cancellationToken);
    }
}
