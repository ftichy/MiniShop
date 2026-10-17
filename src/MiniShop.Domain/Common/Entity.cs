namespace MiniShop.Domain.Common;

/// <summary>
/// Společný předek entit, které umí vyvolávat doménové události.
/// Entita události zatím jen sbírá – doručení zajistí infrastruktura (krok 2: dispatcher).
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>Události vyvolané od posledního vyčištění. EF Core je neukládá (viz konfigurace).</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Zahodí dosud neuložené události daného typu (např. když novější událost nese aktuální stav).</summary>
    protected void RemoveDomainEvents<TEvent>() where TEvent : IDomainEvent => _domainEvents.RemoveAll(e => e is TEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
