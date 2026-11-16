using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 2: pojmenovaná pravidla „kdo smí co“. Stránky a endpointy používají jen tyto názvy –
/// když se pravidlo změní, změní se na jednom místě (AddAccounts).
/// Účty krok 12B: správa po oblastech – každá oblast má vlastní politiku a seznam rolí v <see cref="StaffPolicies"/>.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Kterákoli zaměstnanecká role (+ 2FA): zámek složky <c>/Admin</c>, štítek „správa“, přesměrování na nastavení 2FA.
    /// Co konkrétně smí, rozhodují politiky oblastí níže.
    /// </summary>
    public const string RequireAdmin = nameof(RequireAdmin);

    /// <summary>Zákazník s potvrzeným e-mailem (účty krok 9): zakládání objednávek, košík, pokladna, vratky.</summary>
    public const string RequireCustomer = nameof(RequireCustomer);

    /// <summary>Uživatelé, role, blokace, historie přihlášení.</summary>
    public const string RequireUserAdmin = nameof(RequireUserAdmin);

    /// <summary>Produkty a kategorie – založit, upravit, smazat, obrázky.</summary>
    public const string RequireCatalog = nameof(RequireCatalog);

    /// <summary>Přehled produktů a naskladnění (sortiment i sklad).</summary>
    public const string RequireStock = nameof(RequireStock);

    /// <summary>Expedice – balení, odeslání, doručení; převzetí vrácené zásilky.</summary>
    public const string RequireFulfilment = nameof(RequireFulfilment);

    /// <summary>Stránka Vratky (přehled) – sklad přebírá, podpora zamítá.</summary>
    public const string RequireReturns = nameof(RequireReturns);

    /// <summary>Všechny objednávky (seznam a detail).</summary>
    public const string RequireOrderOverview = nameof(RequireOrderOverview);

    /// <summary>Zákaznická podpora – zrušení cizí objednávky, zamítnutí vratky, hodnocení, průběh a přehrání objednávky.</summary>
    public const string RequireSupport = nameof(RequireSupport);

    /// <summary>Kupony a statistika košíků.</summary>
    public const string RequireMarketing = nameof(RequireMarketing);

    /// <summary>Faktury a dobropisy (přehled, PDF cizí objednávky).</summary>
    public const string RequireInvoices = nameof(RequireInvoices);

    /// <summary>Přehled tržeb i jeho přestavění (tlačítko je na stejné stránce).</summary>
    public const string RequireAccounting = nameof(RequireAccounting);

    /// <summary>Zprávy a fronty (dead-letter, zkusit znovu).</summary>
    public const string RequireOperations = nameof(RequireOperations);

    /// <summary>Obnova kopie katalogu ze skladu (sortiment i provoz).</summary>
    public const string RequireCatalogSync = nameof(RequireCatalogSync);
}

/// <summary>
/// Účty krok 12B: jediné místo, kde se rozhoduje, které role smí do které oblasti správy. Každá politika = aspoň jedna
/// z rolí + zapnuté dvoufázové ověření. Tabulka je i v příručce (modul Accounts) a ověřuje ji <c>StaffAreaTests</c>.
/// </summary>
public static class StaffPolicies
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Roles = new Dictionary<string, IReadOnlyList<string>>
    {
        [AuthorizationPolicies.RequireAdmin] = AccountRoles.Staff,
        [AuthorizationPolicies.RequireUserAdmin] = [AccountRoles.UserAdmin],
        [AuthorizationPolicies.RequireCatalog] = [AccountRoles.CatalogManager],
        [AuthorizationPolicies.RequireStock] = [AccountRoles.CatalogManager, AccountRoles.Warehouse],
        [AuthorizationPolicies.RequireFulfilment] = [AccountRoles.Warehouse],
        [AuthorizationPolicies.RequireReturns] = [AccountRoles.Warehouse, AccountRoles.CustomerSupport],
        [AuthorizationPolicies.RequireOrderOverview] = [AccountRoles.Warehouse, AccountRoles.CustomerSupport, AccountRoles.Accounting],
        [AuthorizationPolicies.RequireSupport] = [AccountRoles.CustomerSupport],
        [AuthorizationPolicies.RequireMarketing] = [AccountRoles.Marketing],
        [AuthorizationPolicies.RequireInvoices] = [AccountRoles.Accounting, AccountRoles.CustomerSupport],
        [AuthorizationPolicies.RequireAccounting] = [AccountRoles.Accounting],
        [AuthorizationPolicies.RequireOperations] = [AccountRoles.Operations],
        [AuthorizationPolicies.RequireCatalogSync] = [AccountRoles.CatalogManager, AccountRoles.Operations],
    };
}
