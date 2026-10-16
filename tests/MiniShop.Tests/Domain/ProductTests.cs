using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Products;

namespace MiniShop.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void RemoveStock_SniziZasobu()
    {
        var product = new Product("Myš", null, 500m, 10);

        product.RemoveStock(3);

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_VicNezJeSkladem_VyhodiVyjimku()
    {
        var product = new Product("Myš", null, 500m, 2);

        var ex = Assert.Throws<InsufficientStockException>(() => product.RemoveStock(3));

        Assert.Equal(2, ex.Available);
        Assert.Equal(2, product.StockQuantity);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Myš", 0)]
    [InlineData("Myš", -5)]
    public void Konstruktor_NeplatnaData_VyhodiVyjimku(string name, int price)
    {
        Assert.Throws<DomainException>(() => new Product(name, null, price, 1));
    }
}
