namespace MiniShop.Domain.Common;

/// <summary>
/// Doménová událost: business fakt, který se stal (pojmenovaný v minulém čase).
/// Je neměnná a vzniká uvnitř entity při změně jejího stavu.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Jednoznačné "číslo zásilky" (krok 6). Při opakovaném doručení z outboxu zůstává stejné,
    /// takže handler pozná, že tuhle událost už zpracoval.
    /// </summary>
    Guid EventId { get; }

    DateTime OccurredAtUtc { get; }
}
