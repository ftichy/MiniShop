namespace MiniShop.Contracts;

/// <summary>
/// Společný předek všech zpráv mezi službami: událostí (<see cref="IIntegrationEvent"/>) i příkazů
/// (<see cref="IIntegrationCommand"/>). Outbox, broker a inbox s nimi zacházejí stejně.
/// </summary>
/// <remarks>
/// MessageId je odvozené (EventId nebo CommandId) a do JSONu se neserializuje – tvar kontraktů z kroku 7 se tak nezměnil.
/// </remarks>
public interface IIntegrationMessage
{
    Guid MessageId { get; }

    /// <summary>Kdy se fakt stal / kdy byl příkaz vydán (ne kdy byla zpráva odeslána).</summary>
    DateTime OccurredAtUtc { get; }
}
