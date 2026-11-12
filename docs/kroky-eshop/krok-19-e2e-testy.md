# E-shop krok 19 – E2E testy v prohlížeči (Playwright)

Issue #92. Doteď nikdo automaticky neprošel e-shop v prohlížeči jako zákazník. Nový projekt `tests/MiniShop.E2E` to dělá v CI.

## Jak to funguje

Přirovnání: ostatní testy kontrolují každý díl auta zvlášť, E2E test je zkušební jízda celým autem.

1. Job `e2e` v `ci.yml` postaví image Webu a skladu a spustí je přes `deploy/compose.yaml` + `deploy/compose.e2e.yaml`
   (PostgreSQL, RabbitMQ, Inventory.Api, Web, **Mailpit**). Hesla se generují za běhu, admin vznikne příkazem `create-admin`.
2. Playwright (Chromium) projde tři scénáře: **nákup kartou**, **expedice** (admin zabalí → odešle → doručeno), **mobil** (390×844).
3. Potvrzovací odkaz z e-mailu test přečte přes API Mailpitu (`MailpitClient`) – v aplikaci žádná zadní vrátka.
4. Čeká se opakovaným načtením stránky s limitem 40 s (`ShopFlow.ReloadUntilAsync`) – sága a katalog jsou asynchronní.
5. Při selhání artefakt `e2e-artifacts` (Playwright trace, snímek stránky, logy služeb).

## Nové soubory
- `tests/MiniShop.E2E/`: `E2EEnvironment` (+ `[E2EFact]`, `[E2EAdminFact]` – bez `E2E_BASE_URL` přeskočí), `E2ETestBase`, `ShopFlow`,
  `MailpitClient`, `NakupKartouTests`, `ExpediceTests`, `MobilTests`
- `deploy/compose.e2e.yaml` – Mailpit + zvednutý limit formulářů účtu (jen pro E2E, na VM se nenačítá)
- `.github/workflows/ci.yml` – nový job `e2e` („E2E testy“)

## Změněné soubory
`MiniShop.slnx`, příručka (`github/ci.md`, `treti-strany/knihovny.md`, `treti-strany/beh.md`), `CLAUDE.md`.

## Rozhodnutí
- Projekt je v `MiniShop.slnx` (build ho ověří), ale bez `E2E_BASE_URL` se testy přeskočí – `dotnet test MiniShop.slnx` nezčervená.
- Job **není povinný check** a nasazení neblokuje; název „Build a testy“ se nemění.
- Selektory přes `data-track` a ID polí; produkční kód se nemění.
- Image se staví z Dockerfile s GHA cache (image z GHCR pro PR neexistují).
- Mailpit `axllent/mailpit:v1.31.4` (stejně jako v AppHostu), heslo admina z roury, v logu zamaskované.
- Scénář expedice používá dobírku (platbu kartou pokrývá první scénář).

## Známá omezení
- Scénáře jsem v tomto prostředí **nemohl spustit** (bez Dockeru / prohlížeče) – ověří je až první běh CI; selektory vycházejí z Razor stránek.
- Stripe, vratky, kupony a faktury E2E nepokrývá.

## Jak ověřit
Viz `docs/prirucka/github/ci.md` (sekce E2E testy v prohlížeči) – lokálně přes `compose.e2e.yaml` a `dotnet test tests/MiniShop.E2E`.

## Vyzkoušejte sami
Rozbijte v pokladně `id` pole `Input_Street` a ověřte, že job „E2E testy“ spadne a v artefaktu je trace i snímek.

## Jak krok vznikl

Agent Claude z issue #92 (analýza → odpovědi na 5 otázek → `@claude implementuj`). Scénáře nemohl spustit (runner agenta
nemá povolený Docker) a job nemohl dát do `ci.yml` (aplikace bez oprávnění *workflows*). Ručně doplněno v PR #93:
- job „E2E testy“ přesunut do `.github/workflows/ci.yml`,
- `Email__UseTls=false` v `compose.e2e.yaml` (Mailpit neumí STARTTLS),
- po platbě se čeká na hlášku „Platba proběhla“ (ne „Zaplaceno“), mobil: druhé klepnutí na ☰ až po dokončení animace.

Druhý běh v CI: všechny 3 scénáře zelené (~3 min).
