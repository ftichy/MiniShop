using MiniShop.Domain.Exceptions;

namespace MiniShop.Application.Abstractions;

/// <summary>
/// Účty krok 12C: pravidlo čtyř očí – zaměstnanec, který je i zákazník, nevyřizuje sám sebe (vlastní objednávku nebalí
/// ani nedoručí, vlastní vratku nepřevezme ani nezamítne, vlastní hodnocení neskryje). Vyřídí to kolega.
/// </summary>
/// <remarks>
/// <c>actingUserId</c> = ID účtu zaměstnance, který akci dělá; null = systém (timeout worker, sága) – na ten se pravidlo
/// nevztahuje. Kontrola je v aplikační vrstvě, aby platila pro stránky i API.
/// </remarks>
public static class ConflictOfInterest
{
    public const string Message = "Vlastní objednávku musí vyřídit kolega (střet zájmů).";

    public static void EnsureNotOwn(string ownerId, string? actingUserId, string message = Message)
    {
        if (actingUserId is not null && !string.IsNullOrEmpty(ownerId) && ownerId == actingUserId)
        {
            throw new DomainException(message);
        }
    }
}
