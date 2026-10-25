---
title: Aplikace třetích stran
summary: Katalog všech knihoven, nástrojů a služeb, které MiniShop používá – k čemu slouží, proč zrovna ty a odkazy.
order: 60
---

> **V jedné větě:** MiniShop stojí na open-source knihovnách .NET, na kontejnerových nástrojích a na několika
> bezplatných online službách. Každá položka katalogu říká, **co to je**, **proč ji používáme** a **kde je v projektu**.

## Kde co je

```mermaid
flowchart TB
    subgraph lib["V kódu (knihovny)"]
        aspnet[".NET, ASP.NET Core"]
        ef["EF Core + SQLite"]
        rmqc["RabbitMQ.Client"]
        otel["OpenTelemetry, Serilog"]
        ui["Bootstrap, Mermaid, Markdig"]
    end
    subgraph run["Za běhu (servery a kontejnery)"]
        docker["Docker, Compose"]
        rabbit["RabbitMQ server"]
        caddy["Caddy"]
        dash["Aspire dashboard"]
        ubuntu["Ubuntu, cloud-init, systemd"]
    end
    subgraph svc["Online služby"]
        oci["Oracle Cloud"]
        github["GitHub, Actions, GHCR"]
        ts["Tailscale"]
        dns["DuckDNS, Let's Encrypt"]
        mon["UptimeRobot, healthchecks.io"]
    end
    lib --> run --> svc

    click aspnet "/Docs/treti-strany/knihovny"
    click ef "/Docs/treti-strany/knihovny"
    click rmqc "/Docs/treti-strany/knihovny"
    click otel "/Docs/treti-strany/knihovny"
    click ui "/Docs/treti-strany/knihovny"
    click docker "/Docs/treti-strany/beh"
    click rabbit "/Docs/treti-strany/beh"
    click caddy "/Docs/treti-strany/beh"
    click dash "/Docs/treti-strany/beh"
    click ubuntu "/Docs/treti-strany/beh"
    click oci "/Docs/treti-strany/sluzby"
    click github "/Docs/treti-strany/sluzby"
    click ts "/Docs/treti-strany/sluzby"
    click dns "/Docs/treti-strany/sluzby"
    click mon "/Docs/treti-strany/sluzby"
```

| Stránka | Obsah |
| --- | --- |
| [Knihovny a frameworky](knihovny.md) | .NET, NuGet balíčky, front-end knihovny – i s verzemi z běžící aplikace |
| [Běhové prostředí](beh.md) | Docker, RabbitMQ, Caddy, Aspire dashboard, Ubuntu, systemd… |
| [Online služby](sluzby.md) | Oracle Cloud, GitHub, Tailscale, DuckDNS, Let's Encrypt, monitoring |

## Jak se volí nová závislost

1. **Je opravdu potřeba?** Každá závislost je kód, který neznáme a musíme aktualizovat.
2. **Zdarma a open-source** (nebo free tarif bez časového omezení) – rozpočet je 0 Kč.
3. **Rozšířená a udržovaná** – aktivní vývoj, dokumentace, komunita.
4. **Přidat ji sem do katalogu** – test `DocumentationTests` jinak neprojde.
5. Aktualizace hlídá **Dependabot** ([Repozitář](../github/repozitar.md)).
