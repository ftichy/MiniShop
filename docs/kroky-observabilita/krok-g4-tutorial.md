# Observabilita krok G4: tutoriál „Grafana krok za krokem“

Plán: [Observabilita-roadmap.md](../Observabilita-roadmap.md). Cíl: aby Grafanu uměl použít i úplný začátečník – ne obecný
návod, ale **skutečné situace z provozu MiniShopu**: kam kliknout, co napsat, co uvidím a co to znamená.

## Co vzniklo

Stránka příručky [Grafana krok za krokem](../prirucka/nasazeni/grafana-tutorial.md) (`/Docs/nasazeni/grafana-tutorial`):

1. **Než začnete** – 5 pojmů (trasa, span, log, metrika, štítek) s přirovnáním, 4 místa v Grafaně (Explore, Drilldown,
   Dashboards, Alerting), 3 zdroje dat (`-traces`/TraceQL, `-logs`/LogQL, `-prom`/PromQL), časový rozsah.
2. **Případ 1: „Zákazník píše, že objednávka ve 14:20 nedorazila“** – e-mail → ID účtu (`/Admin/Users/Detail/{id}`) a číslo
   objednávky → trasy podle `enduser.id` → příběh objednávky podle `minishop.order.id` → jak číst vodopád (publish/process,
   červený span = chyba) → logy k trase → tabulka „co vidím → co to znamená → co dál“.
3. **Případ 2: přišel alarm** – e-mail → tabulka alarmů → `/Admin/Messages` / dashboard → *Resolved*, pauza při údržbě.
4. **Případ 3: web je pomalý** – p95 v dashboardu → TraceQL `duration > 500ms` → nejdelší pruh (SQL, sklad, kód).
5. **Případ 4: po nasazení** – `target_info` ukáže výměnu kopií, porovnání před/po, *Split*.
6. **Případ 5: hledání v logu** – Drilldown Logs, LogQL `|=`, `!=`, Trace ID z logu na trasu.
7. **Případ 6: graf ukazuje nesmysl** – skutečná diagnóza z G3 (`target_info` = 1 řada pro 2 kopie Webu), Query inspector, Table.
8. **Případ 7: kolik objednávek a jak rychle** – PromQL s vysvětlením `increase`, `rate`, `sum by`, `histogram_quantile`.
9. **Tipy a tahák** – uložení dotazu, sdílení odkazu, z panelu do Explore, Ask Assistant, retence 14 dní.

## Změněné soubory

`docs/prirucka/nasazeni/grafana-tutorial.md` (nová), `nasazeni/monitoring.md` (odkaz), `historie.md`, roadmapa, CLAUDE.md.

## Rozhodnutí

- **V příručce, ne jako samostatný dokument** – je u zbytku provozní dokumentace, hlídá ji `DocumentationTests` (odkazy) a nasazuje
  se s Webem (`/Docs`).
- **Situace místo popisu funkcí** – začátečník přichází s otázkou („proběhla objednávka?“), ne s „chci použít TraceQL“.
- **Přesná jména z MiniShopu** (atributy `enduser.id`, `minishop.order.id`, štítek `service_name`, metriky `minishop_*`) –
  změna jmen v kódu = upravit i tutoriál (pravidlo v CLAUDE.md).
- **Bez e-mailů v Grafaně** – tutoriál začíná v administraci MiniShopu (e-mail → ID účtu), protože osobní údaje do telemetrie nejdou.

## Známá omezení

- Snímky obrazovky v návodu nejsou – rozhraní Grafana Cloud se často mění; popisy jsou podle textů tlačítek (anglicky).
- Ověřené nad produkcí při psaní: `span.url.path`, `target_info`, metriky `minishop_*` a dashboard. Ostatní dotazy
  (`enduser.id`, `minishop.order.id`, LogQL s `trace_id`, `kind = server && duration`) vychází z kódu – ověřit při prvním použití.

## Jak ověřit

- `/Docs/nasazeni/grafana-tutorial` se zobrazí v sekci Nasazení a provoz; `DocumentationTests` zelené.
- Projít případ 1 se skutečnou objednávkou (vlastní testovací účet) a případ 5 s textem „Vedoucí“.

## Vyzkoušejte sami

1. Udělejte objednávku na dobírku a najděte ji podle případu 1 – od ID účtu po e-mail o potvrzení ve vodopádu.
2. Uložte si dotaz `{ span.minishop.order.id = … }` do *Saved queries*.
3. Zkuste Ask Assistant: „ukaž chyby webu za poslední hodinu“ a porovnejte jeho dotaz s tahákem.
