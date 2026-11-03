using System.Collections.Concurrent;
using MiniShop.Application.Notifications;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// E-shop krok 10: kontakty zákazníků pro Notifications bez účtů (users.db) – testy nastaví e-mail a souhlas
/// přes <see cref="Set"/>; neznámý zákazník = smazaný účet (null).
/// </summary>
public sealed class InMemoryCustomerContacts : ICustomerContacts
{
    private readonly ConcurrentDictionary<string, CustomerContact> _contacts = new();

    public void Set(string customerId, CustomerContact contact) => _contacts[customerId] = contact;

    public void Remove(string customerId) => _contacts.TryRemove(customerId, out _);

    public Task<CustomerContact?> FindAsync(string customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_contacts.GetValueOrDefault(customerId));
}
