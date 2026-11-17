using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>E-shop krok 24: volby oznámení k práci (modul Notifications).</summary>
public interface IStaffAlertSubscriptionRepository
{
    Task<IReadOnlyList<StaffAlertSubscription>> ListForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListUserIdsAsync(StaffAlertKind kind, CancellationToken cancellationToken = default);

    void Add(StaffAlertSubscription subscription);

    void Remove(StaffAlertSubscription subscription);

    /// <summary>Zapomenout účet (smazání účtu).</summary>
    Task<int> RemoveForUserAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// E-shop krok 24: kdo z přihlášených k oznámení ho smí dostat TEĎ – má k typu roli a není zablokovaný. Implementuje
/// Web nad účty (Notifications role ani účty nezná; přiřazení typ → role je ve Webu, <c>StaffAlertRoles</c>).
/// Dotaz při odeslání, ne kopie: komu roli mezitím odebrali, oznámení nedostane.
/// </summary>
public interface IStaffDirectory
{
    Task<IReadOnlyList<string>> FilterAllowedAsync(StaffAlertKind kind, IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// E-shop krok 24: název produktu pro oznámení „Došlo zboží“ – zpráva o dostupnosti nese jen ID. Implementuje Web nad
/// kopií katalogu (Notifications katalog nezná).
/// </summary>
public interface IProductNames
{
    Task<string?> FindAsync(int productId, CancellationToken cancellationToken = default);
}

/// <summary>E-shop krok 24: zapnutí a vypnutí oznámení k práci na stránce E-maily a oznámení.</summary>
public sealed class StaffAlertService(
    IStaffAlertSubscriptionRepository subscriptions,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlySet<StaffAlertKind>> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        (await subscriptions.ListForUserAsync(userId, cancellationToken)).Select(s => s.Kind).ToHashSet();

    /// <summary>
    /// Uloží volbu. <paramref name="allowed"/> = typy, ke kterým má zaměstnanec roli (zjistí Web) – jiné se nezapnou,
    /// ani když je formulář pošle; volby k typům mimo <paramref name="allowed"/> zůstanou, jak byly.
    /// </summary>
    public async Task SetAsync(string userId, IReadOnlyCollection<StaffAlertKind> wanted, IReadOnlyCollection<StaffAlertKind> allowed,
        CancellationToken cancellationToken = default)
    {
        var current = await subscriptions.ListForUserAsync(userId, cancellationToken);
        foreach (var kind in allowed)
        {
            var existing = current.FirstOrDefault(s => s.Kind == kind);
            if (wanted.Contains(kind) && existing is null)
            {
                subscriptions.Add(new StaffAlertSubscription(userId, kind, timeProvider.GetUtcNow().UtcDateTime));
            }
            else if (!wanted.Contains(kind) && existing is not null)
            {
                subscriptions.Remove(existing);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
