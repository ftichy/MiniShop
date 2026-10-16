namespace MiniShop.Domain.Common;

/// <summary>
/// Doménová událost: business fakt, který se stal (pojmenovaný v minulém čase).
/// Je neměnná a vzniká uvnitř entity při změně jejího stavu.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}
