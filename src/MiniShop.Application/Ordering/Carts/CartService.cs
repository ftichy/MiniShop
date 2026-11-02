using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 3: košík. Pravidla (maximum řádků, množství, vyprodané zboží) hlídá agregát <see cref="Cart"/>,
/// služba jen načte, zavolá metodu a uloží.
/// </summary>
/// <remarks>
/// Košík nepotřebuje žádnou novou zprávu: produkty zná z téže kopie katalogu jako objednávka a zboží neblokuje
/// (nic se neposílá na sklad). Košík vzniká až prvním přidáním.
/// </remarks>
public sealed class CartService(
    ICartRepository carts,
    ICatalogProductRepository catalog,
    IOrderingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICartService
{
    public async Task<CartDto> GetAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            return CartDto.Empty;
        }

        return cart.ToDto(await catalog.GetForCartAsync(cart, cancellationToken));
    }

    public Task<int> GetTotalQuantityAsync(string customerId, CancellationToken cancellationToken = default) =>
        carts.GetTotalQuantityAsync(customerId, cancellationToken);

    public async Task<string> AddItemAsync(string customerId, int productId, int quantity,
        CancellationToken cancellationToken = default)
    {
        var product = await catalog.GetByIdAsync(productId, cancellationToken)
            ?? throw new DomainException("Produkt není v nabídce.");

        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart(customerId, UtcNow);
            carts.Add(cart);
        }

        cart.AddItem(product, quantity, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.Name;
    }

    public async Task ChangeQuantityAsync(string customerId, int productId, int quantity,
        CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken)
            ?? throw new DomainException("Produkt v košíku není.");

        cart.ChangeQuantity(productId, quantity, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveItemAsync(string customerId, int productId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByCustomerIdAsync(customerId, cancellationToken);
        if (cart is null)
        {
            return;
        }

        cart.RemoveItem(productId, UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
