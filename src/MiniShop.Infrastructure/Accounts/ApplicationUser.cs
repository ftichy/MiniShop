using Microsoft.AspNetCore.Identity;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Uživatelský účet (účty krok 1). Hash hesla, e-mail, zámek účtu, security stamp… dědí z Identity.
/// Přihlašovací jméno (UserName) je e-mail.
/// </summary>
/// <remarks>
/// Účet nemá business pravidla, proto nežije v doméně – je to „technická“ věc přihlašování.
/// Ostatní moduly znají jen jeho ID (Ordering od účty kroku 3 jako CustomerId).
/// </remarks>
public sealed class ApplicationUser : IdentityUser
{
    public const int DisplayNameMaxLength = 100;

    /// <summary>Jméno, které se ukazuje v hlavičce a předvyplní se do objednávky.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// E-shop krok 10: souhlas s připomínkou opuštěného košíku (e-mail). Výchozí ne – zapíná si ho zákazník sám
    /// na stránce Nastavení e-mailů; Notifications ho čte až při odeslání.
    /// </summary>
    public bool CartRemindersConsent { get; set; }
}
