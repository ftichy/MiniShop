namespace MiniShop.Application.Exceptions;

/// <summary>
/// E-shop krok 4: uložení se nepovedlo, protože stejná data mezitím změnil jiný požadavek (dvojklik, dvě záložky).
/// Vyhazuje ji infrastruktura místo výjimek EF Core – aplikační vrstva o databázi neví.
/// </summary>
public sealed class ConcurrencyConflictException(Exception innerException)
    : Exception("Data mezitím změnil jiný požadavek. Zkuste to znovu.", innerException);
