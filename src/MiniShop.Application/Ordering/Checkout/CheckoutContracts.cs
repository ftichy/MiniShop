using System.ComponentModel.DataAnnotations;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Checkout;

/// <summary>
/// E-shop krok 4: co pokladna ukáže – košík s aktuálními cenami, slevy, možnosti dopravy a předvyplněná adresa.
/// </summary>
/// <param name="CheckoutId">Verze košíku – formulář ji pošle zpět (idempotence a „objednávám to, co vidím“).</param>
/// <param name="Discount">Součet slev (stejná pravidla jako objednávka).</param>
/// <param name="ItemsTotal">Položky po slevách, bez dopravy (jen ty, které jdou objednat).</param>
/// <param name="LastAddress">Adresa z poslední objednávky kurýrem, nebo null.</param>
public sealed record CheckoutDto(
    CartDto Cart,
    Guid CheckoutId,
    decimal Discount,
    decimal ItemsTotal,
    IReadOnlyList<ShippingOptionDto> ShippingOptions,
    DeliveryAddressDto? LastAddress)
{
    public bool HasPriceChanges => Cart.Lines.Any(l => l.PriceChanged);

    /// <summary>Stažený nebo vyprodaný produkt – zákazník ho musí odebrat.</summary>
    public bool HasUnavailableItems => Cart.Lines.Any(l => !l.IsOrderable);

    public bool CanPlaceOrder => !Cart.IsEmpty && !HasPriceChanges && !HasUnavailableItems;
}

/// <param name="Price">Cena dopravy pro aktuální obsah košíku (kurýr nad limit zdarma).</param>
public sealed record ShippingOptionDto(ShippingMethod Method, decimal Price);

public sealed record DeliveryAddressDto(string Street, string City, string PostalCode);

/// <summary>E-shop krok 4: odeslaný formulář pokladny. Adresu ověří doména (<see cref="DeliveryAddress"/>).</summary>
public sealed class CheckoutRequest
{
    public Guid CheckoutId { get; set; }

    public ShippingMethod ShippingMethod { get; set; }

    [MaxLength(DeliveryAddress.MaxLength)]
    public string? Street { get; set; }

    [MaxLength(DeliveryAddress.MaxLength)]
    public string? City { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }
}
