using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// E-shop krok 21: jak dobře produkt odpovídá hledaným slovům – pro řazení „Nejrelevantnější“.
/// </summary>
/// <remarks>
/// Jednoduché body místo fulltextového skóre databáze: produktů jsou stovky, řadí se v paměti stejně jako podle názvu
/// a pravidla jsou čitelná. Za každé slovo se počítá nejlepší shoda v každém poli:
/// celé slovo &gt; začátek slova &gt; kus slova, a název &gt; kategorie &gt; popis.
/// „mys“ tak dá „Myš pro hráče“ (celé slovo v názvu) před „Podložka pod myš“ (taky název) před kabelem,
/// který myš zmiňuje jen v popisu.
/// </remarks>
public static class CatalogRelevance
{
    private const double NameWeight = 3;
    private const double CategoryWeight = 2;
    private const double DescriptionWeight = 1;

    public static double Score(CatalogProduct product, IReadOnlyList<string> words)
    {
        var name = CatalogSearchText.Normalize(product.Name);
        var category = CatalogSearchText.Normalize(product.CategoryName);
        var description = CatalogSearchText.Normalize(product.Description);

        var score = 0.0;
        foreach (var word in words)
        {
            score += NameWeight * Match(name, word)
                     + CategoryWeight * Match(category, word)
                     + DescriptionWeight * Match(description, word);
        }

        // Název začíná prvním hledaným slovem („notebook …“ při hledání „notebook“) – drobná přednost.
        if (words.Count > 0 && name.StartsWith(words[0], StringComparison.Ordinal))
        {
            score += 1;
        }

        return score;
    }

    /// <summary>3 = celé slovo, 2 = začátek slova, 1 = kus slova, 0 = nic. Text i slovo už normalizované.</summary>
    internal static int Match(string text, string word)
    {
        var best = 0;
        var index = text.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0 && best < 3)
        {
            var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + word.Length;
            var endsWord = end == text.Length || !char.IsLetterOrDigit(text[end]);
            best = Math.Max(best, startsWord ? (endsWord ? 3 : 2) : 1);
            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return best;
    }
}
