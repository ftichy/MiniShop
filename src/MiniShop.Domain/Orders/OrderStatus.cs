namespace MiniShop.Domain.Orders;

/// <summary>
/// Životní cyklus objednávky od kroku 9 (sága):
/// Pending → Confirmed (sklad zarezervoval) nebo Rejected (nezarezervoval / neodpověděl včas).
/// E-shop krok 8: platba kartou – Pending → AwaitingPayment → Confirmed (zaplaceno) / Rejected (nezaplaceno).
/// E-shop krok 6 (expedice): Confirmed → Packing → Shipped → Completed (= doručeno);
/// zrušit (Cancelled) jde z Confirmed a Packing, po odeslání už ne.
/// </summary>
/// <remarks>Ukládá se jako text – nové hodnoty jen na konec (čísla se tak nikdy neposunou).</remarks>
public enum OrderStatus
{
    /// <summary>Přijata, čeká na rezervaci skladu.</summary>
    Pending = 0,

    /// <summary>Sklad zboží zarezervoval.</summary>
    Confirmed = 1,

    /// <summary>Zamítnuta – zboží nebylo (nebo sklad neodpověděl). Důvod je v RejectionReason.</summary>
    Rejected = 2,

    /// <summary>Doručena (kurýrem) nebo vyzvednuta (osobní odběr). Před e-shop krokem 6 „vyřízena“.</summary>
    Completed = 3,

    Cancelled = 4,

    /// <summary>E-shop krok 6: balí se (osobní odběr: připravuje se).</summary>
    Packing = 5,

    /// <summary>E-shop krok 6: předána dopravci (osobní odběr: připravena k vyzvednutí). Zrušit už nejde.</summary>
    Shipped = 6,

    /// <summary>E-shop krok 8: zboží zarezervované, čeká se na platbu kartou (sága má termín, pak zamítne).</summary>
    AwaitingPayment = 7
}
