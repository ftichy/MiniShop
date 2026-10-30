using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Web.Inventory;

/// <summary>
/// Krok 10B: sklad je samostatná služba – stránky Webu s ním mluví přes jeho HTTP API (/api/products).
/// Implementuje stejné rozhraní jako dřív <see cref="ProductService"/>, takže stránky se skoro nezměnily.
/// </summary>
/// <remarks>
/// Adresu nezná natvrdo: BaseAddress je "https+http://inventory-api" a skutečnou URL doplní service discovery
/// (z AppHostu Aspire, bez něj ze sekce "Services" v appsettings.json).
/// Chyby API převádí zpět na výjimky, které stránky už znají: 404 → <see cref="NotFoundException"/>,
/// 400 → <see cref="DomainException"/>. Nedostupná služba → <see cref="InventoryUnavailableException"/>.
/// Synchronní HTTP je tu v pořádku – jde o administraci a zobrazení. Business tok (rezervace) jde dál přes zprávy.
/// </remarks>
public sealed class InventoryApiClient(HttpClient http) : IProductService, ICategoryService, IProductImageService
{
    private const string Products = "api/products";
    private const string Categories = "api/categories";

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<ProductDto>>(new HttpRequestMessage(HttpMethod.Get, Products), cancellationToken);

    public Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Get, $"{Products}/{id}"), cancellationToken);

    public Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Post, Products) { Content = JsonContent.Create(request) },
            cancellationToken);

    public Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Put, $"{Products}/{id}") { Content = JsonContent.Create(request) },
            cancellationToken);

    public Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default) =>
        SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Post, $"{Products}/{id}/stock")
        {
            Content = JsonContent.Create(new AddStockRequest { Quantity = quantity })
        }, cancellationToken);

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Products}/{id}"), cancellationToken);
    }

    // ---------- E-shop krok 2B: obrázky (IProductImageService) ----------

    public Task<ProductDto> SetImageAsync(int productId, byte[] content, CancellationToken cancellationToken = default)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", "image" } };
        return SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Put, $"{Products}/{productId}/image") { Content = form },
            cancellationToken);
    }

    public Task<ProductDto> RemoveImageAsync(int productId, CancellationToken cancellationToken = default) =>
        SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Delete, $"{Products}/{productId}/image"), cancellationToken);

    /// <returns>Obrázek, nebo null, když ho sklad nemá (404). Neplatné jméno se na sklad vůbec neposílá.</returns>
    public async Task<ProductImage?> GetImageAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!ProductImageFormat.IsValidFileName(fileName))
        {
            return null;
        }

        try
        {
            using var response = await SendRawAsync(
                new HttpRequestMessage(HttpMethod.Get, $"{Products}/images/{fileName}"), cancellationToken);
            return new ProductImage(await response.Content.ReadAsByteArrayAsync(cancellationToken),
                ProductImageFormat.ContentType(fileName));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    // ---------- E-shop krok 2: kategorie (ICategoryService) ----------

    async Task<IReadOnlyList<CategoryDto>> ICategoryService.GetAllAsync(CancellationToken cancellationToken) =>
        await SendAsync<List<CategoryDto>>(new HttpRequestMessage(HttpMethod.Get, Categories), cancellationToken);

    public Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CategoryDto>(new HttpRequestMessage(HttpMethod.Post, Categories) { Content = JsonContent.Create(request) },
            cancellationToken);

    public Task<CategoryDto> RenameAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CategoryDto>(new HttpRequestMessage(HttpMethod.Put, $"{Categories}/{id}") { Content = JsonContent.Create(request) },
            cancellationToken);

    async Task ICategoryService.DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Categories}/{id}"), cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InventoryUnavailableException("Služba skladu vrátila prázdnou odpověď.");
    }

    /// <summary>Pošle požadavek a vrátí jen úspěšnou odpověď; vše ostatní převede na výjimku.</summary>
    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await http.SendAsync(request, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Spojení odmítnuto, timeout, otevřený circuit breaker… – pro stránku je to vždy "sklad teď neodpovídá".
            throw new InventoryUnavailableException("Služba skladu je nedostupná. Zkuste to za chvíli.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var message = await ReadProblemAsync(response, cancellationToken);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new NotFoundException(message),
                HttpStatusCode.BadRequest => new DomainException(message),
                _ => new InventoryUnavailableException($"Služba skladu vrátila chybu {(int)response.StatusCode}.")
            };
        }
    }

    /// <summary>Text chyby z ProblemDetails (RFC 9457) – "detail", u validace seznam chyb.</summary>
    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            if (problem?.Errors.Count > 0)
            {
                return string.Join(" ", problem.Errors.SelectMany(e => e.Value));
            }

            return problem?.Title ?? response.ReasonPhrase ?? "Chyba služby skladu.";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return response.ReasonPhrase ?? "Chyba služby skladu.";
        }
    }
}
