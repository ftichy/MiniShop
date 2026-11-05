using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Application.Ordering.Pricing;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;
using MiniShop.Domain.Orders.Coupons;

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
    ICouponRepository coupons,
    TimeProvider timeProvider) : ICheckoutService
{
    private static readonly ShippingMethod[] ShippingMethods = [ShippingMethod.Pickup, ShippingMethod.Courier];

    public async Task<CheckoutDto> GetAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        var (cartDto, discount, afterDiscounts) = await PriceAsync(cart, cancellationToken);

        // E-shop krok 13: uplatněný kód – sleva, nebo proč už neplatí (mezitím se mohl vyčerpat či vypršet).
        decimal couponDiscount = 0;
        string? couponProblem = null;
        if (cart?.CouponCode is { } code)
        {
            var coupon = await coupons.GetByCodeAsync(code, cancellationToken);
            couponProblem = await WhyCouponNotApplicableAsync(coupon, customerId, afterDiscounts, cancellationToken);
            couponDiscount = couponProblem is null ? coupon!.DiscountFor(afterDiscounts) : 0;
        }

        var itemsTotal = afterDiscounts - couponDiscount;
        var lastAddress = await orders.GetLastDeliveryAddressAsync(customerId, cancellationToken);

        return new CheckoutDto(
            cartDto,
            cart?.CheckoutId ?? Guid.Empty,
            discount,
            itemsTotal,
            ShippingMethods.Select(method => new ShippingOptionDto(method, shipping.Calculate(method, itemsTotal))).ToList(),
            lastAddress is null ? null : new DeliveryAddressDto(lastAddress.Street, lastAddress.City, lastAddress.PostalCode),
            cart?.CouponCode,
            couponDiscount,
            couponProblem);
    }

    /// <summary>
    /// E-shop krok 13: zákazník uplatnil slevový kód. Ověří ho hned (ať zákazník vidí slevu nebo důvod), uloží do košíku;
    /// při objednání se ověří znovu – a teprve tam se počítá jeho použití.
    /// </summary>
    public async Task ApplyCouponAsync(string customerId, string code, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken)
            ?? throw new DomainException("Slevový kód jde uplatnit jen na košík se zbožím.");
        var normalized = Coupon.NormalizeCode(code);
        if (normalized.Length == 0)
        {
            throw new DomainException("Zadejte slevový kód.");
        }

        var (_, _, afterDiscounts) = await PriceAsync(cart, cancellationToken);
        var coupon = await coupons.GetByCodeAsync(normalized, cancellationToken);
        if (await WhyCouponNotApplicableAsync(coupon, customerId, afterDiscounts, cancellationToken) is { } problem)
        {
            throw new DomainException(problem);
        }

        cart.ApplyCoupon(normalized, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveCouponAsync(string customerId, CancellationToken cancellationToken = default)
    {
        if (await carts.GetByCustomerIdAsync(customerId, cancellationToken) is { } cart)
        {
            cart.RemoveCoupon(UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Košík s aktuálními cenami a slevami za množství (stejná pravidla jako objednávka, jen objednatelné řádky).</summary>
    private async Task<(CartDto Cart, decimal Discount, decimal ItemsTotal)> PriceAsync(Cart? cart,
        CancellationToken cancellationToken)
    {
        var products = cart is null
            ? new Dictionary<int, CatalogProduct>()
            : await catalog.GetForCartAsync(cart, cancellationToken);
        var cartDto = cart?.ToDto(products) ?? CartDto.Empty;
        var discount = cartDto.Lines
            .Where(line => line.IsOrderable)
            .Sum(line => discountPolicies.BestDiscount(products[line.ProductId], line.Quantity));
        return (cartDto, discount, cartDto.Total - discount);
    }

    /// <summary>Proč kupon nejde použít (neexistuje, neplatí, vyčerpaný, malý nákup, už použitý zákazníkem), nebo null.</summary>
    private async Task<string?> WhyCouponNotApplicableAsync(Coupon? coupon, string customerId, decimal itemsTotal,
        CancellationToken cancellationToken) =>
        coupon is null ? "Takový slevový kód neexistuje."
        : coupon.WhyNotApplicable(itemsTotal, UtcNow) is { } reason ? reason
        : await orders.HasUsedCouponAsync(customerId, coupon.Id, cancellationToken) ? "Tento slevový kód jste už použili."
        : null;

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
        var couponCode = cart.CouponCode;   // CheckOut košík vyprázdní i s kódem – zapamatovat si ho předem
        IReadOnlyList<(CatalogProduct Product, int Quantity)> lines;
        try
        {
            lines = cart.CheckOut(request.CheckoutId, await catalog.GetForCartAsync(cart, cancellationToken), UtcNow);
        }
        catch (DomainException)
        {
            // Škálování krok 3C: souběžné odeslání (dvojklik) – v PostgreSQL může tenhle požadavek načíst košík až PO
            // commitu prvního (verze košíku se změnila → CheckOut odmítne). Objednávku ale založil první se stejným
            // CheckoutId – vrátíme ji. Jinak jde o skutečnou změnu košíku a chyba platí.
            if (await orders.GetByCheckoutIdAsync(customer.CustomerId, request.CheckoutId, cancellationToken) is { } concurrent)
            {
                return concurrent.ToDto();
            }

            throw;
        }

        var order = new Order(customer.CustomerId, customer.Name, customer.Email, UtcNow, request.CheckoutId);
        foreach (var (product, quantity) in lines)
        {
            order.AddItem(product, quantity, discountPolicies.BestDiscount(product, quantity));
        }

        // E-shop krok 13: kupon se ověří znovu a jeho použití se započítá ve STEJNÉ transakci jako objednávka.
        // Souběh o poslední použití rozhodne verze kuponu (concurrency token) – viz catch níže.
        if (couponCode is not null)
        {
            var coupon = await coupons.GetByCodeAsync(couponCode, cancellationToken);
            if (await WhyCouponNotApplicableAsync(coupon, customer.CustomerId, order.ItemsTotal, cancellationToken) is { } problem)
            {
                throw new DomainException($"{problem} Odeberte ho v pokladně a objednejte znovu.");
            }

            order.ApplyCoupon(coupon!, UtcNow);   // sleva rozpočítaná do položek, kupon.RedemptionCount + 1
        }

        order.SetShipping(request.ShippingMethod, shipping.Calculate(request.ShippingMethod, order.ItemsTotal), address);
        order.SetPayment(request.PaymentMethod);   // e-shop krok 8: kartou → sága po rezervaci počká na platbu
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
                ?? throw new DomainException(couponCode is not null
                    // E-shop krok 13: jiná pokladna mezitím použila stejný kupon (třeba jeho poslední použití).
                    ? "Slevový kód mezitím použil někdo jiný. Zkontrolujte souhrn a objednejte znovu."
                    : "Košík se mezitím změnil. Zkontrolujte ho a objednejte znovu.");
        }

        TraceTags.TagOrder(order.Id);
        return order.ToDto();
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
