using System.Text.Json;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Opak <see cref="OutboxMessageFactory"/>: z řádku outboxu znovu sestaví doménovou událost.
/// </summary>
/// <remarks>
/// Payload nese jen OrderId, ale handlery čekají událost s celou entitou Order.
/// Objednávku proto načteme z DB – handlery tak zůstávají beze změny.
/// Pozor: handler uvidí AKTUÁLNÍ stav objednávky, ne stav v okamžiku události.
/// EventId se převezme z řádku – při každém opakování je stejné, takže inbox (krok 6) pozná duplicitu.
/// </remarks>
public sealed class OutboxEventReader(IOrderRepository orders)
{
    public async Task<IDomainEvent> ReadAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        message.Type switch
        {
            nameof(OrderPlaced) => new OrderPlaced(await LoadOrderAsync(message, cancellationToken), message.OccurredAtUtc)
                { EventId = message.Id },
            nameof(OrderCompleted) => new OrderCompleted(await LoadOrderAsync(message, cancellationToken), message.OccurredAtUtc)
                { EventId = message.Id },
            nameof(OrderConfirmed) => new OrderConfirmed(await LoadOrderAsync(message, cancellationToken), message.OccurredAtUtc)
                { EventId = message.Id },
            nameof(OrderRejected) => new OrderRejected(await LoadOrderAsync(message, cancellationToken), message.OccurredAtUtc)
                { EventId = message.Id },
            nameof(OrderCancelled) => new OrderCancelled(await LoadOrderAsync(message, cancellationToken), message.OccurredAtUtc)
                { EventId = message.Id },
            _ => throw new InvalidOperationException($"Neznámý typ zprávy v outboxu: {message.Type}.")
        };

    private async Task<Order> LoadOrderAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OrderPayload>(message.Payload)
            ?? throw new InvalidOperationException($"Zpráva {message.Id} má prázdný payload.");

        return await orders.GetByIdAsync(payload.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Objednávka {payload.OrderId} ze zprávy {message.Id} neexistuje.");
    }
}
