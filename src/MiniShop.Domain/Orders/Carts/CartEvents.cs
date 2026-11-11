namespace MiniShop.Domain.Orders.Carts;

/// <summary>
/// E-shop krok 17: událost košíku – <b>zdroj pravdy</b> (event sourcing). Stav košíku = součet jeho událostí
/// (<see cref="Cart.Apply"/>). Události se jen přidávají, nikdy nemění ani nemažou (kromě smazání účtu).
/// </summary>
/// <remarks>
/// Nejsou to doménové události pro outbox (<c>IDomainEvent</c>) ani zprávy pro jiné moduly – nikam se neposílají,
/// jen se ukládají do <c>CartEvents</c>. Jméno typu se ukládá jako text: <b>události nepřejmenovávat</b>, změna tvaru =
/// nový typ (…V2) nebo převod staré podoby při čtení (upcasting).
/// </remarks>
/// <param name="OccurredAtUtc">Kdy se to stalo – zároveň „poslední změna košíku“.</param>
public abstract record CartEvent(DateTime OccurredAtUtc);

/// <summary>Řádek v události (produkt, množství, cena v tu chvíli).</summary>
public sealed record CartEventLine(int ProductId, int Quantity, decimal Price);

/// <summary>Událost, která mění obsah košíku – nese novou verzi pro pokladnu (<see cref="Cart.CheckoutId"/>).</summary>
public abstract record CartContentEvent(DateTime OccurredAtUtc, Guid CheckoutId) : CartEvent(OccurredAtUtc);

/// <summary>Přidán produkt – nový řádek, nebo navýšení množství existujícího (pak se zapamatuje i aktuální cena).</summary>
public sealed record CartItemAdded(DateTime OccurredAtUtc, Guid CheckoutId, int ProductId, int Quantity, decimal Price)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Zákazník změnil množství řádku na <paramref name="Quantity"/> (nová hodnota, ne rozdíl).</summary>
public sealed record CartItemQuantityChanged(DateTime OccurredAtUtc, Guid CheckoutId, int ProductId, int Quantity)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Řádek odebrán.</summary>
public sealed record CartItemRemoved(DateTime OccurredAtUtc, Guid CheckoutId, int ProductId)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Zákazník potvrdil nové ceny (e-shop krok 4).</summary>
public sealed record CartPricesAccepted(DateTime OccurredAtUtc, Guid CheckoutId, IReadOnlyList<CartEventLine> Prices)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Uplatněn slevový kód (e-shop krok 13).</summary>
public sealed record CartCouponApplied(DateTime OccurredAtUtc, Guid CheckoutId, string CouponCode)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

public sealed record CartCouponRemoved(DateTime OccurredAtUtc, Guid CheckoutId)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>
/// Nákup skončil objednávkou – košík je prázdný. Nese objednané řádky (statistika „kolik nákupů skončilo objednávkou“).
/// <b>Reset:</b> stav po něm nezávisí na dřívějších událostech – načtení košíku začíná tady.
/// </summary>
public sealed record CartCheckedOut(DateTime OccurredAtUtc, Guid CheckoutId, IReadOnlyList<CartEventLine> Lines)
    : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Košík vyprázdněn bez objednávky. <b>Reset</b> jako <see cref="CartCheckedOut"/>.</summary>
public sealed record CartCleared(DateTime OccurredAtUtc, Guid CheckoutId) : CartContentEvent(OccurredAtUtc, CheckoutId);

/// <summary>Odešla připomínka opuštěného košíku (e-shop krok 10) – obsah ani verze pro pokladnu se nemění.</summary>
public sealed record CartReminderSent(DateTime OccurredAtUtc) : CartEvent(OccurredAtUtc);

/// <summary>
/// Celý stav košíku najednou (snapshot) – košík z doby před event sourcingem, nebo změna, kterou udělala starší verze
/// kódu jen v tabulkách (postupné nasazení). <b>Reset:</b> načtení začíná tady.
/// </summary>
/// <param name="Reason"><see cref="ImportedReason"/> nebo <see cref="ResyncedReason"/>.</param>
public sealed record CartSnapshotTaken(
    DateTime OccurredAtUtc,
    Guid CheckoutId,
    IReadOnlyList<CartEventLine> Lines,
    string? CouponCode,
    DateTime? ReminderSentAtUtc,
    string Reason) : CartContentEvent(OccurredAtUtc, CheckoutId)
{
    public const string ImportedReason = "imported";
    public const string ResyncedReason = "resynced";
}

/// <summary>Událost s pořadovým číslem v proudu košíku (verze 1, 2, 3…).</summary>
public sealed record VersionedCartEvent(int Version, CartEvent Event);
