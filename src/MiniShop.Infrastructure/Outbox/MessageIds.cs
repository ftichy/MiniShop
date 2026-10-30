using System.Security.Cryptography;
using System.Text;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// E-shop krok 2: ID zprávy odvozené z jiného ID – stejný vstup dá vždy stejné ID.
/// </summary>
/// <remarks>
/// Když jedna doménová událost vede na dvě zprávy (ProductUpdatedV1 + V2), nesmí mít stejné ID: inbox příjemce
/// rozlišuje zprávy podle (ID, handler), takže by druhou zahodil jako duplicitu, a v outboxu je ID primární klíč.
/// Náhodné ID nejde – při opakovaném uložení stejné události musí vyjít stejné (jako EventId u první zprávy).
/// </remarks>
public static class MessageIds
{
    public static Guid Derive(Guid sourceId, string purpose)
    {
        var hash = SHA256.HashData([.. sourceId.ToByteArray(), .. Encoding.UTF8.GetBytes(purpose)]);
        return new Guid(hash.AsSpan(0, 16));
    }
}
