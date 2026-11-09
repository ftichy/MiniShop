using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Invoicing;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Invoicing;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 15: PDF faktury přes Web – vlastník a admin ho stáhnou, cizí zákazník dostane 404; detail objednávky
/// ukáže odkaz, admin vidí fakturu v přehledu. Faktura vzniká rovnou v databázi (workery v testech Webu neběží).
/// </summary>
[Collection(WebCollection.Name)]
public sealed class InvoicePageTests(MiniShopWebFactory web) : IAsyncLifetime
{
    private const string Password = "heslo-pro-faktury-123";
    private readonly string _janEmail = $"faktura-jan-{Guid.NewGuid():N}@test.local";
    private readonly string _petraEmail = $"faktura-petra-{Guid.NewGuid():N}@test.local";
    private int _orderId;
    private string _number = string.Empty;

    public async Task InitializeAsync()
    {
        await web.CreateUserAsync(_janEmail, Password);
        await web.CreateUserAsync(_petraEmail, Password);
        _orderId = await web.CreateOrderForAsync(_janEmail);
        _number = await IssueInvoiceAsync(_orderId, _janEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Vlastnik_StahnePdf_DetailMaOdkaz()
    {
        var jan = await web.LoginAsync(_janEmail, Password);

        var response = await jan.GetAsync($"/Orders/{_orderId}/Invoice");
        var detail = await jan.GetStringAsync($"/Orders/{_orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"faktura-{_number}.pdf", response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("%PDF"u8.ToArray(), (await response.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Contains($"/Orders/{_orderId}/Invoice", detail);
        Assert.Contains(_number, detail);
    }

    [Fact]
    public async Task CiziZakaznik_404()
    {
        var petra = await web.LoginAsync(_petraEmail, Password);

        Assert.Equal(HttpStatusCode.NotFound, (await petra.GetAsync($"/Orders/{_orderId}/Invoice")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await petra.GetAsync($"/Orders/{_orderId}/CreditNote")).StatusCode);
    }

    [Fact]
    public async Task Admin_StahnePdf_AVidiFakturuVPrehledu()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Orders/{_orderId}/Invoice")).StatusCode);
        Assert.Contains(_number, await admin.GetStringAsync("/Admin/Invoices"));
    }

    [Fact]
    public async Task Dobropis_VlastnikStahne_DetailIPrehledMajiOdkaz()
    {
        var orderId = await web.CreateOrderForAsync(_janEmail);
        await IssueInvoiceAsync(orderId, _janEmail, withCreditNote: true);
        var jan = await web.LoginAsync(_janEmail, Password);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var response = await jan.GetAsync($"/Orders/{orderId}/CreditNote");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("%PDF"u8.ToArray(), (await response.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Contains($"/Orders/{orderId}/CreditNote", await jan.GetStringAsync($"/Orders/{orderId}"));
        Assert.Contains($"/Orders/{orderId}/CreditNote", await admin.GetStringAsync("/Admin/Invoices"));
        Assert.Equal(HttpStatusCode.NotFound, (await jan.GetAsync($"/Orders/{_orderId}/CreditNote")).StatusCode);   // bez dobropisu
    }

    /// <summary>Faktura rovnou v databázi – jako by Invoicing zpracoval OrderPlacedV2 a PaymentSucceededV1.</summary>
    private async Task<string> IssueInvoiceAsync(int orderId, string email, bool withCreditNote = false)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var order = BillableOrder.Create(orderId);
        order.ApplyPlaced(user!.Id, "Jan Novák", null, "Pickup", 0m, 200m, DateTime.UtcNow,
            [new InvoiceLine("Myš – černá, česká klávesnice", 2, 100m, 0m, 200m)]);
        order.MarkPaid(DateTime.UtcNow, "Uhrazeno kartou online");
        // Číslo z vlastní „řady“ testu (rok 3000+) – testy sdílí databázi a řada roku se jinde nepoužije.
        var number = $"{3000 + orderId % 6000}{orderId % 1_000_000:D6}";
        var invoice = Invoice.Issue(order, number, DateTime.UtcNow);
        db.BillableOrders.Add(order);
        db.Invoices.Add(invoice);
        if (withCreditNote)
        {
            // E-shop krok 15B: dobropis s číslem mimo skutečnou řadu (jako u faktury výše).
            db.CreditNotes.Add(CreditNote.Issue(invoice, "D" + number, CreditNoteReason.Cancelled, "Vráceno na kartu", DateTime.UtcNow));
        }

        await db.SaveChangesAsync();
        return number;
    }
}
