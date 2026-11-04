using System.Text.RegularExpressions;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Coupons;

/// <summary>
/// E-shop krok 13: slevový kód – agregát s pravidly (platnost, minimum objednávky, limit použití). Patří modulu Ordering,
/// takže uplatnění kuponu, vznik objednávky a vyprázdnění košíku jsou jedna transakce (žádná sága).
/// </summary>
/// <remarks>
/// <para>
/// Záludnost je <b>limit použití</b>: poslední použití můžou chtít dvě pokladny naráz. Každé uplatnění proto zvýší
/// <see cref="Version"/> (concurrency token) – druhé uložení narazí na <c>UPDATE … WHERE Version = původní</c>
/// a celá jeho transakce (i objednávka) se zruší. Projde jen jedno.
/// </para>
/// <para>
/// Pravidlo „jeden zákazník jednou“ hlídá pokladna dotazem na objednávky – i ten je díky verzi kuponu bezpečný:
/// dvě souběžná použití stejného kuponu se nikdy obě neuloží.
/// </para>
/// </remarks>
public sealed partial class Coupon
{
    public const int CodeMinLength = 3;
    public const int CodeMaxLength = 20;
    public const int MaxPercent = 90;

    // Konstruktor pro EF Core.
    private Coupon()
    {
    }

    public int Id { get; private set; }

    /// <summary>Kód velkými písmeny (A–Z, číslice, pomlčka) – zákazník ho může psát malými.</summary>
    public string Code { get; private set; } = string.Empty;

    public CouponKind Kind { get; private set; }

    /// <summary>Procenta (1–90) nebo Kč podle <see cref="Kind"/>.</summary>
    public decimal Value { get; private set; }

    /// <summary>Do kdy platí (včetně); null = bez omezení.</summary>
    public DateTime? ValidUntilUtc { get; private set; }

    /// <summary>Minimální hodnota položek (po slevě za množství), od které kupon platí.</summary>
    public decimal MinItemsTotal { get; private set; }

    /// <summary>Kolikrát smí být použit celkem; null = bez omezení.</summary>
    public int? MaxRedemptions { get; private set; }

    /// <summary>Kolikrát je použitý (zamítnuté a zrušené objednávky se odečtou – <see cref="Release"/>).</summary>
    public int RedemptionCount { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Concurrency token – každé použití nebo vrácení ji zvýší.</summary>
    public int Version { get; private set; }

    public static Coupon Create(string code, CouponKind kind, decimal value, DateTime? validUntilUtc,
        decimal minItemsTotal, int? maxRedemptions, DateTime createdAtUtc)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length is < CodeMinLength or > CodeMaxLength || !CodePattern().IsMatch(normalized))
        {
            throw new DomainException($"Kód musí mít {CodeMinLength}–{CodeMaxLength} znaků: písmena bez diakritiky, číslice, pomlčka.");
        }

        if (kind == CouponKind.Percent && value is < 1 or > MaxPercent)
        {
            throw new DomainException($"Procentní sleva musí být 1–{MaxPercent} %.");
        }

        if (kind == CouponKind.Amount && value <= 0)
        {
            throw new DomainException("Sleva v Kč musí být větší než nula.");
        }

        if (minItemsTotal < 0 || maxRedemptions is <= 0)
        {
            throw new DomainException("Minimum objednávky nesmí být záporné a limit použití musí být aspoň 1.");
        }

        return new Coupon
        {
            Code = normalized,
            Kind = kind,
            Value = decimal.Round(value, 2),
            ValidUntilUtc = validUntilUtc,
            MinItemsTotal = minItemsTotal,
            MaxRedemptions = maxRedemptions,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    /// <summary>Jak zákazník kód napsal → jak se ukládá a hledá (bez mezer, velkými písmeny).</summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public bool IsExhausted => MaxRedemptions is { } max && RedemptionCount >= max;

    /// <summary>Proč kupon na tuhle objednávku nejde použít, nebo null (jde).</summary>
    public string? WhyNotApplicable(decimal itemsTotal, DateTime nowUtc) =>
        !IsActive ? "Slevový kód už neplatí."
        : ValidUntilUtc is { } until && nowUtc > until ? "Platnost slevového kódu skončila."
        : IsExhausted ? "Slevový kód je už vyčerpaný."
        : itemsTotal < MinItemsTotal ? $"Slevový kód platí pro nákup od {MinItemsTotal:N0} Kč."
        : null;

    /// <summary>Kolik kupon ze zboží odečte (nikdy víc, než zboží stojí).</summary>
    public decimal DiscountFor(decimal itemsTotal) => Kind == CouponKind.Percent
        ? decimal.Round(itemsTotal * Value / 100m, 2, MidpointRounding.AwayFromZero)
        : Math.Min(Value, itemsTotal);

    /// <summary>Použití při objednání. Vrací slevu v Kč. Zvýší verzi – souběžné poslední použití neprojde dvakrát.</summary>
    public decimal Redeem(decimal itemsTotal, DateTime nowUtc)
    {
        if (WhyNotApplicable(itemsTotal, nowUtc) is { } reason)
        {
            throw new DomainException(reason);
        }

        RedemptionCount++;
        Version++;
        return DiscountFor(itemsTotal);
    }

    /// <summary>Kompenzace: objednávka s kuponem byla zamítnuta nebo zrušena – použití se vrací.</summary>
    public void Release()
    {
        if (RedemptionCount > 0)
        {
            RedemptionCount--;
            Version++;
        }
    }

    /// <summary>Admin kupon vypnul – další objednávky ho nepoužijí (už uplatněné zůstávají).</summary>
    public void Deactivate()
    {
        IsActive = false;
        Version++;
    }

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex CodePattern();
}

/// <remarks>Ukládá se jako text – nové hodnoty jen na konec.</remarks>
public enum CouponKind
{
    /// <summary>Procenta ze zboží (po slevě za množství).</summary>
    Percent,

    /// <summary>Pevná částka v Kč.</summary>
    Amount
}
