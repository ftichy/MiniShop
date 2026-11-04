namespace MiniShop.Web.Catalog;

/// <summary>E-shop krok 14: hvězdičky hodnocení jako text (★★★★☆) – výloha, detail produktu, administrace.</summary>
public static class RatingStars
{
    /// <summary>Průměr zaokrouhlený na celé hvězdy (4,6 → ★★★★★).</summary>
    public static string For(decimal rating)
    {
        var full = (int)Math.Clamp(Math.Round(rating, MidpointRounding.AwayFromZero), 0, 5);
        return new string('★', full) + new string('☆', 5 - full);
    }

    /// <summary>„4,6 (12 hodnocení)“ pro čtečku i tooltip.</summary>
    public static string Summary(decimal average, int count) => count switch
    {
        0 => "zatím bez hodnocení",
        1 => $"{average:0.0} (1 hodnocení)",
        _ => $"{average:0.0} ({count} hodnocení)"
    };
}
