using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 2: celý Web (Program.cs) v paměti – skutečné HTTP požadavky, skutečné přihlášení cookie,
/// skutečné policies. Databáze jsou SQLite v paměti, workery (outbox, RabbitMQ, sága) neběží a sklad
/// nahrazuje <see cref="StubProductService"/>.
/// </summary>
/// <remarks>
/// Typový parametr jen určuje assembly Webu – <c>Program</c> nejde, testy vidí i <c>Program</c> z Inventory.Api.
/// Sdílí se přes <see cref="WebCollection"/> – Program nastavuje statický Serilog logger, dva Weby najednou
/// by si ho přepisovaly.
/// </remarks>
public sealed partial class MiniShopWebFactory : WebApplicationFactory<MiniShop.Web.Inventory.InventoryApiClient>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "admin-heslo-pro-test";
    public const string UserEmail = "jan@test.local";
    public const string UserPassword = "jan-heslo-pro-test";

    /// <summary>Účty krok 5: všechny e-maily, které Web „odeslal“.</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Účty krok 8: všechno, co Web zapsal do logu.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    private readonly InMemoryDatabase _ordering = new();
    private readonly InMemoryDatabase _journeys = new();
    private readonly InMemoryDatabase _accounts = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ordering", _ordering.ConnectionString);
        builder.UseSetting("ConnectionStrings:Journeys", _journeys.ConnectionString);
        builder.UseSetting("ConnectionStrings:Accounts", _accounts.ConnectionString);
        // Účty krok 7: servisní klíč skladu (sklad tu nahrazuje StubProductService, ale bez klíče by se Web nespustil).
        builder.UseSetting("InventoryApi:ApiKey", new string('k', 64));
        // Účty krok 8: limit formulářů účtu vysoko – testy se přihlašují stokrát z jedné „IP“. Limit zkouší vlastní test.
        builder.UseSetting("RateLimiting:AccountForms:PermitLimit", "100000");

        // První admin z konfigurace (seed) – stejně jako ve vývoji, jen bez vynucené změny hesla.
        builder.UseSetting("AdminBootstrap:Email", AdminEmail);
        builder.UseSetting("AdminBootstrap:Password", AdminPassword);
        builder.UseSetting("AdminBootstrap:MustChangePassword", "false");

        builder.ConfigureTestServices(services =>
        {
            // Workery MiniShopu pryč (outbox, consumery RabbitMQ, timeout ságy, obnova katalogu).
            // Server samotný je také IHostedService – ten musí zůstat.
            var workers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType?.Namespace?.StartsWith("MiniShop", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }

            services.RemoveAll<IProductService>();
            services.AddSingleton<IProductService, StubProductService>();
            services.RemoveAll<ICategoryService>();
            services.AddSingleton<ICategoryService, StubCategoryService>();
            services.RemoveAll<IProductImageService>();
            services.AddSingleton<IProductImageService, StubProductImageService>();

            // Účty krok 5: e-maily do „schránky“ testů – odkazy z nich testy otevírají.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            // Účty krok 8: log do paměti testu.
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    public async Task InitializeAsync()
    {
        // Spustí Web (seed vytvoří roli a admina) a založí zákazníka.
        await CreateUserAsync(UserEmail, UserPassword);
    }

    /// <summary>
    /// Zákazník (role User), volitelně s příznakem „musí změnit heslo“. Účty krok 9: e-mail je standardně potvrzený
    /// (bez potvrzení nejde objednávat).
    /// </summary>
    public async Task CreateUserAsync(string email, string password, bool mustChangePassword = false, bool emailConfirmed = true)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test", EmailConfirmed = emailConfirmed };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        await users.AddToRoleAsync(user, AccountRoles.User);
        if (mustChangePassword)
        {
            await users.AddClaimAsync(user, new(AccountClaimTypes.MustChangePassword, "true"));
        }
    }

    /// <summary>
    /// Účty krok 3: objednávka zákazníka rovnou v ordering.db (bez katalogu a ságy – testy Webu je nepotřebují).
    /// </summary>
    /// <param name="cancelled">Účty krok 9: rovnou potvrzená a zrušená (vyřízená – nebrání smazání účtu).</param>
    public async Task<int> CreateOrderForAsync(string email, bool cancelled = false)
    {
        await using var scope = Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Uživatel {email} neexistuje.");
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var order = new Order(user.Id, user.DisplayName, email, DateTime.UtcNow);
        if (cancelled)
        {
            order.Confirm(DateTime.UtcNow);
            order.Cancel(DateTime.UtcNow);
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>Klient jako prohlížeč: HTTPS (kvůli Secure cookie), cookies, bez automatického přesměrování.</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Přihlášení přes skutečný formulář /Account/Login.</summary>
    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = CreateBrowser();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, "/Account/Login"),
            ["Input.Email"] = email,
            ["Input.Password"] = password
        }));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    /// <summary>Antiforgery token z formuláře na stránce (patří k identitě klienta, proto se čte jeho vlastním klientem).</summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pagePath)
    {
        var html = await client.GetStringAsync(pagePath);
        var match = AntiforgeryTokenPattern().Match(html);
        Assert.True(match.Success, $"Na stránce {pagePath} není antiforgery token.");
        return match.Groups[1].Value;
    }

    // WebApplicationFactory má vlastní DisposeAsync (ValueTask) – xUnit volá tuhle, explicitní.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        _ordering.Dispose();
        _journeys.Dispose();
        _accounts.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}

