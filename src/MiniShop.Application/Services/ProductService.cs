using MiniShop.Application.Abstractions;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Mapping;
using MiniShop.Domain.Products;

namespace MiniShop.Application.Services;

public sealed class ProductService(IProductRepository products, IUnitOfWork unitOfWork) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await products.GetAllAsync(cancellationToken);
        return all.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        return product.ToDto();
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = new Product(request.Name, request.Description, request.Price, request.StockQuantity);
        products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.ToDto();
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.Update(request.Name, request.Description, request.Price);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.ToDto();
    }

    public async Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        product.AddStock(quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(id, cancellationToken);
        products.Remove(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Product> GetProductAsync(int id, CancellationToken cancellationToken) =>
        await products.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Produkt s ID {id} nebyl nalezen.");
}
