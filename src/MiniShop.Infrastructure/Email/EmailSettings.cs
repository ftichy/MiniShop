namespace MiniShop.Infrastructure.Email;

/// <summary>
/// Účty krok 5: sekce "Email" v konfiguraci. Bez <see cref="Host"/> se e-maily jen zapisují do logu
/// (testy, lokální Compose). Lokálně Host/Port dodá Aspire (Mailpit), v produkci production.env + secret SMTP_PASSWORD.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>SMTP server, např. smtp.gmail.com. Prázdný = neodesílat, jen logovat.</summary>
    public string? Host { get; set; }

    /// <summary>587 = SMTP s STARTTLS (Gmail), Mailpit poslouchá na 1025.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Šifrovat spojení (STARTTLS). Mailpit lokálně šifrování nemá.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Přihlašovací jméno k SMTP (Gmail: celá adresa). Prázdné = bez přihlášení (Mailpit).</summary>
    public string? UserName { get; set; }

    /// <summary>Heslo k SMTP (Gmail: heslo aplikace, 16 písmen). Jen ze secretu, nikdy do gitu.</summary>
    public string? Password { get; set; }

    /// <summary>Adresa odesílatele. Gmail ji vynutí na adresu účtu, jiná by se přepsala.</summary>
    public string FromAddress { get; set; } = "minishop@localhost";

    public string FromName { get; set; } = "MiniShop";
}
