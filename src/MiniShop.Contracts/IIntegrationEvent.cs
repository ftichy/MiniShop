namespace MiniShop.Contracts;

/// <summary>
/// Integrační událost: zpráva pro JINÉ služby (krok 7). Na rozdíl od doménové události nese jen jednoduchá data
/// a její tvar je kontrakt – smí se jen zpětně kompatibilně rozšiřovat. Větší změna = nová verze (…V2).
/// </summary>
/// <remarks>
/// Pravidla verzování:
/// - nové volitelné pole přidat smíte,
/// - přejmenovat nebo odebrat pole nesmíte – vytvoří se nová verze a chvíli se publikují obě,
/// - příjemce ignoruje pole, která nezná (tolerant reader).
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>Jednoznačné ID zprávy – příjemce podle něj pozná duplicitu (inbox, krok 6).</summary>
    Guid EventId { get; }

    /// <summary>Kdy se business fakt stal (ne kdy byla zpráva odeslána).</summary>
    DateTime OccurredAtUtc { get; }
}
