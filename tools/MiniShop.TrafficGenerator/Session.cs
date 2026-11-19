using System.Net;
using System.Text.RegularExpressions;

namespace MiniShop.TrafficGenerator;

/// <summary>
/// Jeden „prohlížeč“: vlastní cookies, GET stránky a POST formuláře s antiforgery tokenem. Přesměrování se následují
/// (po POSTu Razor Pages vždy přesměruje na GET), takže návratová hodnota je výsledná stránka.
/// </summary>
public sealed class Session : IDisposable
{
    private readonly HttpClient _http;

    public Session(Uri baseAddress)
    {
        _http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<Page> GetAsync(string url)
    {
        using var response = await _http.GetAsync(url);
        return await ToPageAsync(response);
    }

    /// <summary>Načte <paramref name="tokenPage"/> kvůli antiforgery tokenu a pošle na <paramref name="url"/> formulář.</summary>
    public async Task<Page> PostAsync(string tokenPage, string url, params (string Name, string Value)[] fields) =>
        await PostAsync(await GetAsync(tokenPage), url, fields);

    public async Task<Page> PostAsync(Page tokenPage, string url, IEnumerable<(string Name, string Value)> fields)
    {
        var token = HtmlForms.AntiForgeryToken(tokenPage.Html)
            ?? throw new InvalidOperationException($"Stránka {tokenPage.Url} nemá antiforgery token (nejsme přihlášení?).");
        var content = new FormUrlEncodedContent(fields
            .Append((Name: "__RequestVerificationToken", Value: token))
            .Select(field => new KeyValuePair<string, string>(field.Name, field.Value)));
        using var response = await _http.PostAsync(url, content);
        return await ToPageAsync(response);
    }

    private static async Task<Page> ToPageAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("429 – limit požadavků (RateLimiting). Zpomalte generátor nebo počkejte minutu.");
        }

        var url = response.RequestMessage?.RequestUri?.PathAndQuery ?? string.Empty;
        return new Page(url, (int)response.StatusCode, html);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record Page(string Url, int Status, string Html)
{
    public bool IsOk => Status is >= 200 and < 300;

    public int? OrderId => Regex.Match(Url, @"^/Orders/(\d+)") is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;
}
