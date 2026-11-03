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
}
