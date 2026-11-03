using System.Security.Cryptography;
using System.Text;

namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: podpis webhooku – HMAC-SHA256 těla zprávy sdíleným tajemstvím, hexadecimálně v hlavičce
/// <see cref="HeaderName"/>. Bez tajemství podpis spočítat nejde a změna jediného znaku těla ho rozbije.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-MiniShop-Signature";

    public static string Compute(string secret, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    /// <summary>Porovnání v konstantním čase – útočník nepozná z doby odpovědi, kolik znaků podpisu trefil.</summary>
    public static bool IsValid(string secret, string body, string? signature) =>
        signature is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Compute(secret, body)),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
}
