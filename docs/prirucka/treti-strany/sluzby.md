---
title: Online služby
summary: Oracle Cloud, GitHub, Tailscale, DuckDNS, Let's Encrypt, UptimeRobot, healthchecks.io, Gmail, přihlášení přes Google a Stripe (testovací režim) – všechny zdarma.
order: 3
---

> **V jedné větě:** MiniShop využívá řadu online služeb (tabulka níže) a všechny jsou ve **free tarifu** – výsledná cena
> provozu je **0 Kč měsíčně**.

| Služba | K čemu ji používáme | Proč právě ona | Tarif | Odkazy |
| --- | --- | --- | --- | --- |
| **Oracle Cloud Infrastructure** | VM (Compute A1), síť (VCN), Object Storage (zálohy, state OpenTofu), Monitoring + Notifications (alarmy), IAM, rozpočty | jediný cloud, kde je zdarma natrvalo VM s dostatkem paměti; skutečný cloud s IAM a sítí ([detail](../nasazeni/cloud-oci.md)) | Always Free (účet Pay As You Go) | [oracle.com/cloud/free](https://www.oracle.com/cloud/free/), [docs](https://docs.oracle.com/en-us/iaas/Content/home.htm) |
| **GitHub** | repozitář, pull requesty, ochrana větve, secret scanning, Dependabot | standard pro open-source; veřejný repozitář má vše zdarma ([detail](../github/repozitar.md)) | Free | [github.com](https://github.com/) |
| **GitHub Actions** | CI (build, testy, image) a CD (nasazení) | integrované s repozitářem, workflow jako kód ([detail](../github/ci.md)) | zdarma pro veřejné repozitáře | [docs](https://docs.github.com/actions) |
| **GitHub Container Registry (GHCR)** | úložiště Docker image `minishop-web` a `minishop-inventory-api` | u kódu, přihlášení přes `GITHUB_TOKEN`, veřejné image bez limitu | Free | [docs](https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry) |
| **Tailscale** | soukromá síť (WireGuard) pro SSH na VM – pro správce i runner CI | port 22 nemusí být otevřený do internetu; bez vlastního VPN serveru ([detail](../github/cd.md)) | Personal (free) | [tailscale.com](https://tailscale.com/) |
| **Wedos** | registrace domény `minishop-tichy.cz` a její DNS (záznamy A pro `@` a `dashboard`) | český registrátor, DNS hosting k doméně zdarma; vlastní doména nemá špatnou pověst sdílených domén ([detail](../nasazeni/https-a-pristup.md)) | placená doména (roční poplatek), DNS free | [wedos.cz](https://www.wedos.cz/), [znalostní báze](https://kb.wedos.com/) |
| **DuckDNS** | původní adresa `minishop-tichy.duckdns.org` – od cloud kroku 13 jen přesměrování na `minishop-tichy.cz` | zdarma, hotové za minutu; antiviry ji ale často blokují ([detail](../nasazeni/https-a-pristup.md)) | free | [duckdns.org](https://www.duckdns.org/) |
| **Let's Encrypt** | HTTPS certifikáty (vydává a obnovuje přes Caddy) | zdarma, automaticky, důvěryhodné ve všech prohlížečích | free | [letsencrypt.org](https://letsencrypt.org/) |
| **UptimeRobot** | každých 5 minut zvenku kontroluje `/health` a `/health/inventory`, e-mail při výpadku | aktivní kontrola zvenku pozná i výpadek sítě, DNS či certifikátu ([detail](../nasazeni/monitoring.md)) | Free | [uptimerobot.com](https://uptimerobot.com/) |
| **healthchecks.io** | hlídá, že noční záloha proběhla (*dead man's switch*) | ozve se, když se záloha **neozve** ([detail](../nasazeni/zalohy.md)) | Hobbyist (free) | [healthchecks.io](https://healthchecks.io/) |
| **Gmail (SMTP)** | odesílání e-mailů z produkce – potvrzení e-mailu, nové heslo, stav objednávky; účet `ftichy.gemini@gmail.com` s heslem aplikace | zdarma (~500 e-mailů/den), dobrá doručitelnost i bez vlastní domény ([detail](../architektura/emaily.md)) | free | [heslo aplikace](https://support.google.com/accounts/answer/185833) |
| **Google Cloud (OAuth klient)** | přihlášení přes Google (účty krok 10) – projekt MiniShop, *OAuth consent screen* a klient typu *Web application* s adresou návratu `/signin-google` | většina lidí účet Google má; jen rozsahy `openid email profile`, ověření aplikace Googlem pro ně není potřeba ([detail](../architektura/moduly/accounts.md#přihlášení-přes-google)) | free | [Google Cloud Console](https://console.cloud.google.com/apis/credentials), [docs](https://developers.google.com/identity/protocols/oauth2/web-server) |
| **Stripe** (testovací režim) | platební brána (e-shop krok 16) – stránka pro zadání karty (Checkout), webhook s výsledkem, vrácení peněz; testovací karta `4242 4242 4242 4242` | testovací režim hned po registraci (bez firmy a ověření), skvělá dokumentace, Stripe CLI doručí webhooky i na localhost; číslo karty MiniShop nevidí ([detail](../architektura/moduly/payments.md#stripe-e-shop-krok-16)) | test mode free (skutečné platby se nepřijímají) | [dashboard.stripe.com](https://dashboard.stripe.com/test/dashboard), [docs](https://docs.stripe.com/) |

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
| Stripe | `PAYMENTS_GATEWAY=Fake` v `production.env` vrátí simulovanou bránu; jiná brána (GoPay, Comgate) = nová implementace `IPaymentGateway` + webhook |
| Google (přihlášení) | prázdné `GOOGLE_CLIENT_ID` tlačítko skryje; účty dál fungují heslem (účet bez hesla si ho nastaví přes *Zapomenuté heslo*) |
