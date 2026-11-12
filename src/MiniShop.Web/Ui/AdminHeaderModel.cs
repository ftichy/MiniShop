namespace MiniShop.Web.Ui;

/// <summary>
/// Vzhled C: hlavička stránky administrace (partial <c>_AdminHeader</c>) – drobečková navigace „Správa › …“ a nadpis.
/// Na telefonu admin hned ví, kde je (menu Správa je schované v ☰).
/// </summary>
/// <param name="Title">Nadpis stránky.</param>
/// <param name="Parent">Nadřazená stránka (např. „Uživatelé“ u detailu uživatele), nebo null.</param>
/// <param name="ParentPage">Razor stránka nadřazené položky (např. <c>/Admin/Users/Index</c>).</param>
public sealed record AdminHeaderModel(string Title, string? Parent = null, string? ParentPage = null);
