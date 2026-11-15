using System.Globalization;
using System.Text;

namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 2: text pro hledání ve výloze – malá písmena bez diakritiky („Myš“ → „mys“).
/// Stejně se upraví uložený text produktu i to, co zákazník napíše, takže „mys“, „MYŠ“ i „myš“ najdou totéž.
/// </summary>
/// <remarks>
/// Proč ne rovnou SQL: databáze bez zvláštního nastavení (unaccent, kolace) neporovná „myš“ a „MYS“ jako shodu.
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

    /// <summary>Nejvýš tolik slov z dotazu – delší dotaz je spíš omyl (vložený text) než hledání.</summary>
    public const int MaxWords = 8;

    /// <summary>
    /// E-shop krok 21: dotaz rozdělený na slova (normalizovaná, bez interpunkce na krajích, bez opakování).
    /// „Notebook, 32 GB!“ → [„notebook“, „32“, „gb“]. Každé slovo pak musí být v textu produktu – v libovolném pořadí.
    /// </summary>
    public static IReadOnlyList<string> Words(string? query) =>
        Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(TrimmedPunctuation))
            .Where(word => word.Length > 0)
            .Distinct()
            .Take(MaxWords)
            .ToList();

    // Interpunkce na krajích slova („myš,“ „(usb-c)“); uvnitř zůstane („usb-c“, „2.5“, „14"“ se zkrátí na „14“).
    private static readonly char[] TrimmedPunctuation = ".,;:!?\"'()[]{}“”„‚‘’«»…-–—/\\*+".ToCharArray();
}
