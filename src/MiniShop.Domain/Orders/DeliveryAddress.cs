using System.Text.RegularExpressions;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 4: doručovací adresa – <b>hodnotový objekt</b>: nemá vlastní identitu, dvě adresy se stejnými
/// údaji jsou stejné a po vytvoření se nemění (změna = nová adresa). Platnou adresu nejde vytvořit jinak než přes
/// konstruktor, který ji ověří – objednávka tak nikdy nemá „půl adresy“.
/// </summary>
public sealed partial record DeliveryAddress
{
    public const int MaxLength = 100;

    public DeliveryAddress(string street, string city, string postalCode)
    {
        Street = Required(street, "Ulice a číslo popisné");
        City = Required(city, "Město");

        // PSČ: 5 číslic, mezera uprostřed nevadí („110 00“) – ukládá se bez ní.
        var digits = (postalCode ?? string.Empty).Replace(" ", string.Empty);
        if (!PostalCodePattern().IsMatch(digits))
        {
            throw new DomainException("PSČ musí mít 5 číslic.");
        }

        PostalCode = digits;
    }

    public string Street { get; }
    public string City { get; }

    /// <summary>Pět číslic bez mezery.</summary>
    public string PostalCode { get; }

    /// <summary>PSČ ve tvaru „110 00“ pro zobrazení.</summary>
    public string PostalCodeFormatted => $"{PostalCode[..3]} {PostalCode[3..]}";

    public override string ToString() => $"{Street}, {PostalCodeFormatted} {City}";

    private static string Required(string value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainException($"{field}: povinný údaj.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"{field}: nejvýš {MaxLength} znaků.");
        }

        return trimmed;
    }

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex PostalCodePattern();
}
