namespace MiniShop.Web.Orders;

/// <summary>
/// Texty a barvy stavů objednávky na stránkách (seznam, detail, expedice, uživatelé) – na jednom místě.
/// E-shop krok 6: osobní odběr má u expedice jiné texty („Připraveno k vyzvednutí“ místo „Odesláno“).
/// </summary>
public static class OrderStatusLabels
{
    public static string Badge(string status) => status switch
    {
        "Pending" => "bg-warning text-dark",
        "AwaitingPayment" => "bg-warning text-dark",
        "Confirmed" => "bg-primary",
        "Packing" => "bg-info text-dark",
        "Shipped" => "bg-primary",
        "Rejected" => "bg-danger",
        "Completed" => "bg-success",
        _ => "bg-secondary"
    };

    /// <param name="shippingMethod"><c>Pickup</c> = osobní odběr (jiné texty expedice), jinak doručení.</param>
    public static string Text(string status, string shippingMethod = "None") => (status, IsPickup(shippingMethod)) switch
    {
        ("Pending", _) => "Ověřujeme sklad…",
        ("AwaitingPayment", _) => "Čeká na platbu",
        ("Confirmed", _) => "Potvrzená",
        ("Packing", false) => "Balíme",
        ("Packing", true) => "Připravujeme",
        ("Shipped", false) => "Odeslaná",
        ("Shipped", true) => "K vyzvednutí",
        ("Rejected", _) => "Zamítnutá",
        ("Completed", false) => "Doručená",
        ("Completed", true) => "Vyzvednutá",
        ("Cancelled", _) => "Zrušená",
        _ => status
    };

    /// <summary>Řádek časové osy na detailu – co se s objednávkou stalo, ne jen název stavu.</summary>
    public static string HistoryText(string status, string shippingMethod = "None") => (status, IsPickup(shippingMethod)) switch
    {
        ("Pending", _) => "Objednávka přijata, ověřujeme sklad",
        ("AwaitingPayment", _) => "Zboží je rezervované – čekáme na platbu kartou",
        ("Confirmed", _) => "Sklad zboží zarezervoval – objednávka potvrzena",
        ("Packing", false) => "Objednávku balíme",
        ("Packing", true) => "Objednávku připravujeme k vyzvednutí",
        ("Shipped", false) => "Objednávka odeslána",
        ("Shipped", true) => "Objednávka je připravená k vyzvednutí",
        ("Rejected", _) => "Objednávka zamítnuta",
        ("Completed", false) => "Objednávka doručena",
        ("Completed", true) => "Objednávka vyzvednuta",
        ("Cancelled", _) => "Objednávka zrušena",
        _ => status
    };

    /// <summary>
    /// Zrušit jde kdykoli před odesláním (e-shop krok 9: i čekající na sklad nebo na platbu) – stejné pravidlo jako Order.Cancel.
    /// </summary>
    public static bool CanCancel(string status) => status is "Pending" or "AwaitingPayment" or "Confirmed" or "Packing";

    private static bool IsPickup(string shippingMethod) => shippingMethod == "Pickup";
}
