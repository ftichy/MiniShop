namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Role uživatelů (účty krok 2). Ukládají se do users.db (AspNetRoles) a při přihlášení do cookie jako claim.
/// </summary>
public static class AccountRoles
{
    /// <summary>Zákazník: zakládá objednávky. Dostane ji každý při registraci.</summary>
    public const string User = "User";

    /// <summary>Správce obchodu: sklad, produkty, vyřízení objednávek. Sám neobjednává.</summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [User, Admin];
}
