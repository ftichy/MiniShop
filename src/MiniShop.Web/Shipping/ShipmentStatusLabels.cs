namespace MiniShop.Web.Shipping;

/// <summary>E-shop krok 26C: české popisky stavů zásilky (ShipmentStatus z modulu Shipping jako text).</summary>
public static class ShipmentStatusLabels
{
    public static string Label(string status) => status switch
    {
        "Created" => "Čeká na převzetí dopravcem",
        "PickedUp" => "Převzato dopravcem",
        "InTransit" => "Na cestě",
        "OutForDelivery" => "Doručuje se",
        "Delivered" => "Doručeno",
        "DeliveryFailed" => "Nezastižen",   // e-shop krok 26D
        "ReturningToSender" => "Vrací se odesílateli",
        "ReturnedToSender" => "Vráceno odesílateli",
        _ => status
    };
}
