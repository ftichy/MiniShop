namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 2: pojmenovaná pravidla „kdo smí co“. Stránky a endpointy používají jen tyto názvy –
/// když se pravidlo změní, změní se na jednom místě (AddAccounts).
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Správa obchodu: produkty, sklad, vyřízení objednávek, průběh objednávek.</summary>
    public const string RequireAdmin = nameof(RequireAdmin);

    /// <summary>Zákazník s potvrzeným e-mailem (účty krok 9): zakládání objednávek. Admin sem nepatří (oddělení povinností).</summary>
    public const string RequireCustomer = nameof(RequireCustomer);
}
