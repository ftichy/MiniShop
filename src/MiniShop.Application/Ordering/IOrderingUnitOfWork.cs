using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Uložení změn modulu Ordering (krok 10): objednávky, ságy, kopie katalogu a jeho outbox v databázi ordering.db.
/// Vlastní rozhraní pro každý modul – handler Orderingu tak omylem neuloží změny do cizí databáze.
/// </summary>
public interface IOrderingUnitOfWork : IUnitOfWork;
