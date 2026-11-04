using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Reviews;

/// <summary>
/// E-shop krok 14: hodnocení produktu zákazníkem – 1–5 hvězd a text. Jedno na zákazníka a produkt (unikátní index),
/// vlastní jde upravit, admin ho může skrýt. Kdo smí hodnotit (koupil a dostal), rozhoduje služba nad kopií nákupů.
/// </summary>
public sealed class Review
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int TextMaxLength = 1000;
    public const string AnonymizedAuthor = "Smazaný zákazník";

    // Konstruktor pro EF Core.
    private Review()
    {
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }

    /// <summary>ID účtu autora; po smazání účtu prázdné (hodnocení zůstane, autor ne).</summary>
    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Jak se autor zobrazí – „Jan N.“ (bez celého příjmení).</summary>
    public string AuthorName { get; private set; } = string.Empty;

    public int Rating { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Skryté adminem – nepočítá se do průměru a nezobrazuje se.</summary>
    public bool IsHidden { get; private set; }

    public static Review Create(int productId, string customerId, string customerName, int rating, string? text, DateTime nowUtc)
    {
        var review = new Review
        {
            ProductId = productId,
            CustomerId = customerId,
            AuthorName = ShortName(customerName),
            CreatedAtUtc = nowUtc
        };
        review.Edit(rating, text, nowUtc);
        return review;
    }

    public void Edit(int rating, string? text, DateTime nowUtc)
    {
        if (rating is < MinRating or > MaxRating)
        {
            throw new DomainException($"Hodnocení musí být {MinRating}–{MaxRating} hvězd.");
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length > TextMaxLength)
        {
            throw new DomainException($"Text hodnocení může mít nejvýš {TextMaxLength} znaků.");
        }

        Rating = rating;
        Text = trimmed;
        UpdatedAtUtc = nowUtc;
    }

    public void SetHidden(bool hidden) => IsHidden = hidden;

    /// <summary>Zákazník smazal účet – hodnocení zůstane (pomáhá ostatním), ale bez jména a vazby na účet.</summary>
    public void AnonymizeAuthor()
    {
        CustomerId = string.Empty;
        AuthorName = AnonymizedAuthor;
    }

    /// <summary>„Jan Novák“ → „Jan N.“ – ostatní zákazníci nemusí znát celé příjmení.</summary>
    public static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Zákazník",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}."
        };
    }
}
