using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Checkout;

/// <summary>
/// E-shop krok 4: pokladna. Řeší tři klasické problémy:
/// <list type="number">
/// <item>dvojí odeslání – <c>CheckoutId</c> (idempotentní příkaz),</item>
/// <item>změna ceny mezi vložením do košíku a objednáním – pravidlo v <c>Cart.CheckOut</c>, žádné tiché zdražení,</item>
/// <item>košík a objednávka se nesmí rozejít – založení objednávky a vyprázdnění košíku v jednom SaveChanges.</item>
/// </list>
/// </summary>
/// <remarks>
/// Dál je to stejná objednávka jako dřív: OrderPlaced → outbox → sága → ReserveStockV1 se všemi položkami
/// (sklad zarezervuje všechno, nebo nic). Pokladna zboží neověřuje – rozhoduje sklad.
/// </remarks>
public sealed class CheckoutService(
    ICartRepository carts,
    ICatalogProductRepository catalog,
    IOrderRepository orders,
    IOrderingUnitOfWork unitOfWork,
    IEnumerable<IDiscountPolicy> discountPolicies,
    IShippingPriceCalculator shipping,
    TimeProvider timeProvider) : ICheckoutService
{
    private static readonly ShippingMethod[] ShippingMethods = [ShippingMethod.Pickup, ShippingMethod.Courier];

    public async Task<CheckoutDto> GetAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        var products = cart is null
            ? new Dictionary<int, CatalogProduct>()
            : await catalog.GetForCartAsync(cart, cancellationToken);
        var cartDto = cart?.ToDto(products) ?? CartDto.Empty;

        // Slevy stejnými pravidly jako objednávka – jen pro řádky, které jdou objednat.
        var discount = cartDto.Lines
            .Where(line => line.IsOrderable)
            .Sum(line => discountPolicies.BestDiscount(products[line.ProductId], line.Quantity));
        var itemsTotal = cartDto.Total - discount;

        var lastAddress = await orders.GetLastDeliveryAddressAsync(customerId, cancellationToken);

        return new CheckoutDto(
            cartDto,
            cart?.CheckoutId ?? Guid.Empty,
            discount,
            itemsTotal,
            ShippingMethods.Select(method => new ShippingOptionDto(method, shipping.Calculate(method, itemsTotal))).ToList(),
            lastAddress is null ? null : new DeliveryAddressDto(lastAddress.Street, lastAddress.City, lastAddress.PostalCode));
    }

    public async Task AcceptCurrentPricesAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        if (cart is null)
        {
            return;
        }

        cart.AcceptCurrentPrices(await catalog.GetForCartAsync(cart, cancellationToken), UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderDto> PlaceOrderAsync(CheckoutRequest request, OrderCustomer customer,
        CancellationToken cancellationToken = default)
    {
        // 1. Idempotence: tohle odeslání už objednávku založilo (dvojklik, F5 po odeslání) → vrátit ji.
        if (await orders.GetByCheckoutIdAsync(customer.CustomerId, request.CheckoutId, cancellationToken) is { } placed)
        {
            return placed.ToDto();
        }

        var cart = await carts.GetByCustomerIdAsync(customer.CustomerId, cancellationToken)
            ?? throw new DomainException("Košík je prázdný.");

        // Adresu ověří hodnotový objekt (PSČ, povinné údaje) – ještě než se na košíku cokoli změní.
        var address = request.ShippingMethod == ShippingMethod.Courier
            ? new DeliveryAddress(request.Street ?? string.Empty, request.City ?? string.Empty, request.PostalCode ?? string.Empty)
            : null;

        // 2. Pravidla pokladny hlídá košík: stejná verze, nic staženého/vyprodaného, žádná nepotvrzená změna ceny.
        var lines = cart.CheckOut(request.CheckoutId, await catalog.GetForCartAsync(cart, cancellationToken), UtcNow);

        var order = new Order(customer.CustomerId, customer.Name, customer.Email, UtcNow, request.CheckoutId);
        foreach (var (product, quantity) in lines)
        {
            order.AddItem(product, quantity, discountPolicies.BestDiscount(product, quantity));
        }

        order.SetShipping(request.ShippingMethod, shipping.Calculate(request.ShippingMethod, order.ItemsTotal), address);
        orders.Add(order);

        // 3. Jedna transakce: nová objednávka + vyprázdněný košík + OrderPlaced v outboxu – všechno, nebo nic.
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Souběžné odeslání (dvojklik ve stejný okamžik): první požadavek košík mezitím odbavil, tenhle narazil
            // na změněnou verzi košíku (concurrency token) a jeho transakce se celá zrušila. Vrátíme objednávku prvního.
            var first = await orders.GetByCheckoutIdAsync(customer.CustomerId, request.CheckoutId, cancellationToken);
            return first?.ToDto()
                ?? throw new DomainException("Košík se mezitím změnil. Zkontrolujte ho a objednejte znovu.");
        }

        TraceTags.TagOrder(order.Id);
        return order.ToDto();
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
