using System.Net;
using System.Text;
using MiniShop.ServiceDefaults;
using MiniShop.Web.Messages;

namespace MiniShop.Tests.Web;

/// <summary>
/// Mikroservisy M2B: klient /api/messages jiné služby (stránka Zprávy). Adresy, servisní klíč a převod odpovědí –
/// cizí fronta (400) je chyba uživatele (ArgumentException), ne pád stránky. Server nahrazuje falešný HttpMessageHandler.
/// </summary>
public sealed class RemoteServiceMessagesTests
{
    [Fact]
    public async Task Prehled_PrectePresHttpSeServisnimKlicem()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request =>
        {
            sent = request;
            return Json(HttpStatusCode.OK, """
                {"modules":[{"module":"Reporting","pending":1,"poisonCount":0,"poison":[]}],"deadLetters":{"minishop.reporting":3}}
                """);
        });

        var status = await client.GetAsync();

        Assert.Equal("http://reporting/api/messages", sent!.RequestUri!.ToString());
        Assert.Equal("klic-prehledu", sent.Headers.GetValues(ServiceApiKey.HeaderName).Single());
        Assert.Equal(("Reporting", 1, 3u), (status.Modules.Single().Module, status.Modules.Single().Pending, status.DeadLetters!["minishop.reporting"]));
    }

    [Fact]
    public async Task ZkusitZnovuAVratit_SpravneAdresy()
    {
        var urls = new List<string>();
        var client = Client(request =>
        {
            urls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return request.RequestUri!.AbsolutePath.EndsWith("/retry", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{"retried":true}""")
                : Json(HttpStatusCode.OK, """{"moved":4}""");
        });
        var id = Guid.NewGuid();

        Assert.True(await client.RetryAsync("Reporting", id));
        Assert.Equal(4, await client.RequeueAsync("minishop.reporting"));
        Assert.Equal([$"POST /api/messages/Reporting/{id}/retry", "POST /api/messages/queues/minishop.reporting/requeue"], urls);
    }

    [Fact]
    public async Task CiziFronta_ArgumentException_JinaChyba_Vyjimka()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Client(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)).RequeueAsync("minishop.ordering"));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)).GetAsync());
    }

    private static RemoteServiceMessages Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new("reporting-api", "Přehled tržeb", new HttpClient(new ServiceApiKeyHandler("klic-prehledu") { InnerHandler = new FakeHandler(respond) })
        {
            BaseAddress = new Uri("http://reporting/")
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
