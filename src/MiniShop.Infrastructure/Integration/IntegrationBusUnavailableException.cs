namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Broker není dostupný (neběží, spadlo spojení). Chyba není ve zprávě, ale "na poště" – proto se
/// zpráva v outboxu nepočítá jako neúspěšný pokus a nestane se z ní poison message (krok 8).
/// </summary>
public sealed class IntegrationBusUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
