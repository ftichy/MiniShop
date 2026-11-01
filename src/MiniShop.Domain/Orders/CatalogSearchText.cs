using System.Globalization;
using System.Text;

namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 2: text pro hledání ve výloze – malá písmena bez diakritiky („Myš“ → „mys“).
/// Stejně se upraví uložený text produktu i to, co zákazník napíše, takže „mys“, „MYŠ“ i „myš“ najdou totéž.
/// </summary>
/// <remarks>
/// Proč ne rovnou SQL: SQLite porovnává bez ohledu na velikost písmen jen ASCII a o diakritice neví.
/// Read model si proto drží text už připravený pro hledání (CQRS: data tvarovaná podle čtení).
/// </remarks>
public static class CatalogSearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            // Rozložené „š“ = „s“ + háček (NonSpacingMark) – háček zahodíme.
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && result.Length > 0)
                {
                    result.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            result.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return result.ToString().TrimEnd();
    }
}
