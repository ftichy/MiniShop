---
title: Knihovny a frameworky
summary: .NET, NuGet balíčky a front-end knihovny – k čemu slouží, proč je používáme a v jaké verzi běží.
order: 1
---

> **V jedné větě:** Základem je **.NET 10 / ASP.NET Core**; nad ním EF Core pro databázi, RabbitMQ.Client pro zprávy,
> OpenTelemetry a Serilog pro observabilitu a pár drobných knihoven pro UI a tuto dokumentaci.

## Verze v běžící aplikaci

{{auto:nuget-packages}}

Projekt `MiniShop.AppHost` (balíčky `Aspire.Hosting.RabbitMQ`, `MessagePack`) a testy se do Webu nenasazují, proto
v tabulce nejsou – jsou popsané níže.

## Platforma

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **.NET 10 / C#** | běhové prostředí a jazyk | LTS verze, multiplatformní (ARM i x86), výkon | [dotnet.microsoft.com](https://dotnet.microsoft.com/) |
| **ASP.NET Core** | web: Razor Pages (UI), kontrolery (REST API), DI, konfigurace, health checky | součást .NET, vše potřebné v jednom | [docs](https://learn.microsoft.com/aspnet/core/) |
| **Microsoft.AspNetCore.OpenApi** | dokument `/openapi/v1.json` (popis REST API, jen ve vývoji) | standardní popis API pro nástroje a klienty | [docs](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview) |
| **ASP.NET Core Identity** (Microsoft.AspNetCore.Identity.EntityFrameworkCore) | uživatelské účty: hash hesla, pravidla hesla, role, přihlašovací cookie; tabulky v `users.db` | hotové a prověřené – vlastní kryptografie se nepíše; umí i zámek účtu, reset hesla a přihlášení přes Google | [docs](https://learn.microsoft.com/aspnet/core/security/authentication/identity) |
| **Microsoft.Extensions.Hosting.Abstractions**, **Microsoft.Extensions.Configuration.Binder**, **Microsoft.Extensions.Diagnostics.HealthChecks** | background služby (outbox worker, consumery), načtení nastavení, health checky | Infrastructure není web projekt, potřebuje jen tyto části | [docs](https://learn.microsoft.com/dotnet/core/extensions/generic-host) |

## Data a zprávy

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **Entity Framework Core** + **Microsoft.EntityFrameworkCore.Sqlite** | ORM – entity ↔ tabulky, transakce, change tracker (outbox v `SaveChanges`) | standard v .NET; transakce přes data i outbox v jednom `DbContext` | [docs](https://learn.microsoft.com/ef/core/) |
| **SQLite** | databáze v jednom souboru na modul | žádný databázový server – ideální pro výuku a malou VM; online backup API | [sqlite.org](https://www.sqlite.org/) |
| **RabbitMQ.Client** | připojení k RabbitMQ: exchange, fronty, publisher confirms, consumery | oficiální klient; vlastní tenká vrstva ukazuje, co dělají knihovny jako MassTransit | [docs](https://www.rabbitmq.com/client-libraries/dotnet) |

## Služby a observabilita

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **OpenTelemetry** – OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.Runtime, OpenTelemetry.Instrumentation.EntityFrameworkCore | trasy, metriky a logy ve standardním formátu OTLP | nezávislé na dodavateli – stejná data jde poslat do Aspire, Grafany i cloudu | [opentelemetry.io](https://opentelemetry.io/docs/languages/dotnet/) |
| **Serilog** (Serilog.AspNetCore, Serilog.Sinks.File) | strukturované logy do konzole, souboru a OpenTelemetry | konfigurace v `appsettings`, denní soubory, jeden řádek na HTTP požadavek | [serilog.net](https://serilog.net/) |
| **Microsoft.Extensions.Http.Resilience** | opakování, timeout a circuit breaker pro HTTP klienta skladu | postavené na [Polly](https://www.pollydocs.org/); výpadek skladu neshodí stránku | [docs](https://learn.microsoft.com/dotnet/core/resilience/http-resilience) |
| **Microsoft.Extensions.ServiceDiscovery** | najde adresu služby podle jména `inventory-api` | adresu dodá Aspire nebo Compose, kód se nemění | [docs](https://learn.microsoft.com/dotnet/core/extensions/service-discovery) |
| **.NET Aspire** (Aspire.AppHost.Sdk, Aspire.Hosting.RabbitMQ) | lokální orchestrace: RabbitMQ, služby, dashboard | jeden příkaz pro celé prostředí; viz [Lokální vývoj](../nasazeni/lokalne.md) | [docs](https://learn.microsoft.com/dotnet/aspire/) |
| **MessagePack** | tranzitivní závislost Aspire, připnutá na opravenou verzi | bezpečnostní oprava (GHSA-qhrr-8q5h-9q3h) | [GitHub](https://github.com/MessagePack-CSharp/MessagePack-CSharp) |

## Testy

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **xUnit** (xunit, xunit.runner.visualstudio) + **Microsoft.NET.Test.Sdk** | testovací framework a jeho spouštění | nejrozšířenější v .NET, jednoduchý | [xunit.net](https://xunit.net/) |
| **Microsoft.AspNetCore.Mvc.Testing** | `WebApplicationFactory` – celý Web v paměti pro testy přes skutečné HTTP (matice oprávnění) | ověří přihlášení, cookie, policies i antiforgery dohromady, bez spuštění serveru | [docs](https://learn.microsoft.com/aspnet/core/test/integration-tests) |
| **NetArchTest.Rules** | testy architektury – hranice modulů a vrstev | pravidla architektury hlídá CI, ne jen code review | [GitHub](https://github.com/BenMorris/NetArchTest) |

## Front-end a dokumentace

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **Bootstrap 5** (z CDN jsDelivr) | vzhled UI – mřížka, formuláře, tabulky | hotový vzhled bez vlastního CSS | [getbootstrap.com](https://getbootstrap.com/) |
| **Markdig** | převod Markdownu této dokumentace na HTML (na serveru) | nejrychlejší a nejúplnější Markdown pro .NET, tabulky i Mermaid bloky | [GitHub](https://github.com/xoofx/markdig) |
| **Mermaid** (lokální kopie ve `wwwroot/lib/mermaid`) | diagramy z textu v prohlížeči | diagram se píše jako text vedle dokumentace a mění se s ní v PR | [mermaid.js.org](https://mermaid.js.org/) |

## Na internetu

- [NuGet Gallery](https://www.nuget.org/) – katalog .NET balíčků
- [GitHub Advisory Database](https://github.com/advisories) – známé zranitelnosti balíčků
