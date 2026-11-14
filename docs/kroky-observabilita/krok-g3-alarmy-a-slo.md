# Observabilita krok G3: alarmy a SLO

Plán: [Observabilita-roadmap.md](../Observabilita-roadmap.md). Cíl: na problém nemusí nikdo koukat – Grafana pošle e-mail.
A měřitelný slib spolehlivosti: **99 % objednávek potvrzeno do 30 s**.

## Jak to funguje

Přirovnání: G2 je palubní deska (musíte se dívat), G3 kontrolky, které se rozsvítí samy a zavolají vás. SLO je jako měsíční
limit dat v telefonu – „1 % pomalých objednávek“ je rozpočet, graf ukazuje, kolik ho zbývá.

1. **Pravidla** v `deploy/grafana/minishop-alarmy.yaml` – standardní formát pravidel Prometheus (skupiny `minishop-zpravy`,
   `minishop-objednavky`, `minishop-provoz`, 9 alarmů). Každé má `for` (jak dlouho musí podmínka platit), české `summary`,
   `description` („co dělat“) a `runbook_url` na příručku.
2. **Import ručně** do Grafana Cloud: *Alerting → Alert rules → Import to Grafana-managed rules → Import from file* → zdroj `-prom`,
   složka MiniShop. Grafana z nich udělá vlastní („Grafana-managed“) alarmy – bez tokenu se zápisem.
3. **E-mail**: výchozí kontakt `grafana-default-email` s adresou správce, výchozí politika (připomínka po 4 h, *Resolved* při vyřešení).
4. **SLO**: podíl `minishop_order_confirmation_duration_seconds_bucket{le="30"}` / `…_count` za 7 dní (bez karty – u ní se počítá
   i zadávání karty zákazníkem). Alarm `MiniShopConfirmationSlo` pod 99 %, v dashboardu řádek SLO (podíl, zbývající rozpočet, počet pomalých).
5. **CI**: krok *Kontrola alarmů Grafany (promtool)* v jobu *Build a testy* + `GrafanaConfigTests` (struktura dashboardu a alarmů).

## Nové soubory

- `deploy/grafana/minishop-alarmy.yaml` – 9 alarmů
- `tests/MiniShop.Tests/Architecture/GrafanaConfigTests.cs`
- tento souhrn

## Změněné soubory

`deploy/grafana/minishop-zpravy-a-sagy.json` (řádek SLO), `.github/workflows/ci.yml` (promtool), příručka `nasazeni/monitoring.md`
(alarmy a co dělat), `github/ci.md`, `treti-strany/beh.md` (promtool), `historie.md`, roadmapa, CLAUDE.md.

## Rozhodnutí

- **Formát Prometheus + import přes UI, ne API:** žádný další token se zápisem do Grafany; soubor jde zkontrolovat `promtool`em.
- **Jména alarmů bez mezer a diakritiky** (`MiniShopDeadLetter`…) – některé nástroje je kontrolují jako jméno metriky; česky je `summary`.
- **SLO za 7 dní, ne 30:** Grafana Cloud Free drží metriky 14 dní.
- **SLO a p95 bez karty:** doba u karty závisí na zákazníkovi, ne na MiniShopu.
- **Prahy pro malý provoz** (`for` 5–15 min, backlog > 50, 5xx > 3 za 10 min) – ladit podle skutečnosti (PR + nový import).
- **Bez aplikace SLO v Grafaně:** nastavuje se klikáním a nejde snadno verzovat; dashboard + alarm pokryjí totéž (ukázka v tutoriálu G4).
- **UptimeRobot a alarmy OCI zůstávají:** hlídají zvenku – i výpadek, kdy nechodí nic (ani telemetrie do Grafany).

## Známá omezení

- Při velmi malém provozu je p95 i SLO citlivé na jednu pomalou objednávku (1 ze 30 = 3 %).
- `MiniShopTelemetryMissing` se ozve až po 10 min; rychlejší je UptimeRobot (5 min).
- Alarmy stavu (dead-letter, poison…) mají zpoždění až ~2 min (měření 30 s, export 1 min) + `for`.
- Změna pravidel = nový import (soubor v gitu je zdroj pravdy, změny v UI by se importem přepsaly).

## Jak ověřit

- CI: krok promtool zelený, `GrafanaConfigTests`.
- Grafana: *Contact points* → **Test** → e-mail dorazí; *Alert rules* → složka MiniShop, 9 pravidel ve stavu *Normal*
  (`MiniShopConfirmationSlo` a `MiniShopSlowConfirmation` mohou být *No data*, dokud není objednávka).
- Dashboard (nový import): řádek SLO po objednávce ukáže podíl a rozpočet.

## Vyzkoušejte sami

1. Otevřete alarm `MiniShopDeadLetter` → *View* → graf dotazu: uvidíte, kdy by se rozsvítil.
2. Pozastavte jeden alarm (*Pause evaluation*) a zase ho spusťte – tak se alarm vypne při plánované údržbě.
3. V *Notification policies* si prohlédněte, jak dlouho Grafana čeká, než pošle připomínku.
