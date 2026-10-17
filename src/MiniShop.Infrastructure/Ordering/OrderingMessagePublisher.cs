using MiniShop.Application.Ordering;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>Zprávy z modulu Ordering jdou do outboxu v ordering.db (krok 10).</summary>
public sealed class OrderingMessagePublisher(OrderingDbContext db)
    : OutboxIntegrationMessagePublisher(db), IOrderingMessagePublisher;
