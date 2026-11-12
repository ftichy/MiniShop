using Microsoft.Playwright;

namespace MiniShop.E2E;

/// <summary>
/// Společný základ: každý test má vlastní Chromium a vlastní kontext (cookies) – testy se neovlivňují.
/// Při selhání se uloží Playwright trace (<c>npx playwright show-trace</c> / trace.playwright.dev) a snímek stránky.
/// </summary>
public abstract class E2ETestBase : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    protected static string BaseUrl => E2EEnvironment.BaseUrl!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    /// <summary>Nový „člověk s prohlížečem“. <paramref name="mobile"/> = viewport telefonu 390×844 s dotykem.</summary>
    protected async Task<Session> NewSessionAsync(string name, bool mobile = false)
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            Locale = "cs-CZ",
            ViewportSize = mobile ? new ViewportSize { Width = 390, Height = 844 } : new ViewportSize { Width = 1280, Height = 900 },
            IsMobile = mobile,
            HasTouch = mobile
        };
        var context = await _browser!.NewContextAsync(options);
        context.SetDefaultTimeout(15_000);
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false });
        var page = await context.NewPageAsync();
        return new Session(name, context, page);
    }

    /// <summary>Spustí scénář; při výjimce uloží trace a snímek všech relací a výjimku pošle dál.</summary>
    protected static async Task RunAsync(Func<Task> scenario, params Func<Session?>[] sessions)
    {
        try
        {
            await scenario();
        }
        catch
        {
            foreach (var get in sessions)
            {
                if (get() is { } session)
                {
                    await session.SaveFailureArtifactsAsync();
                }
            }

            throw;
        }
    }

    /// <summary>Jedna relace prohlížeče (kontext + stránka).</summary>
    public sealed class Session(string name, IBrowserContext context, IPage page) : IAsyncDisposable
    {
        public IPage Page => page;

        public async Task SaveFailureArtifactsAsync()
        {
            Directory.CreateDirectory(E2EEnvironment.ArtifactsDir);
            var file = $"{name}-{DateTime.UtcNow:HHmmss}";
            try
            {
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(E2EEnvironment.ArtifactsDir, file + ".png"),
                    FullPage = true
                });
            }
            catch (PlaywrightException)
            {
                // snímek je jen bonus – chybu testu nepřebije
            }

            await context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(E2EEnvironment.ArtifactsDir, file + ".trace.zip") });
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await context.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // kontext už mohl být zavřený
            }
        }
    }
}
