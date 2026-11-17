namespace MiniShop.Web.Notifications;

/// <summary>
/// E-shop krok 23: krátký popisek zařízení pro seznam „kde máte zapnutá oznámení“, např. „Android · Chrome“. Z hlavičky
/// User-Agent se vezme jen systém a prohlížeč – celý řetězec (verze, model telefonu) se neukládá.
/// </summary>
public static class PushDeviceLabel
{
    public static string FromUserAgent(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var system = ua switch
        {
            _ when ua.Contains("iPhone", StringComparison.Ordinal) => "iPhone",
            _ when ua.Contains("iPad", StringComparison.Ordinal) => "iPad",
            _ when ua.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when ua.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when ua.Contains("Mac OS X", StringComparison.Ordinal) => "Mac",
            _ when ua.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => "Zařízení"
        };

        // Pořadí: Edge i Opera se hlásí i jako Chrome, Chrome i jako Safari.
        var browser = ua switch
        {
            _ when ua.Contains("Edg/", StringComparison.Ordinal) || ua.Contains("EdgA/", StringComparison.Ordinal) => "Edge",
            _ when ua.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when ua.Contains("SamsungBrowser/", StringComparison.Ordinal) => "Samsung Internet",
            _ when ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS/", StringComparison.Ordinal) => "Firefox",
            _ when ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) => "Chrome",
            _ when ua.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => "prohlížeč"
        };

        return $"{system} · {browser}";
    }
}
