using System.Text.Json;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Tests.Application;

/// <summary>Issue #115: import produktů do skladu – idempotence, kategorie, obrázky, kontrola celého souboru před zápisem.</summary>
public sealed class ProductImportServiceTests : IDisposable
{
    // Nejmenší platný PNG (1×1 px) – formát se pozná podle prvních bajtů.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _directory = Directory.CreateTempSubdirectory("import-").FullName;
    private readonly Fake _fake = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ProductImportService Service => new(_fake, _fake, _fake);

    private static ProductImportItem Item(string name = "Test", decimal price = 100, string? image = "a.png", string category = "Notebooky") =>
        new(name, "Popis", price, 5, category, image);

    [Fact]
    public async Task Import_ZalozíKategorieProduktyAObrazky_ADruheSpusteniNicNezdvoji()
    {
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), Png);
        var items = new[] { Item("A"), Item("B"), Item("C", category: "Monitory") };

        var first = await Service.ImportAsync(items, _directory);
        var second = await Service.ImportAsync(items, _directory);

        Assert.Equal(new ProductImportResult(3, 0, 2, 3), first);
        Assert.Equal(new ProductImportResult(0, 3, 0, 0), second);
        Assert.Equal(3, _fake.Products.Count);
    }

    [Fact]
    public async Task Import_ExistujiciProduktBezObrazku_DostaneObrazek()
    {
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), Png);
        await Service.ImportAsync([Item("A", image: null)], _directory);

        var result = await Service.ImportAsync([Item("a")], _directory);   // jiná velikost písmen = stejný produkt

        Assert.Equal(new ProductImportResult(0, 1, 0, 1), result);
    }

    [Fact]
    public async Task Import_ChybaVSouboru_NezapiseNic()
    {
        File.WriteAllBytes(Path.Combine(_directory, "a.png"), Png);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Service.ImportAsync([Item("A"), Item("B", price: 0), Item("C", image: "chybi.png"), Item("A")], _directory));

        Assert.Contains("B:", ex.Message);
        Assert.Contains("chybi.png", ex.Message);
        Assert.Contains("dvakrát", ex.Message);
        Assert.Empty(_fake.Products);
    }

    [Fact]
    public async Task Import_ObrazekNeniObrazek_JeChyba()
    {
        File.WriteAllText(Path.Combine(_directory, "a.png"), "není obrázek");

        await Assert.ThrowsAsync<DomainException>(() => Service.ImportAsync([Item("A")], _directory));
    }

    [Fact]
    public async Task Import_ObrazekSCestou_JeChyba()
    {
        await Assert.ThrowsAsync<DomainException>(() => Service.ImportAsync([Item("A", image: "../a.png")], _directory));
    }

    [Fact]
    public void DatovySoubor_JeVporadku_AObrazkyExistuji()
    {
        // Skutečná data pro příkaz import-products: všechna pravidla + obrázky (sklad stačí ve falešné podobě).
        var directory = Path.Combine(AppContext.BaseDirectory, "SeedData");
        var file = JsonSerializer.Deserialize<Dictionary<string, ProductImportItem[]>>(
            File.ReadAllText(Path.Combine(directory, "products.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var items = file["products"];

        var result = Service.ImportAsync(items, Path.Combine(directory, "images")).GetAwaiter().GetResult();

        Assert.InRange(items.Length, 25, 40);
        Assert.Equal(items.Length, result.Created);
        Assert.Equal(items.Length, result.ImagesSet);
    }

    private sealed class Fake : IProductService, ICategoryService, IProductImageService
    {
        public List<ProductDto> Products { get; } = [];
        private readonly List<CategoryDto> _categories = [];

        public Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductDto>>(Products.ToList());

        public Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
        {
            var category = _categories.Single(c => c.Id == request.CategoryId);
            var product = new ProductDto(Products.Count + 1, request.Name, request.Description, request.Price,
                request.StockQuantity, category.Id, category.Name);
            Products.Add(product);
            return Task.FromResult(product);
        }

        Task<IReadOnlyList<CategoryDto>> ICategoryService.GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoryDto>>(_categories.ToList());

        public Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default)
        {
            var category = new CategoryDto(_categories.Count + 1, request.Name, 0);
            _categories.Add(category);
            return Task.FromResult(category);
        }

        public Task<ProductDto> SetImageAsync(int productId, byte[] content, CancellationToken cancellationToken = default)
        {
            var index = Products.FindIndex(p => p.Id == productId);
            Products[index] = Products[index] with { ImageFileName = $"{productId}.png" };
            return Task.FromResult(Products[index]);
        }

        public Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CategoryDto> RenameAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        Task ICategoryService.DeleteAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductDto> RemoveImageAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductImage?> GetImageAsync(string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
