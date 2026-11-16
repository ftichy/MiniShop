namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Role uživatelů (účty krok 2). Ukládají se do schématu accounts (AspNetRoles) a při přihlášení do cookie jako claim.
/// Účty krok 12A: místo jedné role Admin sedm zaměstnaneckých rolí podle práce v reálném e-shopu; uživatel jich může mít
/// víc najednou a k tomu i roli zákazníka. Přiděluje je jen správce uživatelů (<see cref="UserAdmin"/>).
/// </summary>
/// <remarks>Jména rolí se ukládají do databáze – nepřejmenovávat (zákazník je proto dál <c>User</c>).</remarks>
public static class AccountRoles
{
    /// <summary>Zákazník: objednává, vidí a ruší své objednávky. Dostane ji každý při registraci.</summary>
    public const string User = "User";

    /// <summary>Správce uživatelů: přiděluje role, blokuje účty, audit a historie přihlášení.</summary>
    public const string UserAdmin = "UserAdmin";

    /// <summary>Správce sortimentu: produkty, kategorie, obrázky, import, obnova katalogu.</summary>
    public const string CatalogManager = "CatalogManager";

    /// <summary>Sklad a expedice: balení, odeslání, doručení, převzetí vrácené zásilky.</summary>
    public const string Warehouse = "Warehouse";

    /// <summary>Zákaznická podpora: všechny objednávky, zrušení za zákazníka, zamítnutí vratky, hodnocení, průběh objednávky.</summary>
    public const string CustomerSupport = "CustomerSupport";

    /// <summary>Marketing: slevové kódy, statistika košíků.</summary>
    public const string Marketing = "Marketing";

    /// <summary>Účetní: faktury, dobropisy, přehled tržeb.</summary>
    public const string Accounting = "Accounting";

    /// <summary>Provoz: zprávy a fronty (dead-letter, zkusit znovu), technické akce.</summary>
    public const string Operations = "Operations";

    /// <summary>
    /// Účty krok 2–11: jediná role správce. Od kroku 12A ji nový kód nepoužívá – převedla se na všechny zaměstnanecké role
    /// (migrace <c>StaffRoles</c>). V databázi zůstává, dokud může běžet starší verze kódu (expand → contract);
    /// při uložení rolí v aplikaci se odebere.
    /// </summary>
    public const string LegacyAdmin = "Admin";

    /// <summary>Zaměstnanecké role – každá vyžaduje dvoufázové ověření.</summary>
    public static readonly IReadOnlyList<string> Staff =
        [UserAdmin, CatalogManager, Warehouse, CustomerSupport, Marketing, Accounting, Operations];

    /// <summary>Všechny role, které jde přidělit (pořadí = pořadí na stránce).</summary>
    public static readonly IReadOnlyList<string> All = [User, .. Staff];

    public static bool IsStaff(string role) => Staff.Contains(role);

    /// <summary>Jméno role pro lidi (stránky, e-maily, audit).</summary>
    public static string Label(string role) => role switch
    {
        User => "Zákazník",
        UserAdmin => "Správce uživatelů",
        CatalogManager => "Správce sortimentu",
        Warehouse => "Sklad a expedice",
        CustomerSupport => "Zákaznická podpora",
        Marketing => "Marketing",
        Accounting => "Účetní",
        Operations => "Provoz",
        LegacyAdmin => "Admin (původní)",
        _ => role
    };
}
