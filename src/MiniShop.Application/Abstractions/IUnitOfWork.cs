namespace MiniShop.Application.Abstractions;

/// <summary>
/// Uloží všechny změny provedené přes repozitáře najednou (v jedné transakci).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
