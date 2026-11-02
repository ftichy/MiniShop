namespace MiniShop.Application.Ordering.Checkout;

/// <summary>
/// E-shop krok 4: pokladna – jediné místo, kde se nezávazný košík mění v závazek (objednávku).
/// </summary>
public interface ICheckoutService
{
    /// <summary>Souhrn pro stránku pokladny (prázdný košík = <see cref="CheckoutDto.Cart"/> prázdný).</summary>
    Task<CheckoutDto> GetAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>Zákazník viděl změněné ceny a souhlasí s nimi.</summary>
    Task AcceptCurrentPricesAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Založí objednávku z košíku a košík vyprázdní – v jedné transakci. Stejné <see cref="CheckoutRequest.CheckoutId"/>
    /// podruhé (dvojklik, F5, souběh) vrátí už založenou objednávku.
    /// </summary>
    Task<OrderDto> PlaceOrderAsync(CheckoutRequest request, OrderCustomer customer, CancellationToken cancellationToken = default);
}
