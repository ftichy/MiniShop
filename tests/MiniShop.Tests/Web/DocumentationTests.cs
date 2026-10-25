using System.Text.RegularExpressions;
using MiniShop.Contracts;
using MiniShop.Domain.Common;
using MiniShop.Web.Documentation;

namespace MiniShop.Tests.Web;

/// <summary>
/// Příručka v aplikaci (/Docs): testy hlídají, aby dokumentace nezastarala potichu. Přejmenovaný soubor, nový projekt,
/// kontrakt nebo doménová událost bez popisu = červené CI a jasná zpráva, kterou stránku doplnit.
/// </summary>
public sealed partial class DocumentationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly DocumentationStore Store = new(Path.Combine(RepositoryRoot, "docs", "prirucka"));
    private static readonly DocumentationRenderer Renderer = new(new DocumentationOptions());

    public static TheoryData<string> PageSlugs()
    {
        var data = new TheoryData<string>();
        foreach (var page in Store.All())
        {
            data.Add(page.Slug);
        }

        return data;
    }

    [Fact]
    public void Prirucka_MaUvodniStrankuAStrom()
    {
        var tree = Store.Tree();

        Assert.NotNull(tree);
        Assert.NotEmpty(tree.Children);
    }

    [Theory]
    [MemberData(nameof(PageSlugs))]
    public void KazdaStranka_MaNazevShrnutiAJdeVykreslit(string slug)
    {
        var page = Store.Find(slug);

        Assert.NotNull(page);
        Assert.False(page.Title == page.Slug, $"Stránka '{slug}' nemá v hlavičce 'title'.");
        Assert.False(string.IsNullOrWhiteSpace(page.Summary), $"Stránka '{slug}' nemá v hlavičce 'summary'.");
        Assert.NotEqual(int.MaxValue, page.Order);
        Assert.DoesNotContain("Neznámý automatický blok", Renderer.Render(page).Html);
    }

    [Fact]
    public void OdkazyMeziStrankami_VedouNaExistujiciStranky()
    {
        var broken = new List<string>();
        foreach (var page in Store.All())
        {
            var html = Renderer.Render(page).Html;
            var targets = InternalHrefPattern().Matches(html).Select(m => m.Groups[1].Value)
                .Concat(MermaidClickPattern().Matches(page.Markdown).Select(m => m.Groups[1].Value));

            broken.AddRange(targets
                .Where(target => Store.Find(target.Trim('/')) is null)
                .Select(target => $"{FileOf(page)} → /Docs{target}"));
        }

        Assert.True(broken.Count == 0, "Odkazy na neexistující stránky:\n" + string.Join('\n', broken));
    }

    [Fact]
    public void OdkazyDoKodu_VedouNaExistujiciSoubory()
    {
        var broken = Store.All()
            .SelectMany(page => CodeLinkPattern().Matches(page.Markdown).Select(m => (Page: page, Path: m.Groups[1].Value)))
            .Where(link => !File.Exists(Path.Combine(RepositoryRoot, link.Path))
                           && !Directory.Exists(Path.Combine(RepositoryRoot, link.Path)))
            .Select(link => $"{FileOf(link.Page)} → {link.Path}")
            .ToList();

        Assert.True(broken.Count == 0, "Odkazy do kódu na neexistující soubory (přejmenovaná třída?):\n" + string.Join('\n', broken));
    }

    [Fact]
    public void KazdyProjektVeSrc_JeVDokumentaciZminen()
    {
        var text = AllMarkdown();
        var missing = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(dir => Directory.EnumerateFiles(dir, "*.csproj").Any())
            .Select(Path.GetFileName)
            .Where(project => !text.Contains(project!, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, "Projekty bez zmínky v docs/prirucka: " + string.Join(", ", missing));
    }

    /// <summary>Automatická tabulka kontrakt vypíše sama – tady jde o to, aby ho někdo i popsal (k čemu je, kdo ho posílá).</summary>
    [Fact]
    public void KazdyKontrakt_JePopsanMimoAutomatickouTabulku()
    {
        var messages = typeof(IIntegrationMessage).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationMessage).IsAssignableFrom(t))
            .Select(t => t.Name);

        AssertMentioned(messages, "Kontrakty");
    }

    [Fact]
    public void KazdaDomenovaUdalost_JePopsanaMimoAutomatickouTabulku()
    {
        var events = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(t => t.Name);

        AssertMentioned(events, "Doménové události");
    }

    [Fact]
    public void KazdyNuGetBalicek_JeVKataloguTretichStran()
    {
        var packages = Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => PackageReferencePattern().Matches(File.ReadAllText(path))
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .Distinct();

        AssertMentionedLiterally(packages, "NuGet balíčky");
    }

    [Fact]
    public void KazdaDockerImage_JeVKataloguTretichStran()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "deploy"))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "Dockerfile", SearchOption.AllDirectories))
            .Select(File.ReadAllText);
        var images = sources
            .SelectMany(text => DockerImagePattern().Matches(text).Select(m => m.Groups[1].Value))
            .Where(image => !image.Contains('$') && image != "build")
            .Select(image => image.Split(':')[0])
            .Distinct();

        AssertMentionedLiterally(images, "Docker image");
    }

    [Fact]
    public void KazdaGitHubAction_JeVDokumentaci()
    {
        var actions = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, ".github", "workflows"))
            .SelectMany(path => GitHubActionPattern().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value))
            .Distinct();

        AssertMentionedLiterally(actions, "GitHub Actions");
    }

    [Fact]
    public void KazdySecretAPromennaWorkflow_JsouPopsane()
    {
        var names = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, ".github", "workflows"))
            .SelectMany(path => WorkflowSecretPattern().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value))
            .Distinct();

        AssertMentionedLiterally(names, "Secrets a proměnné workflow");
    }

    [Fact]
    public void KazdySouborNasazeniAInfrastruktury_JeVDokumentaci()
    {
        // Jen soubory z gitu – skutečné vstupy OpenTofu (terraform.tfvars, backend.hcl, state) a .env v repozitáři nejsou.
        string[] ignored = ["terraform.tfvars", "backend.hcl", "crash.log"];
        var files = new[] { "deploy", "infra" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, dir)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith('.') && !ignored.Contains(name)
                           && !name.Contains(".tfstate", StringComparison.Ordinal) && !name.EndsWith(".tfplan", StringComparison.Ordinal));

        AssertMentionedLiterally(files, "Soubory v deploy/ a infra/");
    }

    [Theory]
    [InlineData("../CLAUDE")]
    [InlineData("eda/../../CLAUDE")]
    [InlineData("..%2fCLAUDE")]
    [InlineData("C:/Windows/win")]
    [InlineData("/etc/passwd")]
    [InlineData("EDA/Outbox")]
    public void Store_NepustiCestuMimoPrirucku(string slug)
    {
        Assert.Null(Store.Find(slug));
    }

    [Fact]
    public void Renderer_PrepiseOdkazyNaStrankyADoKodu()
    {
        var page = new DocumentationPage("eda/test", "eda", "Test", "Shrnutí", 1,
            "[Inbox](inbox.md#idempotence) [Úvod](../index.md) [Kód](code:src/MiniShop.Web/Program.cs) [Web](https://example.com)");
        var renderer = new DocumentationRenderer(new DocumentationOptions { Commit = "sha-0123456789abcdef" });

        var html = renderer.Render(page).Html;

        Assert.Contains("href=\"/Docs/eda/inbox#idempotence\"", html);
        Assert.Contains("href=\"/Docs\"", html);
        Assert.Contains("href=\"https://github.com/ftichy/MiniShop/blob/0123456789abcdef/src/MiniShop.Web/Program.cs\"", html);
        Assert.Contains("target=\"_blank\"", html);
    }

    [Fact]
    public void Renderer_BezCommituOdkazujeNaMain()
    {
        var renderer = new DocumentationRenderer(new DocumentationOptions { Commit = "local" });

        Assert.Equal("https://github.com/ftichy/MiniShop/tree/main/src", renderer.CodeUrl("src/"));
    }

    [Fact]
    public void Renderer_MermaidBlokVykresliProProhlizec()
    {
        var page = new DocumentationPage("test", "", "Test", "Shrnutí", 1, "```mermaid\nflowchart LR\n  a --> b\n```");

        var html = Renderer.Render(page).Html;

        Assert.Contains("class=\"mermaid\"", html);
    }

    private static void AssertMentioned(IEnumerable<string> names, string what)
    {
        var text = AllMarkdown();
        var missing = names.Where(name => !Regex.IsMatch(text, $@"\b{name}\b")).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0, $"{what} bez popisu v docs/prirucka: " + string.Join(", ", missing));
    }

    private static void AssertMentionedLiterally(IEnumerable<string> names, string what)
    {
        var all = names.ToList();
        Assert.NotEmpty(all);   // pojistka: test nesmí projít jen proto, že vzor nic nenašel

        var text = AllMarkdown();
        var missing = all.Where(name => !text.Contains(name, StringComparison.Ordinal)).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0, $"{what} bez zmínky v docs/prirucka: " + string.Join(", ", missing));
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string AllMarkdown() => string.Join('\n', Store.All().Select(p => p.Markdown));

    private static string FileOf(DocumentationPage page) => page.Slug.Length == 0 ? "index.md" : page.Slug;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiniShop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Kořen repozitáře (MiniShop.slnx) nenalezen.");
    }

    /// <summary>href="/Docs/eda/inbox#x" → "eda/inbox" (úvod "/Docs" → "").</summary>
    [GeneratedRegex("""href="/Docs(/[^"#]*)?""")]
    private static partial Regex InternalHrefPattern();

    /// <summary>click uzel "/Docs/eda/saga" v diagramu Mermaid.</summary>
    [GeneratedRegex("""click\s+\S+\s+(?:href\s+)?"/Docs(/[^"#]*)?["#]""")]
    private static partial Regex MermaidClickPattern();

    [GeneratedRegex(@"\]\(code:([^)\s#]+)\)")]
    private static partial Regex CodeLinkPattern();

    [GeneratedRegex("""<PackageReference\s+Include="([^"]+)"|Sdk="([^"/]+)/""")]
    private static partial Regex PackageReferencePattern();

    /// <summary>image: x v Compose, FROM [--platform=…] x [AS y] v Dockerfile, IMAGE=x ve skriptech.</summary>
    [GeneratedRegex(@"(?m)^\s*(?:image:\s*|FROM\s+(?:--platform=\S+\s+)?|IMAGE=)(\S+)")]
    private static partial Regex DockerImagePattern();

    [GeneratedRegex(@"uses:\s*([\w.-]+/[\w.-]+)@")]
    private static partial Regex GitHubActionPattern();

    [GeneratedRegex(@"(?:secrets|vars)\.([A-Z][A-Z0-9_]+)")]
    private static partial Regex WorkflowSecretPattern();
}
