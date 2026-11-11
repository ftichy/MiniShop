using MiniShop.Application.Ordering;

namespace MiniShop.Web.Catalog;

/// <summary>
/// Redesign A: karta produktu (partial <c>_ProductCard</c>) – stejná ve výloze i na úvodu.
/// </summary>
/// <param name="CanOrder">Zákazník s potvrzeným e-mailem – vidí „Do košíku“.</param>
/// <param name="IsAnonymous">Nepřihlášený – vidí „Přihlásit a nakoupit“.</param>
/// <param name="ReturnUrl">Kam se po přidání do košíku vrátit (výloha i s filtry).</param>
public sealed record ProductCardModel(CatalogItemDto Product, bool CanOrder, bool IsAnonymous, string ReturnUrl);
