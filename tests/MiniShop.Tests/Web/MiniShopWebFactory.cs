using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
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
using MiniShop.Application.Notifications;
using MiniShop.Tests.TestDoubles;
using MiniShop.Web.Accounts;
using MiniShop.Tests.NotificationsApi;
using MiniShop.Web.Messages;
using MiniShop.Web.Notifications;
using MiniShop.Web.Reporting;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 2: celý Web (Program.cs) v paměti – skutečné HTTP požadavky, skutečné přihlášení cookie,
/// skutečné policies. Databáze je PostgreSQL z <see cref="TestDatabases"/> (Testcontainers), workery (outbox, RabbitMQ, sága) neběží a sklad
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

    /// <summary>Mikroservisy M2A: servisní klíč interního API Webu (testy jím volají /internal/… jako budoucí služba Notifications).</summary>
    public static readonly string InternalApiKey = new('i', 64);
    public const string UserPassword = "jan-heslo-pro-test";
    public const string WebhookSecret = "testovaci-tajemstvi-webhooku-plateb-pro-web-0123456789";

    /// <summary>Účty krok 5: všechny e-maily, které Web „odeslal“.</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Účty krok 8: všechno, co Web zapsal do logu.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>Účty krok 10: falešný Google (<see cref="FakeGoogleHandler"/>).</summary>
    public FakeGoogle Google { get; } = new();

    /// <summary>E-shop krok 23: oznámení push, která Web „odeslal“ (push služba bez internetu).</summary>
    public RecordingPushSender Push => Notifications.Push;

    /// <summary>Mikroservisy M2C: služba Notifications v paměti (zařízení push, oznámení k práci, „Dejte mi vědět“).</summary>
    public NotificationsApiFactory Notifications { get; } = new();

    /// <summary>Mikroservisy M2C: false = služba Notifications „neběží“ (stránky musí výpadek ustát).</summary>
    public bool NotificationsAvailable { get; set; } = true;

    /// <summary>Mikroservisy M2C: zprávy služby Notifications na stránce /Admin/Messages.</summary>
    public FakeServiceMessages NotificationsMessages { get; } = new("notifications-api", "Notifications", "minishop.notifications");

    /// <summary>Mikroservisy M1: služba přehledu tržeb (Reporting.Api v testech Webu neběží).</summary>
    public FakeSalesReportApi SalesReports { get; } = new();

    /// <summary>Mikroservisy M2B: zprávy skladu na stránce /Admin/Messages (Inventory.Api v testech Webu neběží).</summary>
    public FakeServiceMessages InventoryMessages { get; } = new("inventory-api", "Inventory", "minishop.inventory");

    /// <summary>Mikroservisy M2B: zprávy služby přehledu tržeb.</summary>
    public FakeServiceMessages ReportingMessages { get; } = new("reporting-api", "Reporting", "minishop.reporting");

    /// <summary>Škálování krok 5: workery, které Web registruje (před odebráním pro testy) – hlídá je <c>WebWorkersTests</c>.</summary>
    public List<Type> RegisteredWorkers { get; private set; } = [];

    private readonly TestDatabases _databases = new();   // škálování krok 2 a 3C: PostgreSQL (Testcontainers)

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        _databases.Configure(builder);
        // E-shop krok 8: tajemství webhooku platební brány (bez něj by se Web nespustil). Testy jím i podepisují.
        builder.UseSetting("Payments:WebhookSecret", WebhookSecret);
        // Účty krok 7: servisní klíč skladu (sklad tu nahrazuje StubProductService, ale bez klíče by se Web nespustil).
        builder.UseSetting("InventoryApi:ApiKey", new string('k', 64));
        builder.UseSetting("ReportingApi:ApiKey", new string('r', 64));   // mikroservisy M1 (služba tu je FakeSalesReportApi)
        builder.UseSetting("WebInternalApi:ApiKey", InternalApiKey);   // mikroservisy M2A: interní API /internal/…
        builder.UseSetting("NotificationsApi:ApiKey", NotificationsApiFactory.ApiKey);   // mikroservisy M2C
        builder.UseSetting("Carrier:ApiKey", MiniShop.Tests.CarrierSimulator.CarrierSimulatorFactory.ApiKey);   // e-shop krok 26B (dopravce se tu nevolá – outbox neběží)
        builder.UseSetting("Carrier:WebhookSecret", MiniShop.Tests.CarrierSimulator.CarrierSimulatorFactory.WebhookSecret);   // e-shop krok 26C
        builder.UseSetting("PushNotifications:VapidPublicKey", "test-vapid-public-key");   // Web zná jen veřejný klíč
        // Účty krok 8: limit formulářů účtu vysoko – testy se přihlašují stokrát z jedné „IP“. Limit zkouší vlastní test.
        builder.UseSetting("RateLimiting:AccountForms:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:CatalogSuggest:PermitLimit", "100000");

        // První admin z konfigurace (seed) – stejně jako ve vývoji, jen bez vynucené změny hesla.
        builder.UseSetting("AdminBootstrap:Email", AdminEmail);
        builder.UseSetting("AdminBootstrap:Password", AdminPassword);
        builder.UseSetting("AdminBootstrap:MustChangePassword", "false");

        builder.ConfigureTestServices(services =>
        {
            // Workery MiniShopu pryč (outbox, consumery RabbitMQ, timeout ságy, obnova katalogu).
            // Server samotný je také IHostedService – ten musí zůstat.
            RegisteredWorkers = HostedServices.RemoveMiniShopWorkers(services);

            services.RemoveAll<IProductService>();
            services.AddSingleton<IProductService, StubProductService>();
            services.RemoveAll<ICategoryService>();
            services.AddSingleton<ICategoryService, StubCategoryService>();
            services.RemoveAll<IProductImageService>();
            services.AddSingleton<IProductImageService, StubProductImageService>();
            services.RemoveAll<ISalesReportApi>();
            services.AddSingleton<ISalesReportApi>(SalesReports);
            services.RemoveAll<IServiceMessages>();   // mikroservisy M2B: Web v procesu, ostatní služby podvržené
            services.AddScoped<IServiceMessages, LocalServiceMessages>();
            services.AddSingleton<IServiceMessages>(InventoryMessages);
            services.AddSingleton<IServiceMessages>(ReportingMessages);
            services.AddSingleton<IServiceMessages>(NotificationsMessages);

            // Účty krok 5: e-maily do „schránky“ testů – odkazy z nich testy otevírají.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            // Mikroservisy M2C: Notifications je samostatná služba – tady skutečná služba v paměti, Web ji volá přes HTTP
            // se servisním klíčem (stránka → API → služba jako v produkci). Oznámení push z ní jdou do „schránky“ Push.
            services.RemoveAll<INotificationsApi>();
            services.AddScoped<INotificationsApi>(_ => new NotificationsApiClient(NotificationsAvailable
                ? Notifications.CreateAuthorizedClient()
                : new HttpClient(new UnavailableHandler()) { BaseAddress = new Uri("http://notifications-api/") }));

            // Účty krok 8: log do paměti testu.
            services.AddSingleton<ILoggerProvider>(Logs);

            // Účty krok 10: „Google“ bez internetu – testy projdou přihlášením, propojením i smazáním přes stránky.
            services.AddSingleton(Google);
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, FakeGoogleHandler>(GoogleLogin.Scheme, GoogleLogin.DisplayName, _ => { });
        });
    }

    public async Task InitializeAsync()
    {
        // Spustí Web (seed vytvoří roli a admina) a založí zákazníka.
        await CreateUserAsync(UserEmail, UserPassword);

        // 2FA krok A2: admin bez dvoufázového ověření nesmí do administrace – testovací admin ho má zapnuté
        // (stejně jako v produkci) a LoginAsync mu kód dopočítá. Test „bez 2FA“ si založí vlastního admina.
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;
        await users.ResetAuthenticatorKeyAsync(admin);
        await users.SetTwoFactorEnabledAsync(admin, true);
    }

    /// <summary>Admin (role Admin), volitelně bez dvoufázového ověření – takový se smí jen přihlásit a nastavit si ho.</summary>
    public async Task CreateAdminAsync(string email, string password, bool twoFactor)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test admin", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        await users.AddToRolesAsync(user, AccountRoles.Staff);   // účty krok 12A: dřív role Admin
        if (twoFactor)
        {
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
        }
    }

    /// <summary>Účty krok 12A: účet s vybranými rolemi (třeba Sklad + Zákazník), potvrzený e-mail a zapnuté 2FA.</summary>
    public async Task CreateWithRolesAsync(string email, string password, params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test zaměstnanec", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
    }

    /// <summary>Aktuální tajný klíč účtu (jako ho vidí aplikace v telefonu).</summary>
    public async Task<string> GetAuthenticatorKeyAsync(string email)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!))!;
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

    /// <summary>E-shop krok 6: potvrzená objednávka s dopravou (bez ságy a skladu) – připravená pro expedici.</summary>
    public async Task<int> CreateConfirmedOrderForAsync(string email, ShippingMethod shipping)
    {
        await using var scope = Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Uživatel {email} neexistuje.");
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var order = new Order(user.Id, user.DisplayName, email, DateTime.UtcNow);
        order.SetShipping(shipping, 99m, shipping == ShippingMethod.Courier ? new DeliveryAddress("Dlouhá 5", "Praha", "11000") : null);
        order.Confirm(DateTime.UtcNow);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>E-shop krok 3: produkt rovnou v kopii katalogu (bez skladu a zpráv), skladem. Podruhé se nic nestane.</summary>
    public async Task EnsureCatalogProductAsync(int id, string name, decimal price, string? imageFileName = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        if (await db.CatalogProducts.FindAsync(id) is not null)
        {
            return;
        }

        var product = new CatalogProduct(id, name, price, DateTime.UtcNow);
        if (imageFileName is not null)
        {
            product.ApplyDetails(name, null, price, 1, "Nezařazeno", imageFileName, DateTime.UtcNow);
        }

        product.ApplyAvailability(CatalogAvailability.InStock, DateTime.UtcNow);
        db.CatalogProducts.Add(product);
        await db.SaveChangesAsync();
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

        // Účet s dvoufázovým ověřením: druhý krok s kódem, který by počítala aplikace v telefonu.
        if (response.Headers.Location!.OriginalString.Contains("/Account/LoginWith2fa", StringComparison.Ordinal))
        {
            var code = TestTotp.Compute(await GetAuthenticatorKeyAsync(email));
            var second = await client.PostAsync("/Account/LoginWith2fa", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, "/Account/LoginWith2fa"),
                ["Input.Code"] = code
            }));
            Assert.Equal(System.Net.HttpStatusCode.Redirect, second.StatusCode);
        }

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
        await Notifications.DisposeAsync();
        _databases.Dispose();
    }

    /// <summary>Spadlá služba – každý požadavek skončí chybou spojení.</summary>
    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("notifications-api neběží");
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
