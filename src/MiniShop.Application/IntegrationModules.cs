namespace MiniShop.Application;

/// <summary>
/// Názvy modulů (krok 9). Modul = skupina handlerů integračních zpráv s vlastní frontou v RabbitMQ ("minishop.{modul}").
/// V kroku 10 se z modulu stane samostatná služba.
/// </summary>
public static class IntegrationModules
{
    /// <summary>Objednávky + orchestrátor ságy.</summary>
    public const string Ordering = "ordering";

    /// <summary>Sklad: rezervace a jejich uvolnění.</summary>
    public const string Inventory = "inventory";

    /// <summary>E-shop krok 7: e-maily zákazníkovi podle událostí objednávky (jen poslouchá, nic neposílá).</summary>
    public const string Notifications = "notifications";

    /// <summary>E-shop krok 8: platby kartou – účastník ságy objednávky (RequestPayment, RefundPayment).</summary>
    public const string Payments = "payments";

    /// <summary>E-shop krok 11: přehled tržeb pro admina (CQRS – čtecí model z událostí objednávek a plateb).</summary>
    public const string Reporting = "reporting";

    /// <summary>E-shop krok 14: hodnocení produktů – kopie nákupů z událostí objednávek, souhrn hodnocení pro výlohu.</summary>
    public const string Reviews = "reviews";

    /// <summary>E-shop krok 15: faktury – kopie objednávek z událostí, vystavení po zaplacení, PDF.</summary>
    public const string Invoicing = "invoicing";
}
