using System.Text.Json;
using MiniShop.Application.Inventory;

namespace MiniShop.Inventory.Api;

/// <summary>
/// E-shop (issue #115): jednorázový příkaz, který naplní sklad produkty z JSON souboru a skončí (služba se nespustí):
/// <code>dotnet MiniShop.Inventory.Api.dll import-products [--file SeedData/products.json]</code>
/// V cloudu přímo v běžícím kontejneru (<c>docker compose exec inventory-api dotnet MiniShop.Inventory.Api.dll import-products</c>).
/// Obrázky se berou ze složky <c>images</c> vedle souboru. Zprávy pro výlohu (ProductUpdatedV1/V2) zapíše do outboxu,
/// odešle je běžící služba skladu – příkaz sám nic neposílá. Opakované spuštění nic nezdvojí.
/// </summary>
public static class ImportProductsCommand
{
    public const string Name = "import-products";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var file = GetOption(args, "--file") ?? Path.Combine("SeedData", "products.json");
        if (!Path.IsPathRooted(file))
        {
            file = Path.Combine(AppContext.BaseDirectory, file);
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Soubor {file} neexistuje.");
            return 1;
        }

        ProductImportItem[] items;
        try
        {
            var document = JsonSerializer.Deserialize<ImportFile>(await File.ReadAllTextAsync(file), Json);
            items = document?.Products ?? [];
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Soubor {file} není platný JSON: {ex.Message}");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<ProductImportService>()
                .ImportAsync(items, Path.Combine(Path.GetDirectoryName(file)!, "images"));

            Console.WriteLine($"Hotovo: nových produktů {result.Created}, přeskočeno (už existují) {result.Skipped}, " +
                              $"nových kategorií {result.CategoriesCreated}, nahraných obrázků {result.ImagesSet}.");
            return 0;
        }
        catch (MiniShop.Domain.Exceptions.DomainException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record ImportFile(ProductImportItem[] Products);
}
