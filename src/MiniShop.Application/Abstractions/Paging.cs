namespace MiniShop.Application.Abstractions;

/// <summary>
/// Stránkování seznamů: kterou stránku a kolik záznamů na ní. Z adresy (<c>?p=2&amp;size=50</c>) – nesmyslné hodnoty se
/// opraví (stránka &lt; 1 → 1, neznámá velikost → <see cref="DefaultSize"/>), stránka za koncem se srovná na poslední
/// (<see cref="ClampTo"/>). Přirovnání: kniha – číslo stránky a kolik řádků se na stránku vejde.
/// </summary>
public sealed record PageRequest(int Page, int Size)
{
    public const int DefaultSize = 20;

    /// <summary>Velikosti, které si uživatel může vybrat (jiná hodnota z adresy = výchozí).</summary>
    public static readonly IReadOnlyList<int> Sizes = [10, 20, 50, 100];

    public static PageRequest First { get; } = new(1, DefaultSize);

    public static PageRequest Of(int? page, int? size) =>
        new(Math.Max(1, page ?? 1), size is { } s && Sizes.Contains(s) ? s : DefaultSize);

    public int Skip => (Page - 1) * Size;

    /// <summary>Stránka za koncem (např. po smazání záznamů) → poslední existující.</summary>
    public PageRequest ClampTo(int totalCount) =>
        this with { Page = Math.Min(Page, PagedList.PageCountFor(totalCount, Size)) };
}

/// <summary>Jedna stránka seznamu + celkový počet (pro „Zobrazeno 21–40 z 135“ a odkazy na stránky).</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int Size, int TotalCount)
{
    public int PageCount => PagedList.PageCountFor(TotalCount, Size);
    public int FirstItem => TotalCount == 0 ? 0 : (Page - 1) * Size + 1;
    public int LastItem => TotalCount == 0 ? 0 : (Page - 1) * Size + Items.Count;

    public PagedList<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Page, Size, TotalCount);
}

public static class PagedList
{
    public static int PageCountFor(int totalCount, int size) => Math.Max(1, (int)Math.Ceiling(totalCount / (double)size));

    /// <summary>Stránka ze seznamu v paměti (malé seznamy, nebo data z API bez stránkování).</summary>
    public static PagedList<T> ToPagedList<T>(this IReadOnlyCollection<T> all, PageRequest request)
    {
        var page = request.ClampTo(all.Count);
        return new(all.Skip(page.Skip).Take(page.Size).ToList(), page.Page, page.Size, all.Count);
    }
}
