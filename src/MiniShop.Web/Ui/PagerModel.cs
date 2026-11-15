using Microsoft.AspNetCore.Http.Extensions;
using MiniShop.Application.Abstractions;

namespace MiniShop.Web.Ui;

/// <summary>
/// Stránkování seznamů (partial <c>_Pager</c>): „Zobrazeno 21–40 z 135“, výběr velikosti stránky a odkazy
/// ‹ 1 … 4 5 [6] 7 8 … 20 ›. Odkazy jsou obyčejné GET adresy, které zachovají ostatní parametry (hledání, filtry).
/// </summary>
/// <param name="Track">Hodnota <c>data-track</c> (jméno seznamu) – kliky na stránky jdou rozlišit.</param>
public sealed record PagerModel(int Page, int PageCount, int Size, int TotalCount, int FirstItem, int LastItem, string Track)
{
    // „p“ jako ve výloze – „page“ je v Razor Pages rezervovaná hodnota trasy (cesta stránky) a kolidovala by.
    public const string PageParameter = "p";
    public const string SizeParameter = "size";

    public static PagerModel From<T>(PagedList<T> list, string track) =>
        new(list.Page, list.PageCount, list.Size, list.TotalCount, list.FirstItem, list.LastItem, track);

    /// <summary>Čísla stránek k zobrazení; null = vynechané („…“). Vždy první, poslední a dvě kolem aktuální.</summary>
    public IReadOnlyList<int?> Window()
    {
        var pages = new SortedSet<int> { 1, PageCount };
        for (var page = Page - 2; page <= Page + 2; page++)
        {
            if (page >= 1 && page <= PageCount)
            {
                pages.Add(page);
            }
        }

        var result = new List<int?>();
        int? previous = null;
        foreach (var page in pages)
        {
            if (previous is not null && page - previous > 1)
            {
                result.Add(null);
            }

            result.Add(page);
            previous = page;
        }

        return result;
    }

    /// <summary>Adresa stránky: aktuální cesta + všechny parametry kromě stránky a velikosti, které se nastaví znovu.</summary>
    public static string Url(HttpRequest request, int page, int size)
    {
        var query = new QueryBuilder(OtherParameters(request));
        if (page > 1)
        {
            query.Add(PageParameter, page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (size != PageRequest.DefaultSize)
        {
            query.Add(SizeParameter, size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return request.PathBase + request.Path + query.ToQueryString();
    }

    /// <summary>Parametry adresy kromě stránky a velikosti (pro skrytá pole formuláře velikosti a pro odkazy).</summary>
    public static IEnumerable<KeyValuePair<string, string>> OtherParameters(HttpRequest request) =>
        request.Query
            .Where(pair => pair.Key is not PageParameter and not SizeParameter && !pair.Key.Equals("handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string>(pair.Key, value ?? string.Empty)));
}
