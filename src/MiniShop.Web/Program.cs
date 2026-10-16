using System.Globalization;
using Microsoft.AspNetCore.Localization;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Web.ErrorHandling;
using MiniShop.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MiniShop")
    ?? throw new InvalidOperationException("Chybí connection string 'MiniShop' v appsettings.json.");

// Registrace vrstev (composition root).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // OpenAPI dokument: /openapi/v1.json
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// České formátování čísel a dat v UI (desetinná čárka, "Kč").
var czech = new CultureInfo("cs-CZ");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(czech),
    SupportedCultures = [czech],
    SupportedUICultures = [czech]
});

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();

app.Run();

// Umožní později psát integrační testy přes WebApplicationFactory<Program>.
public partial class Program { }
