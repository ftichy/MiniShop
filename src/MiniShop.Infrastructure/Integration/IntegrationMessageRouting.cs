using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Jeden záznam "modul X zpracovává zprávu Y handlerem Z". Vzniká v AddIntegrationMessageHandler.
/// Invoke zavolá handler bez reflexe – typy jsou známé už při registraci.
/// </summary>
public sealed record IntegrationMessageSubscription(
    string Module,
    Type MessageType,
    Type HandlerType,
    Func<IServiceProvider, IIntegrationMessage, CancellationToken, Task> Invoke);

/// <summary>
/// Kdo co odebírá (krok 9). Podle toho InProcessIntegrationBus vybírá handlery a RabbitMQ zakládá fronty
/// (jedna na modul, "minishop.{modul}") a jejich napojení na exchange.
/// </summary>
/// <remarks>
/// Hlídá pravidlo příkazů: příkaz smí mít jen JEDEN handler v celé aplikaci (patří jedné službě).
/// Událost jich může mít libovolně mnoho.
/// </remarks>
public sealed class IntegrationMessageRouting
{
    private readonly IReadOnlyList<IntegrationMessageSubscription> _subscriptions;

    public IntegrationMessageRouting(IEnumerable<IntegrationMessageSubscription> subscriptions)
    {
        _subscriptions = subscriptions.ToList();

        var commandWithMoreHandlers = _subscriptions
            .Where(s => typeof(IIntegrationCommand).IsAssignableFrom(s.MessageType))
            .GroupBy(s => s.MessageType)
            .FirstOrDefault(g => g.Count() > 1);
        if (commandWithMoreHandlers is not null)
        {
            throw new InvalidOperationException(
                $"Příkaz {commandWithMoreHandlers.Key.Name} má víc handlerů " +
                $"({string.Join(", ", commandWithMoreHandlers.Select(s => s.HandlerType.Name))}). Příkaz smí mít jen jednoho příjemce.");
        }
    }

    /// <summary>Všechny moduly, které něco odebírají.</summary>
    public IReadOnlyList<string> Modules => _subscriptions.Select(s => s.Module).Distinct().ToList();

    /// <summary>Moduly, které zprávu daného typu odebírají.</summary>
    public IReadOnlyList<string> ModulesFor(Type messageType) =>
        _subscriptions.Where(s => s.MessageType == messageType).Select(s => s.Module).Distinct().ToList();

    /// <summary>Názvy kontraktů, které modul odebírá – na ty se v RabbitMQ naváže jeho fronta.</summary>
    public IReadOnlyList<string> ContractNamesFor(string module) =>
        _subscriptions.Where(s => s.Module == module).Select(s => s.MessageType.Name).Distinct().ToList();

    public IReadOnlyList<IntegrationMessageSubscription> For(string module, Type messageType) =>
        _subscriptions.Where(s => s.Module == module && s.MessageType == messageType).ToList();
}
