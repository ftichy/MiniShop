---
title: Generátor provozu
summary: Virtuální zákazníci a skladník, kteří z tvého PC projdou nákup, zrušení a vratku – aby měla Grafana co ukazovat.
order: 9
---

> **V jedné větě:** `tools/MiniShop.TrafficGenerator` je konzolová aplikace, která přes HTTP (bez prohlížeče) předvede
> několik zákazníků a jednoho skladníka nad běžícím MiniShopem – objednávky, expedici, zrušení a vratky na dobírku –
> a ty se objeví v Grafaně jako skutečné trasy, ságy a metriky.

Přirovnání: figuranti ve zkušebním obchodě. Chodí, nakupují, vracejí zboží, ale mají falešné jméno i adresu, takže
jim nic reálného nedorazí.

## Co je „bezpečné“

| Riziko | Jak je ošetřené |
| --- | --- |
| Skutečné e-maily | Testovací účty mají adresu `loadtest-N@loadtest.minishop.invalid`. Doména `.invalid` je podle RFC 2606 vyhrazená pro neexistující adresy a `SmtpEmailSender` na ni **nic neodešle** (do logu jen předmět). |
| Platba kartou / Stripe | Generátor platí **jen dobírkou** (platba se přeskočí). |
| Push | Testovací účty se k oznámením nepřihlásí. |
| Potvrzení e-mailu | Účty zakládá příkaz na serveru rovnou s potvrzeným e-mailem (nikdo není k dispozici, aby klikl na odkaz). |
| 2FA | Skladník má zapnuté 2FA; klíč vypíše příkaz, generátor si kód spočítá sám (stejně jako telefon). Má **jedinou roli Sklad a expedice** (ne správce). |

Testovací objednávky **zůstanou** v produkčních datech (Přehled tržeb, faktury – i číselná řada, hodnocení). Je to záměr
z issue #155; před prvním během proto ruční záloha `sudo systemctl start minishop-backup`.

## Postup

1. **Na serveru založ účty** (heslo se píše do terminálu, nikam se neukládá):
   `docker compose exec web dotnet MiniShop.Web.dll create-loadtest-users --count 4`.
   Vypíše e-maily a **klíč 2FA skladníka**. Příkaz je idempotentní – chybějící účty založí, **existujícím testovacím
   účtům nastaví zadané heslo** a zruší blokaci (klíč 2FA skladníka se nemění). Po každém spuštění tak mají všechny
   účty stejné heslo – i když jste se napoprvé přepsali. Heslo, které nesplní pravidla (min. 10 znaků), nezaloží nic.
2. **Na svém PC spusť generátor** (heslo a klíč jen proměnnými prostředí, ne v příkazu):

   ```powershell
   $env:LOADTEST_PASSWORD = "…"; $env:LOADTEST_STAFF_TOTP_KEY = "…"
   dotnet run --project tools/MiniShop.TrafficGenerator -- --url https://minishop-tichy.cz --customers 4 --rounds 3 --pause 5
   ```

3. **Sleduj Grafanu** (dashboard *MiniShop – zprávy a ságy*, trasy podle `enduser.id` nebo `minishop.order.id`,
   viz [tutoriál Grafany](grafana-tutorial.md)).

Parametry: `--customers` (1–20), `--rounds` (kolik scénářů každý zákazník projde), `--duration` (místo kol délka běhu
v minutách), `--pause` (průměrná pauza mezi kroky v s; pauzy jsou náhodné ±50 %, aby provoz nevypadal jako špička).
Lokálně stačí `--url https://localhost:7180`.

## Dlouhý test (1–2 hodiny, 10–20 zákazníků)

```powershell
dotnet run --project tools/MiniShop.TrafficGenerator -- --url https://minishop-tichy.cz --customers 20 --duration 90 --pause 5
```

- Účty: `create-loadtest-users --count 20`. Účty z dřívějšího běhu (např. 1–4) dostanou stejné heslo jako nové –
  22. 11. 2026 se jinak zákazníci 5–20 nepřihlásili. Před během ruční záloha a **dost zboží skladem** – 20 zákazníků za 90 min
  udělá řádově stovky objednávek; zrušení a vratky zboží vrátí, doručené ne.
- **Přihlášení se rozloží** (~8 za minutu, zákazník 20 začne po 2,5 min) – formuláře účtu mají limit 10 za minutu z jedné IP.
  Když přesto přijde 429, generátor počká 65 s a zkusí to znovu (nejvýš 3×).
- S `--duration` zákazník spouští další scénáře, dokud čas neuplyne; rozpracovaný dokončí (konec tedy o pár minut později).
  Po každém scénáři vypíše řádek `[čas] zákazník N: scénář objednávka – OK`.
- **Ctrl+C** jednou = žádné nové scénáře, rozpracované se dokončí a vypíše se souhrn; podruhé = konec hned.
- Na konci tabulka, počty scénářů podle výsledku a **doba potvrzení p50/p95** (srovnejte se stránkou Rychlost vyřízení
  a s dashboardem v Grafaně).
- **PC se během běhu neuspí** (Windows, obrazovka zůstane svítit) – uspání odpojí síť a generátor by hlásil chyby
  („An error occurred while sending the request“, „nedostala do stavu“), které nezpůsobil MiniShop. Víko nezavírat.
- Skladník je jeden a vyřizuje objednávky po jedné – u 20 zákazníků se před ním tvoří fronta (delší doba expedice je očekávaná).

## Scénáře

Zákazníci jedou paralelně, scénáře se střídají podle čísla zákazníka a kola:

- **doručená** – výloha → košík (1–2 náhodné produkty) → pokladna (kurýr, dobírka) → potvrzení ságou → skladník zabalí,
  předá dopravci (číslo zásilky přidělí simulovaný dopravce – e-shop krok 26B), doručení ohlásí dopravce sám (26C, ~2 min) – asi každou desátou zásilku dopravce nedoručí a vrátí (26D, ~4 min), pak má scénář ve výsledku „→ vráceno dopravcem“;
- **zrušená** – totéž až po potvrzení, pak zákazník objednávku zruší (uvolnění rezervace, dobropis nevzniká – nic nebylo zaplaceno);
- **vrácená** – doručená + žádost o vrácení + skladník vratku převezme (zboží zpět na sklad, dobropis).

Skladník je jedna relace a dělá jednu věc po druhé (jako člověk), takže se objednávky zákazníků na skladníka řadí.
Na konci je tabulka: kdo, jaký scénář, číslo objednávky, **čas do potvrzení** (sága ~4–8 s) a výsledek. Chyba = návratový kód 1.

## Omezení

- Formuláře účtu mají limit 10 odeslání za minutu z jedné IP (`RateLimiting:AccountForms`); generátor přihlášení
  rozkládá a 429 přečká, opakované spuštění hned za sebou ale jen zpomalí start.
- Výloha musí mít produkty skladem; generátor nic neobsazuje ani nezakládá.
- Generátor nezná prohlížeč – HTML formuláře čte jednoduchým rozborem. Změna názvu pole formuláře (pokladna, expedice) ho
  rozbije; hlásí to jako chybu scénáře.
- Účty se nemažou (po práci je zablokuj v `/Admin/Users` nebo je nech na další běh); další `create-loadtest-users`
  blokaci zase zruší.
