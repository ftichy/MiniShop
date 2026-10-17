using Microsoft.AspNetCore.Diagnostics;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Inventory.Api;

/// <summary>
/// Převádí výjimky na standardní odpověď ProblemDetails (RFC 9457).
/// Controllery tak nemusí obsahovat try/catch.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Nenalezeno"),
            DomainException => (StatusCodes.Status400BadRequest, "Neplatná operace"),
            _ => (StatusCodes.Status500InternalServerError, "Neočekávaná chyba serveru")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Neošetřená výjimka");
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = statusCode,
                Title = title,
                Detail = statusCode == StatusCodes.Status500InternalServerError ? null : exception.Message
            }
        });
    }
}
