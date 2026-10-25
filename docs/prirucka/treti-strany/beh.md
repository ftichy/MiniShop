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
| **Caddy** (`caddy:2`) | reverse proxy, automatické HTTPS, basic auth | certifikát Let's Encrypt bez konfigurace ([proč](../nasazeni/https-a-pristup.md)) | [caddyserver.com](https://caddyserver.com/) |
| **Aspire dashboard** (`mcr.microsoft.com/dotnet/aspire-dashboard`) | trasy, logy, metriky v cloudu | stejný nástroj jako lokálně, jeden kontejner ([proč](../nasazeni/monitoring.md)) | [docs](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone) |
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
