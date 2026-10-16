namespace MiniShop.Domain.Exceptions;

/// <summary>
/// Porušení business pravidla (např. nedostatek zboží, neplatný stav objednávky).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
