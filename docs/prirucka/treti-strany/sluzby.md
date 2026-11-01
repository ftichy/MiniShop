---
title: Online služby
summary: Oracle Cloud, GitHub, Tailscale, DuckDNS, Let's Encrypt, UptimeRobot, healthchecks.io a Gmail – všechny zdarma.
order: 3
---

> **V jedné větě:** MiniShop využívá sedm online služeb a všechny jsou ve **free tarifu** – výsledná cena
> provozu je **0 Kč měsíčně**.

| Služba | K čemu ji používáme | Proč právě ona | Tarif | Odkazy |
| --- | --- | --- | --- | --- |
| **Oracle Cloud Infrastructure** | VM (Compute A1), síť (VCN), Object Storage (zálohy, state OpenTofu), Monitoring + Notifications (alarmy), IAM, rozpočty | jediný cloud, kde je zdarma natrvalo VM s dostatkem paměti; skutečný cloud s IAM a sítí ([detail](../nasazeni/cloud-oci.md)) | Always Free (účet Pay As You Go) | [oracle.com/cloud/free](https://www.oracle.com/cloud/free/), [docs](https://docs.oracle.com/en-us/iaas/Content/home.htm) |
| **GitHub** | repozitář, pull requesty, ochrana větve, secret scanning, Dependabot | standard pro open-source; veřejný repozitář má vše zdarma ([detail](../github/repozitar.md)) | Free | [github.com](https://github.com/) |
| **GitHub Actions** | CI (build, testy, image) a CD (nasazení) | integrované s repozitářem, workflow jako kód ([detail](../github/ci.md)) | zdarma pro veřejné repozitáře | [docs](https://docs.github.com/actions) |
| **GitHub Container Registry (GHCR)** | úložiště Docker image `minishop-web` a `minishop-inventory-api` | u kódu, přihlášení přes `GITHUB_TOKEN`, veřejné image bez limitu | Free | [docs](https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry) |
| **Tailscale** | soukromá síť (WireGuard) pro SSH na VM – pro správce i runner CI | port 22 nemusí být otevřený do internetu; bez vlastního VPN serveru ([detail](../github/cd.md)) | Personal (free) | [tailscale.com](https://tailscale.com/) |
| **DuckDNS** | doména `minishop-tichy.duckdns.org` (+ subdomény) | zdarma, hotové za minutu ([detail](../nasazeni/https-a-pristup.md)) | free | [duckdns.org](https://www.duckdns.org/) |
| **Let's Encrypt** | HTTPS certifikáty (vydává a obnovuje přes Caddy) | zdarma, automaticky, důvěryhodné ve všech prohlížečích | free | [letsencrypt.org](https://letsencrypt.org/) |
| **UptimeRobot** | každých 5 minut zvenku kontroluje `/health` a `/health/inventory`, e-mail při výpadku | aktivní kontrola zvenku pozná i výpadek sítě, DNS či certifikátu ([detail](../nasazeni/monitoring.md)) | Free | [uptimerobot.com](https://uptimerobot.com/) |
| **healthchecks.io** | hlídá, že noční záloha proběhla (*dead man's switch*) | ozve se, když se záloha **neozve** ([detail](../nasazeni/zalohy.md)) | Hobbyist (free) | [healthchecks.io](https://healthchecks.io/) |
| **Gmail (SMTP)** | odesílání e-mailů z produkce – potvrzení e-mailu, nové heslo, stav objednávky; účet `ftichy.gemini@gmail.com` s heslem aplikace | zdarma (~500 e-mailů/den), dobrá doručitelnost i bez vlastní domény ([detail](../architektura/emaily.md)) | free | [heslo aplikace](https://support.google.com/accounts/answer/185833) |

## Účty a jejich ochrana

Každá služba má vlastní účet chráněný heslem a **MFA** (dvoufázovým ověřením). Přístupové klíče, které používají
automaty (OAuth klient Tailscale, SSH klíč nasazení, API klíč OCI), jsou popsané v [Secrets](../github/secrets.md).

## Co kdyby služba skončila

| Služba | Náhrada |
| --- | --- |
| Oracle Cloud | libovolná VPS (Hetzner, OVH, Netcup) – Compose, Caddy, CI/CD platí beze změny, mění se jen `infra/` |
| DuckDNS | vlastní doména – nový DNS záznam a `DOMAIN` v `production.env` |
| Tailscale | dočasně SSH z vlastní IP v security listu; nebo jiná VPN (WireGuard) |
| UptimeRobot, healthchecks.io | jiný free monitoring (např. Better Stack); kód se nemění |
| Gmail | jiný SMTP (Brevo, Mailjet, OCI Email Delivery) – jen `EMAIL_*` v `production.env` a secret `SMTP_PASSWORD` |
