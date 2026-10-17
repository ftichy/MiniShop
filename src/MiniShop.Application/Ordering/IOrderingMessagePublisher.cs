using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Ordering;

/// <summary>Odesílání integračních zpráv z modulu Ordering – zapisuje do outboxu v ordering.db (krok 10).</summary>
public interface IOrderingMessagePublisher : IIntegrationMessagePublisher;
