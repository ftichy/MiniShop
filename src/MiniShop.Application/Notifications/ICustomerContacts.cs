namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 10: e-mail a souhlasy zákazníka v okamžiku odeslání. Implementuje composition root (Web nad účty) –
/// Notifications o modulu Accounts neví.
/// </summary>
/// <remarks>
/// Proč dotaz a ne kopie ze zprávy (jako příjemce objednávky)? Souhlas musí platit, když e-mail odchází –
/// kdo ho mezitím odvolal nebo smazal účet, připomínku dostat nesmí.
/// </remarks>
public interface ICustomerContacts
{
    /// <summary>Kontakt zákazníka, nebo null (účet už neexistuje).</summary>
    Task<CustomerContact?> FindAsync(string customerId, CancellationToken cancellationToken = default);
}

/// <param name="Email">Jen potvrzený e-mail (nepotvrzenému nepíšeme marketingová upozornění).</param>
/// <param name="CartRemindersConsent">Zákazník chce připomínky opuštěného košíku (výchozí ne).</param>
public sealed record CustomerContact(string Email, string DisplayName, bool EmailConfirmed, bool CartRemindersConsent);
