namespace MiniShop.Contracts;

/// <summary>
/// Integrační příkaz (krok 9): "udělej" (rozkazovací způsob, ReserveStock). Má právě JEDNOHO příjemce –
/// službu, která příkaz vlastní (ReserveStockV1 patří skladu). Odesílatel čeká odpověď v podobě události.
/// </summary>
/// <remarks>
/// Pro kontrakty příkazů platí stejná pravidla verzování jako pro události (viz <see cref="IIntegrationEvent"/>).
/// </remarks>
public interface IIntegrationCommand : IIntegrationMessage
{
    /// <summary>Jednoznačné ID příkazu – příjemce podle něj pozná duplicitu (inbox).</summary>
    Guid CommandId { get; }

    Guid IIntegrationMessage.MessageId => CommandId;
}
