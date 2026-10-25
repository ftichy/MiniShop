namespace MiniShop.Web.Documentation;

public static class DocumentationServiceCollectionExtensions
{
    /// <summary>
    /// Příručka v aplikaci (/Docs). Markdown soubory z <c>docs/prirucka</c> se při buildu kopírují vedle DLL
    /// (a tím i do Docker image), takže nasazená aplikace má vždy dokumentaci ke stejnému commitu.
    /// Při vývoji ukazuje <c>Documentation:ContentPath</c> přímo do repozitáře – změna textu je vidět bez buildu.
    /// </summary>
    public static IServiceCollection AddDocumentation(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(DocumentationOptions.SectionName).Get<DocumentationOptions>() ?? new();
        var rootPath = string.IsNullOrWhiteSpace(options.ContentPath)
            ? Path.Combine(AppContext.BaseDirectory, "prirucka")
            : Path.Combine(environment.ContentRootPath, options.ContentPath);

        services.AddSingleton(options);
        services.AddSingleton(new DocumentationStore(rootPath));
        services.AddSingleton<DocumentationRenderer>();

        return services;
    }
}
