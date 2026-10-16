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

    public void ClearDomainEvents() => _domainEvents.Clear();
}
