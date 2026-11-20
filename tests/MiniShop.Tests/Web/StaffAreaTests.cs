using System.Collections.Concurrent;
using System.Net;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 12B: správa po oblastech – pro každou stránku a akci správy seznam rolí, které smí; test projde všech sedm
/// zaměstnaneckých rolí (každá sama, s 2FA) a ověří povoleno / „Přístup odepřen“. Nová stránka správy = řádek tady
/// (a v <c>StaffPolicies</c>), jinak není rozhodnuté, kdo na ni smí.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class StaffAreaTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-oblasti-spravy-123";
    private static readonly ConcurrentDictionary<string, bool> Created = new();

    private const string UserAdmin = AccountRoles.UserAdmin;
    private const string Catalog = AccountRoles.CatalogManager;
    private const string Warehouse = AccountRoles.Warehouse;
    private const string Support = AccountRoles.CustomerSupport;
    private const string Marketing = AccountRoles.Marketing;
    private const string Accounting = AccountRoles.Accounting;
    private const string Operations = AccountRoles.Operations;

    /// <summary>Metoda, adresa, role, které smí (ostatní zaměstnanecké role = odepřeno).</summary>
    private static readonly (string Method, string Url, string[] Allowed)[] Areas =
    [
        ("GET", "/Admin/Users", [UserAdmin]),
        ("GET", "/Admin/Users/Detail/neexistuje", [UserAdmin]),
        ("POST", "/Admin/Users/Detail/neexistuje?handler=Lock", [UserAdmin]),
        ("POST", "/Admin/Users/Detail/neexistuje?handler=Roles", [UserAdmin]),

        ("GET", "/Products", [Catalog, Warehouse]),
        ("POST", "/Products?handler=AddStock&id=999&quantity=1", [Catalog, Warehouse]),
        ("POST", "/Products?handler=Create", [Catalog]),
        ("POST", "/Products?handler=Delete&id=999", [Catalog]),
        ("POST", "/Products?handler=CreateCategory", [Catalog]),
        ("POST", "/Products?handler=DeleteCategory&id=999", [Catalog]),
        ("GET", "/Products/Edit/1", [Catalog]),
        ("POST", "/Orders?handler=SyncCatalog", [Catalog, Operations]),

        ("GET", "/Admin/Fulfilment", [Warehouse]),
        ("POST", "/Admin/Fulfilment?handler=StartPacking", [Warehouse]),
        ("POST", "/Admin/Fulfilment?handler=HandOver&id=999", [Warehouse]),   // e-shop krok 26B
        ("GET", "/Admin/Returns", [Warehouse, Support]),
        ("POST", "/Admin/Returns?handler=Accept&id=999", [Warehouse]),
        ("POST", "/Admin/Returns?handler=Reject&id=999", [Support]),

        ("GET", "/Admin/Reviews", [Support]),
        ("POST", "/Admin/Reviews?handler=Toggle&id=999", [Support]),
        ("GET", "/Orders/Journey/1", [Support]),

        ("GET", "/Admin/Coupons", [Marketing]),
        ("POST", "/Admin/Coupons?handler=Deactivate&id=999", [Marketing]),
        ("GET", "/Admin/CartInsights", [Marketing]),

        ("GET", "/Admin/Invoices", [Accounting, Support]),
        ("GET", "/Admin/Sales", [Accounting]),

        ("GET", "/Admin/Messages", [Operations]),
        ("POST", "/Admin/Messages?handler=Requeue&queue=minishop.ordering", [Operations]),
    ];

    public static TheoryData<string, string, string, bool> Cases()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var (method, url, allowed) in Areas)
        {
            foreach (var role in AccountRoles.Staff)
            {
                data.Add(role, method, url, allowed.Contains(role));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OblastSpravy(string role, string method, string url, bool allowed)
    {
        var client = await ClientAsync(role);

        var response = method == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/")
            }));

        var denied = response.StatusCode == HttpStatusCode.Redirect
            && (response.Headers.Location?.OriginalString ?? string.Empty).Contains("/Account/AccessDenied", StringComparison.Ordinal);
        Assert.True(denied != allowed,
            $"{role} {method} {url}: čekáno {(allowed ? "povoleno" : "odepřeno")}, přišlo {(int)response.StatusCode} {response.Headers.Location}");
    }

    /// <summary>Menu Správa ukazuje jen oblasti role – skladník Expedici a Produkty, ne Uživatele ani Faktury.</summary>
    [Fact]
    public async Task MenuSpravy_JenOblastiRole()
    {
        var html = await (await ClientAsync(Warehouse)).GetStringAsync("/");

        Assert.Contains("data-track=\"nav-fulfilment\"", html);
        Assert.Contains("data-track=\"nav-products\"", html);
        Assert.Contains("data-track=\"nav-returns\"", html);
        Assert.DoesNotContain("data-track=\"nav-users\"", html);
        Assert.DoesNotContain("data-track=\"nav-invoices\"", html);
        Assert.DoesNotContain("data-track=\"nav-messages\"", html);
    }

    /// <summary>Skladník vidí produkty a naskladňuje, ale formulář nového produktu ani kategorie nemá.</summary>
    [Fact]
    public async Task Produkty_SkladnikBezUprav()
    {
        var warehouse = await (await ClientAsync(Warehouse)).GetStringAsync("/Products");
        var catalog = await (await ClientAsync(Catalog)).GetStringAsync("/Products");

        Assert.DoesNotContain("handler=Create", warehouse);
        Assert.Contains("handler=AddStock", warehouse);
        Assert.Contains("handler=Create", catalog);
    }

    private async Task<HttpClient> ClientAsync(string role)
    {
        var email = $"oblast-{role.ToLowerInvariant()}@test.local";
        if (Created.TryAdd(email, true))
        {
            await web.CreateWithRolesAsync(email, Password, role);
        }

        return await web.LoginAsync(email, Password);
    }
}
