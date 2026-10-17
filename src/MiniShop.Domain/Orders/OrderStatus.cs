namespace MiniShop.Domain.Orders;

/// <summary>
/// Životní cyklus objednávky od kroku 9 (sága):
/// Pending → Confirmed (sklad zarezervoval) nebo Rejected (nezarezervoval / neodpověděl včas),
/// z Confirmed pak Completed nebo Cancelled.
/// </summary>
public enum OrderStatus
{
    /// <summary>Přijata, čeká na rezervaci skladu.</summary>
    Pending = 0,

    /// <summary>Sklad zboží zarezervoval.</summary>
    Confirmed = 1,

    /// <summary>Zamítnuta – zboží nebylo (nebo sklad neodpověděl). Důvod je v RejectionReason.</summary>
    Rejected = 2,

    Completed = 3,
    Cancelled = 4
}
