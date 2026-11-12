using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniShop.E2E;

/// <summary>
/// Čte e-maily z Mailpitu (cvičná pošta v compose.e2e.yaml). Aplikace se kvůli tomu nemění –
/// Web posílá e-maily přes SMTP jako vždy, jen server je Mailpit místo Gmailu.
/// </summary>
public sealed partial class MailpitClient(string baseUrl)
{
    private static readonly HttpClient Http = new();

    /// <summary>Počká na e-mail pro adresu a vrátí první odkaz, jehož adresa obsahuje <paramref name="pathContains"/>.</summary>
    public async Task<string> WaitForLinkAsync(string toEmail, string pathContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var link = await TryFindLinkAsync(toEmail, pathContains);
                if (link is not null)
                {
                    return link;
                }
            }
            catch (HttpRequestException ex)
            {
                last = ex;   // Mailpit krátce nedostupný – zkusit znovu
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"E-mail pro {toEmail} s odkazem na {pathContains} nedorazil do Mailpitu za {timeout.TotalSeconds:0} s.", last);
    }

    private async Task<string?> TryFindLinkAsync(string toEmail, string pathContains)
    {
        var search = await Http.GetStringAsync($"{baseUrl}/api/v1/search?query={Uri.EscapeDataString($"to:{toEmail}")}");
        using var list = JsonDocument.Parse(search);
        if (!list.RootElement.TryGetProperty("messages", out var messages))
        {
            return null;
        }

        foreach (var message in messages.EnumerateArray())
        {
            var id = message.GetProperty("ID").GetString();
            var detail = await Http.GetStringAsync($"{baseUrl}/api/v1/message/{id}");
            using var doc = JsonDocument.Parse(detail);
            var text = doc.RootElement.TryGetProperty("Text", out var t) ? t.GetString() ?? "" : "";
            foreach (Match match in UrlPattern().Matches(WebUtility.HtmlDecode(text)))
            {
                if (match.Value.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }
            }
        }

        return null;
    }

    [GeneratedRegex("https?://[^\\s<>\"]+")]
    private static partial Regex UrlPattern();
}
