using System.Text.RegularExpressions;

namespace MiniShop.TrafficGenerator;

/// <summary>
/// Skladník: jedna přihlášená relace, které zákaznické scénáře zadávají práci (zabalit, předat dopravci, převzít vratku).
/// Zámek – skladník dělá jednu věc po druhé, jako člověk.
/// </summary>
public sealed class Desk(Session session, TimeSpan patience) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Zabalit a předat dopravci (e-shop krok 26B). Doručí dopravce sám – webhookem nebo dohledáním (26C), skladník už neklikne.
    /// Na „Odeslaná“ a „Doručená“ čeká zákaznický scénář – skladník mezitím obslouží ostatní.
    /// </summary>
    public async Task HandOverAsync(int orderId)
    {
        await _lock.WaitAsync();
        try
        {
            var fulfilment = await session.GetAsync("/Admin/Fulfilment");
            await session.PostAsync(fulfilment, "/Admin/Fulfilment?handler=StartPacking", [("ids", orderId.ToString())]);
            // E-shop krok 26B: kurýr = předat dopravci; číslo zásilky přidělí (simulovaný) dopravce zprávou za pár sekund.
            await session.PostAsync("/Admin/Fulfilment", $"/Admin/Fulfilment?handler=HandOver&id={orderId}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Vratka (celá objednávka = číslo 0) se objeví ve frontě až po zpracování zpráv – čekáme na ni.</summary>
    public async Task AcceptReturnAsync(int orderId)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            await _lock.WaitAsync();
            try
            {
                var returns = await session.GetAsync("/Admin/Returns");
                var html = System.Net.WebUtility.HtmlDecode(returns.Html);
                if (Regex.IsMatch(html, $@"handler=Accept(&|&amp;)id={orderId}\b|id={orderId}(&|&amp;)handler=Accept"))
                {
                    await session.PostAsync(returns, $"/Admin/Returns?handler=Accept&id={orderId}", [("returnNumber", "0")]);
                    return;
                }
            }
            finally
            {
                _lock.Release();
            }

            if (DateTime.UtcNow - started > patience)
            {
                throw new TimeoutException($"Vratka objednávky {orderId} se neobjevila ve frontě /Admin/Returns.");
            }

            await Task.Delay(1000);
        }
    }

    public void Dispose() => _lock.Dispose();
}
