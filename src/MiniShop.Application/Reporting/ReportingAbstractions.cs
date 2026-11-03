using MiniShop.Application.Abstractions;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>E-shop krok 11: uložení změn modulu Reporting – čtecí model, inbox a outbox v reporting.db.</summary>
public interface IReportingUnitOfWork : IUnitOfWork;

/// <summary>E-shop krok 11: zprávy z Reportingu (jen příkaz na přestavění) jdou do outboxu v reporting.db.</summary>
public interface IReportingMessagePublisher : IIntegrationMessagePublisher;

/// <summary>E-shop krok 11: objednávky v čtecím modelu přehledu tržeb.</summary>
public interface ISalesOrderRepository
{
    Task<SalesOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Objednávky vzniklé od <paramref name="fromUtc"/> i s položkami (jen ty se známými detaily).</summary>
    Task<IReadOnlyList<SalesOrder>> GetPlacedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Kdy projekce naposledy něco změnila, nebo null (přehled je prázdný).</summary>
    Task<DateTime?> GetLastUpdatedAtAsync(CancellationToken cancellationToken = default);

    void Add(SalesOrder order);

    /// <summary>Smaže celý čtecí model (před přestavěním). Projekce jde kdykoli postavit znovu – to je smysl CQRS.</summary>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}
