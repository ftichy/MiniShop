namespace MiniShop.Web.Carts;

/// <summary>Vzhled krok F1: hláška po přidání do košíku přes htmx (partial <c>_CartAddResult</c>) – úspěch, nebo porušené pravidlo košíku.</summary>
public sealed record CartAddResultModel(string Message, bool IsError);
