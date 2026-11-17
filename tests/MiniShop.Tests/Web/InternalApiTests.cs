using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Notifications;
using MiniShop.ServiceDefaults;

namespace MiniShop.Tests.Web;

/// <summary>
/// Mikroservisy M2A: interní API Webu (<c>/internal/…</c>) pro budoucí službu Notifications. Dvě věci:
/// <list type="bullet">
/// <item><b>zámek v obou směrech</b> – API jen se servisním klíčem (cookie ani admina neplatí), klíč neotevře stránky;</item>
/// <item><b>test kontraktu</b> – <see cref="WebInternalApiClient"/> (klient budoucí služby) proti skutečnému Webu v paměti
/// vrací totéž, co implementace v procesu, které Notifications používají dnes.</item>
/// </list>
/// </summary>
[Collection(WebCollection.Name)]
public sealed class InternalApiTests(MiniShopWebFactory web) : IAsyncLifetime
{
    private const string Password = "heslo-pro-interni-api-1";
    private readonly string _customerEmail = $"interni-zakaznik-{Guid.NewGuid():N}@test.local";
    private readonly string _warehouseEmail = $"interni-sklad-{Guid.NewGuid():N}@test.local";
    private string _customerId = string.Empty;
    private string _warehouseId = string.Empty;

    public async Task InitializeAsync()
    {
        await web.CreateUserAsync(_customerEmail, Password);
        await web.CreateWithRolesAsync(_warehouseEmail, Password, AccountRoles.Warehouse);
        _customerId = await UserIdAsync(_customerEmail);
        _warehouseId = await UserIdAsync(_warehouseEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Zámek ----------

    [Theory]
    [InlineData(null)]
    [InlineData("spatny-klic-spatny-klic-spatny-klic-spatny-klic-spatny-klic-spat")]
    public async Task BezKliceNeboSeSpatnym_401(string? key)
    {
        var client = web.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, key);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/internal/customers/{_customerId}/contact")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/orders/1/invoice")).StatusCode);
    }

    [Fact]
    public async Task CookieAdmina_NeotevreInterniApi()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync($"/internal/customers/{_customerId}/contact")).StatusCode);
    }

    [Fact]
    public async Task Klic_NeotevreStranky()
    {
        var client = web.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, MiniShopWebFactory.InternalApiKey);

        var response = await client.GetAsync("/Admin/Sales");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);   // na přihlášení – klíč stránky nezná
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    // ---------- Kontrakt: klient budoucí služby = implementace v procesu ----------

    [Fact]
    public async Task Kontakt_StejnyJakoVProcesu_NeznamyNull()
    {
        var overHttp = await Client().FindAsync(_customerId);
        var inProcess = await InProcess(sp => sp.GetRequiredService<ICustomerContacts>().FindAsync(_customerId));

        Assert.NotNull(overHttp);
        Assert.Equal(inProcess, overHttp);
        Assert.Equal(_customerEmail, overHttp.Email);
        Assert.Null(await Client().FindAsync("neexistujici-ucet"));
    }

    [Fact]
    public async Task OznameniKPraci_JenSRoliANezablokovany()
    {
        string[] candidates = [_warehouseId, _customerId, "neexistujici-ucet"];

        var overHttp = await Client().FilterAllowedAsync(StaffAlertKind.NewOrder, candidates);
        var inProcess = await InProcess(sp => sp.GetRequiredService<IStaffDirectory>().FilterAllowedAsync(StaffAlertKind.NewOrder, candidates));

        Assert.Equal([_warehouseId], overHttp);
        Assert.Equal(inProcess, overHttp);
        Assert.Empty(await Client().FilterAllowedAsync(StaffAlertKind.ReturnRequested, candidates));   // podpora ≠ sklad
    }

    [Fact]
    public async Task NazevProduktu_ZKopieKatalogu_NeznamyNull()
    {
        await web.EnsureCatalogProductAsync(9101, "Interní test – myš", 199m);
        IProductNames client = Client();

        Assert.Equal("Interní test – myš", await client.FindAsync(9101));
        Assert.Null(await client.FindAsync(9_999_999));
    }

    [Fact]
    public async Task PdfFakturyADobropisu_StejneJakoVProcesu_ChybejiciNull()
    {
        var orderId = await web.CreateOrderForAsync(_customerEmail);
        var number = await InvoicePageTests.IssueInvoiceAsync(web, orderId, _customerEmail, withCreditNote: true);

        var invoice = await Client().GetInvoiceAsync(orderId);
        var creditNote = await Client().GetCreditNoteAsync(orderId, "D" + number);
        var inProcess = await InProcess(sp => sp.GetRequiredService<IInvoiceDocuments>().GetInvoiceAsync(orderId));

        Assert.NotNull(invoice);
        Assert.Equal((inProcess!.FileName, inProcess.ContentType), (invoice.FileName, invoice.ContentType));
        Assert.Equal("%PDF"u8.ToArray(), invoice.Content[..4]);
        Assert.NotNull(creditNote);
        Assert.Equal("%PDF"u8.ToArray(), creditNote.Content[..4]);
        Assert.Null(await Client().GetInvoiceAsync(9_999_999));
        Assert.Null(await Client().GetCreditNoteAsync(orderId, "D-neexistuje"));
    }

    [Fact]
    public async Task Log_BezEmailuZakaznika()
    {
        await Client().FindAsync(_customerId);

        Assert.DoesNotContain(web.Logs.Entries, m => m.Contains(_customerEmail, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Klient budoucí služby Notifications – volá Web v paměti se servisním klíčem (jako v síti Compose).</summary>
    private WebInternalApiClient Client() =>
        new(web.CreateDefaultClient(new ServiceApiKeyHandler(MiniShopWebFactory.InternalApiKey)));

    /// <summary>Totéž v procesu – implementace, které Notifications používají, dokud běží ve Webu.</summary>
    private async Task<T> InProcess<T>(Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }
}