[CollectionDefinition(Name)]
public sealed class WebCollection : ICollectionFixture<MiniShopWebFactory>
{
    public const string Name = "Web v paměti";
}

/// <summary>Sklad v testech Webu – Inventory.Api neběží, stránka Produkty dostane jeden produkt.</summary>
public sealed class StubProductService : IProductService
{
    private static readonly ProductDto Product =
        new(1, "Testovací produkt", null, 100m, 10, 1, "Nezařazeno", StubProductImageService.KnownFileName);

    public Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductDto>>([Product]);

    public Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Product);

    public Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Product);

    public Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Product);

    public Task<ProductDto> AddStockAsync(int id, int quantity, CancellationToken cancellationToken = default) =>
        Task.FromResult(Product);

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>E-shop krok 2: kategorie v testech Webu – Inventory.Api neběží, stránka Produkty dostane jednu kategorii.</summary>
public sealed class StubCategoryService : ICategoryService
{
    private static readonly CategoryDto Category = new(1, "Nezařazeno", 1);

    /// <summary>Názvy kategorií, které stránka poslala „skladu“ (založení i přejmenování) – pro testy formulářů.</summary>
    public List<string> ReceivedNames { get; } = [];

    public Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CategoryDto>>([Category]);

    public Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default)
    {
        ReceivedNames.Add(request.Name);
        return Task.FromResult(Category);
    }

    public Task<CategoryDto> RenameAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default)
    {
        ReceivedNames.Add(request.Name);
        return Task.FromResult(Category);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// E-shop krok 2B: obrázky v testech Webu – „sklad“ zná jeden obrázek (<see cref="KnownFileName"/>)
/// a pamatuje si, co mu stránka poslala.
/// </summary>
public sealed class StubProductImageService : IProductImageService
{
    public const string KnownFileName = "1-0123456789ab.png";
    public static readonly byte[] KnownContent = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static readonly ProductDto Product = new(1, "Testovací produkt", null, 100m, 10, 1, "Nezařazeno", KnownFileName);

    public List<byte[]> Uploaded { get; } = [];

    public Task<ProductDto> SetImageAsync(int productId, byte[] content, CancellationToken cancellationToken = default)
    {
        Uploaded.Add(content);
        return Task.FromResult(Product);
    }

    public Task<ProductDto> RemoveImageAsync(int productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Product);

    public Task<ProductImage?> GetImageAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(fileName == KnownFileName ? new ProductImage(KnownContent, "image/png") : null);
}
