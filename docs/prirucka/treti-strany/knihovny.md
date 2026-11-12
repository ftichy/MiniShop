---
title: Knihovny a frameworky
summary: .NET, NuGet balíčky a front-end knihovny – k čemu slouží, proč je používáme a v jaké verzi běží.
order: 1
---

> **V jedné větě:** Základem je **.NET 10 / ASP.NET Core**; nad ním EF Core pro databázi, RabbitMQ.Client pro zprávy,
> OpenTelemetry a Serilog pro observabilitu a pár drobných knihoven pro UI a tuto dokumentaci.

## Verze v běžící aplikaci

{{auto:nuget-packages}}

Projekt `MiniShop.AppHost` (balíčky `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.PostgreSQL`, `MessagePack`) a testy se do Webu nenasazují, proto
v tabulce nejsou – jsou popsané níže.

## Platforma

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **.NET 10 / C#** | běhové prostředí a jazyk | LTS verze, multiplatformní (ARM i x86), výkon | [dotnet.microsoft.com](https://dotnet.microsoft.com/) |
| **ASP.NET Core** | web: Razor Pages (UI), kontrolery (REST API), DI, konfigurace, health checky | součást .NET, vše potřebné v jednom | [docs](https://learn.microsoft.com/aspnet/core/) |
| **Microsoft.AspNetCore.OpenApi** | dokument `/openapi/v1.json` (popis REST API, jen ve vývoji) | standardní popis API pro nástroje a klienty | [docs](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview) |
| **ASP.NET Core Identity** (Microsoft.AspNetCore.Identity.EntityFrameworkCore) | uživatelské účty: hash hesla, pravidla hesla, role, přihlašovací cookie; tabulky ve schématu `accounts` | hotové a prověřené – vlastní kryptografie se nepíše; umí i zámek účtu, reset hesla a přihlášení přes Google | [docs](https://learn.microsoft.com/aspnet/core/security/authentication/identity) |
| **Microsoft.AspNetCore.Authentication.Google** | přihlášení přes Google (OAuth 2.0 / OpenID Connect): přesměrování na Google, výměna kódu za identitu, ochrana návratu (`state`, korelační cookie) – účty krok 10 ([Accounts](../architektura/moduly/accounts.md#přihlášení-přes-google)) | oficiální handler ASP.NET Core, napojený na Identity (`AspNetUserLogins`); protokol se nepíše ručně | [docs](https://learn.microsoft.com/aspnet/core/security/authentication/social/google-logins) |
| **QuestPDF** | PDF faktury (e-shop krok 15) – layout popsaný v C#, písmo Lato v knihovně (české znaky i v kontejneru bez fontů), nativní knihovna i pro `linux-arm64` ([Invoicing](../architektura/moduly/invoicing.md)) | bez HTML a prohlížeče, rychlé, čitelné API; licence Community zdarma pro výukové projekty a firmy s obratem pod 1 mil. USD (`QuestPDF.Settings.License`) | [questpdf.com](https://www.questpdf.com/) |
| **Stripe.net** | platební brána Stripe (e-shop krok 16) – Checkout Session, refund, ověření podpisu webhooku (`EventUtility.ConstructEvent`), idempotency key v `RequestOptions` ([Payments](../architektura/moduly/payments.md#stripe-e-shop-krok-16)) | oficiální knihovna Stripu; podpis a formát zpráv se nepíšou ručně; `HttpClient` jde v testech podvrhnout | [GitHub](https://github.com/stripe/stripe-dotnet), [docs](https://docs.stripe.com/payments/checkout) |
| **MailKit** (+ **System.Security.Cryptography.Pkcs** připnutý na opravenou verzi) | odesílání e-mailů přes SMTP – Gmail v produkci, Mailpit lokálně ([E-maily](../architektura/emaily.md)) | doporučená náhrada zastaralého `SmtpClient`, umí STARTTLS i přihlášení; Pkcs je jeho tranzitivní závislost s opravou GHSA-846f-f764-76mx | [GitHub](https://github.com/jstedfast/MailKit) |
| **Microsoft.Extensions.Hosting.Abstractions**, **Microsoft.Extensions.Configuration.Binder**, **Microsoft.Extensions.Diagnostics.HealthChecks** | background služby (outbox worker, consumery), načtení nastavení, health checky | Infrastructure není web projekt, potřebuje jen tyto části | [docs](https://learn.microsoft.com/dotnet/core/extensions/generic-host) |

## Data a zprávy

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **Entity Framework Core** (provider viz Npgsql níže) | ORM – entity ↔ tabulky, transakce, change tracker (outbox v `SaveChanges`); **Microsoft.EntityFrameworkCore.Design** + nástroj `dotnet-ef` (`dotnet-tools.json`) pro migrace všech databází | standard v .NET; transakce přes data i outbox v jednom `DbContext`; migrace – produkční data se nesmí mazat ([Sdílené jádro – migrace](../architektura/sdilene-jadro.md)) | [docs](https://learn.microsoft.com/ef/core/) |
| **Npgsql.EntityFrameworkCore.PostgreSQL** (+ Npgsql) | EF Core provider pro PostgreSQL – jedna databáze, schéma pro každý modul (do škálování kroku 3C vedle SQLite) | oficiální a nejrozšířenější provider PostgreSQL pro .NET; cesta k víc instancím ([proč](../architektura/databaze.md)) | [npgsql.org](https://www.npgsql.org/efcore/) |
| **RabbitMQ.Client** | připojení k RabbitMQ: exchange, fronty, publisher confirms, consumery | oficiální klient; vlastní tenká vrstva ukazuje, co dělají knihovny jako MassTransit | [docs](https://www.rabbitmq.com/client-libraries/dotnet) |

## Služby a observabilita

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **OpenTelemetry** – OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.Runtime, OpenTelemetry.Instrumentation.EntityFrameworkCore | trasy, metriky a logy ve standardním formátu OTLP | nezávislé na dodavateli – stejná data jde poslat do Aspire, Grafany i cloudu | [opentelemetry.io](https://opentelemetry.io/docs/languages/dotnet/) |
| **Serilog** (Serilog.AspNetCore, Serilog.Sinks.File) | strukturované logy do konzole, souboru a OpenTelemetry | konfigurace v `appsettings`, denní soubory, jeden řádek na HTTP požadavek | [serilog.net](https://serilog.net/) |
| **Microsoft.Extensions.Http.Resilience** | opakování, timeout a circuit breaker pro HTTP klienta skladu | postavené na [Polly](https://www.pollydocs.org/); výpadek skladu neshodí stránku | [docs](https://learn.microsoft.com/dotnet/core/resilience/http-resilience) |
| **Microsoft.Extensions.ServiceDiscovery** | najde adresu služby podle jména `inventory-api` | adresu dodá Aspire nebo Compose, kód se nemění | [docs](https://learn.microsoft.com/dotnet/core/extensions/service-discovery) |
| **.NET Aspire** (Aspire.AppHost.Sdk, Aspire.Hosting.RabbitMQ, Aspire.Hosting.PostgreSQL) | lokální orchestrace: RabbitMQ, PostgreSQL, služby, dashboard | jeden příkaz pro celé prostředí; viz [Lokální vývoj](../nasazeni/lokalne.md) | [docs](https://learn.microsoft.com/dotnet/aspire/) |
| **MessagePack** | tranzitivní závislost Aspire, připnutá na opravenou verzi | bezpečnostní oprava (GHSA-qhrr-8q5h-9q3h) | [GitHub](https://github.com/MessagePack-CSharp/MessagePack-CSharp) |

## Testy

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **xUnit** (xunit, xunit.runner.visualstudio) + **Microsoft.NET.Test.Sdk** | testovací framework a jeho spouštění | nejrozšířenější v .NET, jednoduchý | [xunit.net](https://xunit.net/) |
| **Microsoft.AspNetCore.Mvc.Testing** | `WebApplicationFactory` – celý Web v paměti pro testy přes skutečné HTTP (matice oprávnění) | ověří přihlášení, cookie, policies i antiforgery dohromady, bez spuštění serveru | [docs](https://learn.microsoft.com/aspnet/core/test/integration-tests) |
| **Testcontainers.PostgreSql** | testy nad skutečným PostgreSQL – kontejner `postgres:18-alpine` spustí a po testech uklidí sám (`MINISHOP_TEST_DATABASE=Postgres`) | testuje se proti databázi, která poběží v produkci; stačí Docker, lokálně i v CI ([proč](../architektura/databaze.md)) | [testcontainers.com](https://dotnet.testcontainers.org/) |
| **Microsoft.Playwright** (+ prohlížeč Chromium) | E2E testy `tests/MiniShop.E2E` – skutečný prohlížeč projde e-shop jako zákazník (registrace, košík, pokladna, platba, expedice, mobilní zobrazení); jen testy, do Webu se nenasazuje ([CI](../github/ci.md)) | ověří dohromady to, co jednotlivé testy ne: JavaScript, cookies, CSP, fronty i ságu; automaticky čeká na prvky, umí trace při selhání; jen Chromium kvůli rychlosti CI | [playwright.dev/dotnet](https://playwright.dev/dotnet/) |
| **NetArchTest.Rules** | testy architektury – hranice modulů a vrstev | pravidla architektury hlídá CI, ne jen code review | [GitHub](https://github.com/BenMorris/NetArchTest) |

## Front-end a dokumentace

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **Bootstrap 5** (5.3.3, CSS + JS bundle, lokálně ve `wwwroot/lib/bootstrap`) | vzhled UI – mřížka, formuláře, tabulky; od redesignu A i menu pro mobil, rozbalovací nabídky a panel filtrů (JS) – [Vzhled a mobil](../architektura/vzhled.md) | hotový základ, vlastní vzhled jen CSS proměnné; JS ověřený otiskem SRI; od účtů kroku 8 ne z CDN – CSP pouští jen vlastní soubory a cizí server nevidí návštěvníky | [getbootstrap.com](https://getbootstrap.com/) |
| **Markdig** | převod Markdownu této dokumentace na HTML (na serveru) | nejrychlejší a nejúplnější Markdown pro .NET, tabulky i Mermaid bloky | [GitHub](https://github.com/xoofx/markdig) |
| **Mermaid** (lokální kopie ve `wwwroot/lib/mermaid`) | diagramy z textu v prohlížeči | diagram se píše jako text vedle dokumentace a mění se s ní v PR | [mermaid.js.org](https://mermaid.js.org/) |

## Na internetu

- [NuGet Gallery](https://www.nuget.org/) – katalog .NET balíčků
- [GitHub Advisory Database](https://github.com/advisories) – známé zranitelnosti balíčků
