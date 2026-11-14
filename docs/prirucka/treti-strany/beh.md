---
title: Běhové prostředí
summary: Docker, RabbitMQ, Caddy, Aspire dashboard, Ubuntu a další nástroje, na kterých MiniShop běží.
order: 2
---

> **V jedné větě:** Na serveru běží **Ubuntu** s **Dockerem**; v kontejnerech MiniShop, **RabbitMQ**, **Caddy**
> a **Aspire dashboard**. O spouštění a plánování se starají **cloud-init** a **systemd**.

## Kontejnery a jejich image

| Co (image) | K čemu | Proč zrovna to | Odkazy |
| --- | --- | --- | --- |
| **Docker** + **Docker Compose** | balení a spouštění aplikace v kontejnerech, propojení služeb | stejné prostředí na PC i v cloudu; Compose stačí pro jednu VM | [docs.docker.com](https://docs.docker.com/) |
| **Docker Buildx** + **QEMU** | sestavení image pro x86 i ARM v CI | VM v cloudu je ARM, runner GitHubu x86 | [docs](https://docs.docker.com/build/building/multi-platform/) |
| `mcr.microsoft.com/dotnet/sdk`, `mcr.microsoft.com/dotnet/aspnet` | základ image služeb (build / runtime) | oficiální image Microsoftu, aktualizace hlídá Dependabot | [MCR](https://mcr.microsoft.com/catalog?search=dotnet) |
| **RabbitMQ** (`rabbitmq:4-management`) | message broker – exchange, fronty, webová správa | zralý, rozšířený, přehledný pro výuku ([proč](../eda/broker.md)) | [rabbitmq.com](https://www.rabbitmq.com/) |
| **PostgreSQL** (`postgres:18-alpine` v Compose a testech, lokálně přes Aspire) | databázový server – jedna databáze `minishop`, schéma pro každý modul; zálohuje `pg_dump` v noční záloze | víc instancí služeb potřebuje databázi po síti; zdarma, ověřený, dobrá podpora v EF Core ([proč](../architektura/databaze.md)) | [postgresql.org](https://www.postgresql.org/) |
| **Caddy** (`caddy:2`) | reverse proxy, automatické HTTPS, basic auth | certifikát Let's Encrypt bez konfigurace ([proč](../nasazeni/https-a-pristup.md)) | [caddyserver.com](https://caddyserver.com/) |
| **Aspire dashboard** (`mcr.microsoft.com/dotnet/aspire-dashboard`) | trasy, logy, metriky v cloudu | stejný nástroj jako lokálně, jeden kontejner ([proč](../nasazeni/monitoring.md)) | [docs](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone) |
| **Grafana Alloy** (`grafana/alloy`, jen v produkci přes `compose.grafana.yaml`) | „rozbočovač“ telemetrie (OpenTelemetry collector): přijme OTLP od služeb, pošle vše do Aspire dashboardu a bez osobních údajů do Grafana Cloud ([config.alloy](code:deploy/config.alloy)) | oficiální collector Grafany, konfigurace jako kód, malý (desítky MB RAM); aplikace se kvůli Grafaně nemění ([detail](../nasazeni/monitoring.md)) | [grafana.com/docs/alloy](https://grafana.com/docs/alloy/latest/) |
| **Prometheus – `promtool`** (`prom/prometheus`, jen v CI – job „Build a testy“) | kontrola pravidel alarmů Grafany (`deploy/grafana/minishop-alarmy.yaml`) – syntaxe YAML i dotazů PromQL ([CI](../github/ci.md)) | oficiální nástroj formátu pravidel; chybný alarm se pozná v PR, ne až při importu do Grafany | [prometheus.io](https://prometheus.io/docs/prometheus/latest/command-line/promtool/) |
| **Mailpit** (`axllent/mailpit`, jen lokálně v Aspire a v E2E testech přes `compose.e2e.yaml` – test z jeho API přečte odkaz na potvrzení e-mailu, [CI](../github/ci.md)) | „cvičná pošta“ – zachytí každý e-mail z Webu a ukáže ho ve webovém rozhraní; nic neodejde ven | vývoj e-mailů bez rizika, že něco dorazí skutečnému člověku ([proč](../architektura/emaily.md)) | [mailpit.axllent.org](https://mailpit.axllent.org/) |
| **OWASP ZAP** (`ghcr.io/zaproxy/zaproxy`, jen v CI – job „ZAP sken“, [CI](../github/ci.md)) | pasivní bezpečnostní sken běžící aplikace: hlavičky, cookies, CSP, zranitelné JS knihovny, chybové stránky | open source standard OWASP, hotový „baseline“ skript pro CI, pravidla jde po jednom vypnout s odůvodněním | [zaproxy.org](https://www.zaproxy.org/) |
| **OCI CLI** (`ghcr.io/oracle/oci-cli`) | nástroj pro práci s Oracle Cloud; v noční záloze nahrává do bucketu | z image – bez instalace Pythonu na PC ani na VM | [docs](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/cliconcepts.htm) |

## Operační systém a správa serveru

| Co | K čemu | Proč | Odkazy |
| --- | --- | --- | --- |
| **Ubuntu 24.04 LTS** (ARM) | operační systém VM | dlouhodobá podpora, oficiální obraz v OCI s Oracle Cloud Agentem | [ubuntu.com](https://ubuntu.com/server) |
| **cloud-init** | příprava nové VM při prvním startu (Docker, firewall, Tailscale, uživatel `deploy`) | standard všech cloudů; nová VM je hotová bez ručních kroků | [docs](https://cloudinit.readthedocs.io/) |
| **systemd** (timer + service) | noční spouštění zálohy, i dodatečně po výpadku (`Persistent=true`) | součást Ubuntu, spolehlivější než cron | [docs](https://www.freedesktop.org/software/systemd/man/latest/systemd.timer.html) |
| **iptables** / **netfilter-persistent** | firewall na VM – povolené 80/443 | druhá vrstva vedle security listu cloudu | [Ubuntu wiki](https://help.ubuntu.com/community/IptablesHowTo) |
| **OpenSSH** | správa VM a nasazování (vynucený příkaz pro uživatele `deploy`) | standard; přístup jen přes Tailscale | [openssh.com](https://www.openssh.com/) |
| **Oracle Cloud Agent** | posílá metriky CPU a paměti do OCI Monitoring | předinstalovaný v obrazu; podklad pro alarmy | [docs](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/manage-plugins.htm) |

## Nástroje správce (PC)

| Co | K čemu | Odkazy |
| --- | --- | --- |
| **.NET SDK** (verze z `global.json`) | build, testy, `dotnet run` | [download](https://dotnet.microsoft.com/download) |
| **Docker Desktop** | RabbitMQ pro Aspire, lokální Compose | [docker.com](https://www.docker.com/products/docker-desktop/) |
| **OpenTofu** | infrastruktura jako kód ([proč](../nasazeni/infrastruktura-jako-kod.md)) | [opentofu.org](https://opentofu.org/) |
| **Git** + **GitHub CLI** (`gh`) | verzování, PR | [git-scm.com](https://git-scm.com/), [cli.github.com](https://cli.github.com/) |
| **Tailscale** klient | SSH na VM přes soukromou síť | [tailscale.com](https://tailscale.com/download) |
| **gitleaks** | kontrola historie gitu na tajné hodnoty | [GitHub](https://github.com/gitleaks/gitleaks) |
