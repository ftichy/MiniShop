namespace MiniShop.Web.Inventory;

/// <summary>Krok 10B: služba skladu neodpověděla (neběží, timeout, chyba 5xx). Stránky to ukážou jako hlášku, ne chybu 500.</summary>
public sealed class InventoryUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
