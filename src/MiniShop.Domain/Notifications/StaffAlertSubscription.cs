using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Notifications;

/// <summary>
/// E-shop krok 24: zaměstnanec chce oznámení push k práci jednoho typu (např. „nová objednávka k zabalení“).
/// Výchozí stav je bez oznámení – řádek vzniká jen zaškrtnutím na stránce E-maily a oznámení. Jestli má zaměstnanec
/// k typu pořád roli, se ověřuje až při odeslání (role mu mohou odebrat).
/// </summary>
public class StaffAlertSubscription
{
    // Konstruktor pro EF Core.
    private StaffAlertSubscription()
    {
    }

    public StaffAlertSubscription(string userId, StaffAlertKind kind, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainException("Oznámení k práci potřebuje účet zaměstnance.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("Neznámý typ oznámení k práci.");
        }

        UserId = userId;
        Kind = kind;
        CreatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public StaffAlertKind Kind { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>Typy oznámení k práci. Ukládá se jako text – nové hodnoty jen na konec, nepřejmenovávat.</summary>
public enum StaffAlertKind
{
    /// <summary>Objednávka potvrzená – připravit k zabalení (Sklad a expedice).</summary>
    NewOrder,

    /// <summary>Zákazník žádá o vrácení zboží (Zákaznická podpora).</summary>
    ReturnRequested,

    /// <summary>Produkt je vyprodaný (Sklad a expedice, Správa sortimentu).</summary>
    OutOfStock
}
