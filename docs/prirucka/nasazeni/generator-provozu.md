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
   Vypíše e-maily a **klíč 2FA skladníka**. Příkaz je idempotentní – existující účty přeskočí.
2. **Na svém PC spusť generátor** (heslo a klíč jen proměnnými prostředí, ne v příkazu):

   ```powershell
   $env:LOADTEST_PASSWORD = "…"; $env:LOADTEST_STAFF_TOTP_KEY = "…"
   dotnet run --project tools/MiniShop.TrafficGenerator -- --url https://minishop-tichy.cz --customers 4 --rounds 3 --pause 5
   ```

3. **Sleduj Grafanu** (dashboard *MiniShop – zprávy a ságy*, trasy podle `enduser.id` nebo `minishop.order.id`,
   viz [tutoriál Grafany](grafana-tutorial.md)).

Parametry: `--customers` (1–20), `--rounds` (kolik scénářů každý zákazník projde), `--pause` (průměrná pauza mezi kroky v s;
pauzy jsou náhodné ±50 %, aby provoz nevypadal jako špička). Lokálně stačí `--url https://localhost:7180`.

## Scénáře

Zákazníci jedou paralelně, scénáře se střídají podle čísla zákazníka a kola:

- **doručená** – výloha → košík (1–2 náhodné produkty) → pokladna (kurýr, dobírka) → potvrzení ságou → skladník zabalí,
  předá dopravci (číslo zásilky přidělí simulovaný dopravce – e-shop krok 26B) a doručí;
- **zrušená** – totéž až po potvrzení, pak zákazník objednávku zruší (uvolnění rezervace, dobropis nevzniká – nic nebylo zaplaceno);
- **vrácená** – doručená + žádost o vrácení + skladník vratku převezme (zboží zpět na sklad, dobropis).

Skladník je jedna relace a dělá jednu věc po druhé (jako člověk), takže se objednávky zákazníků na skladníka řadí.
Na konci je tabulka: kdo, jaký scénář, číslo objednávky, **čas do potvrzení** (sága ~4–8 s) a výsledek. Chyba = návratový kód 1.

## Omezení

- Formuláře účtu mají limit 10 odeslání za minutu z jedné IP (`RateLimiting:AccountForms`); jedno spuštění přihlásí
  zákazníky + skladníka jen jednou, ale opakované spuštění hned za sebou může dostat 429.
- Výloha musí mít produkty skladem; generátor nic neobsazuje ani nezakládá.
- Generátor nezná prohlížeč – HTML formuláře čte jednoduchým rozborem. Změna názvu pole formuláře (pokladna, expedice) ho
  rozbije; hlásí to jako chybu scénáře.
- Účty se nemažou (po práci je zablokuj v `/Admin/Users` nebo je nech na další běh).
