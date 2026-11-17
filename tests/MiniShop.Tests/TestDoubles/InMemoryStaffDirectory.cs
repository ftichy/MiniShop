using System.Collections.Concurrent;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Tests.TestDoubles;

/// <summary>E-shop krok 24: „účty“ zaměstnanců v paměti – kdo má k jakému typu oznámení roli (jako IdentityStaffDirectory).</summary>
public sealed class InMemoryStaffDirectory : IStaffDirectory
{
    private readonly ConcurrentDictionary<string, StaffAlertKind[]> _allowed = new();

    public void Set(string userId, params StaffAlertKind[] kinds) => _allowed[userId] = kinds;

    public Task<IReadOnlyList<string>> FilterAllowedAsync(StaffAlertKind kind, IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(userIds
            .Where(id => _allowed.TryGetValue(id, out var kinds) && kinds.Contains(kind))
            .ToList());
}
