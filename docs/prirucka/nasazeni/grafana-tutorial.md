---
title: Grafana krok za krokem
summary: Tutoriál pro začátečníka – jak v Grafaně ověřit, že objednávka zákazníka proběhla v pořádku, najít chybu, zjistit, proč je web pomalý, a co dělat, když přijde alarm.
order: 8
---

> **V jedné větě:** Grafana je „černá skříňka“ MiniShopu – zaznamenává 14 dní zpětně, co se dělo (trasy, logy, metriky).
> Tahle stránka ukazuje na skutečných situacích, **kam kliknout a co napsat**, abyste z ní dostali odpověď.

Adresa: **https://patientjaguar2071.grafana.net** (přihlášení účtem Grafana Cloud). Jak se do Grafany data dostávají,
popisuje [Monitoring a logy](monitoring.md); co se měří, [Observabilita](../eda/observabilita.md).

## Než začnete: 5 pojmů a 4 místa

| Pojem | Co to je | Přirovnání |
| --- | --- | --- |
| **Trasa** (trace) | celý příběh jednoho požadavku nebo zprávy napříč službami – má vlastní **Trace ID** | podací číslo zásilky – podle něj najdete celou cestu balíku |
| **Span** | jeden krok trasy (HTTP požadavek, SQL dotaz, odeslání zprávy, zpracování ve skladu) s časem začátku a délkou | jedna zastávka balíku (třídírna, auto, doručovatel) |
| **Log** | řádek textu, který aplikace zapsala („Objednávka 42 potvrzena“); má úroveň (Information, Warning, Error) a Trace ID | zápis v deníku směny |
| **Metrika** | číslo v čase (požadavky za minutu, čekající zprávy) | tachometr |
| **Štítek** (label / atribut) | vlastnost, podle které se filtruje: `service_name="web"`, `minishop.order.id=42` | nálepka na krabici |

| Místo v Grafaně (menu ☰ vlevo nahoře) | K čemu |
| --- | --- |
| **Explore** | ruční hledání – vyberete zdroj dat a napíšete dotaz |
| **Drilldown** (Traces, Logs, Metrics) | klikací průzkum bez psaní dotazů – dobrý začátek |
| **Dashboards → MiniShop – zprávy a ságy** | palubní deska: je teď vše v pořádku? |
| **Alerting → Alert rules** (složka MiniShop) | alarmy, které posílají e-mail |

**Tři zdroje dat** (rozbalovací seznam vlevo nahoře v Explore) – podle koncovky:

| Zdroj | Co v něm je | Jazyk dotazů |
| --- | --- | --- |
| `grafanacloud-patientjaguar2071-traces` | trasy (Tempo) | **TraceQL** – `{ podmínka }` |
| `grafanacloud-patientjaguar2071-logs` | logy (Loki) | **LogQL** – `{štítek="hodnota"} \|= "text"` |
| `grafanacloud-patientjaguar2071-prom` | metriky (Prometheus/Mimir) | **PromQL** – `jméno_metriky{štítek="hodnota"}` |

**Časový rozsah** je vpravo nahoře (ikona hodin, např. *Last 1 hour*). Grafana ukazuje čas **v časovém pásmu prohlížeče**
(u vás český čas) – stejně jako stránky MiniShopu. Pro konkrétní okamžik klikněte na rozsah a vyplňte **From** a **To**
(např. `2026-11-14 14:05:00` a `2026-11-14 14:35:00`) → **Apply time range**.

**Dotaz v Explore:** u metrik a logů přepněte vpravo z *Builder* na **Code** (jedno textové pole), u tras zvolte *Query type*
**TraceQL**. Spuštění: **Run query** vpravo nahoře (nebo Shift+Enter).

## Případ 1: „Zákazník píše, že objednávka ve 14:20 nedorazila – proběhlo vše v pořádku?“

Máte **čas** a **uživatele** (e-mail ze stížnosti). Cíl: zjistit, co se s jeho objednávkou dělo, a kde se případně zasekla.

**Krok 1 – z e-mailu na ID účtu a číslo objednávky (MiniShop, ne Grafana).**
V Grafaně nejsou e-maily (osobní údaje sem záměrně nechodí), jen ID účtu a čísla objednávek.

1. Na https://minishop-tichy.cz jako admin → **Správa → Uživatelé** → najděte e-mail → **Detail**.
   ID účtu je konec adresy stránky: `…/Admin/Users/Detail/`**`8f3c…`** – zkopírujte ho.
2. Na stránce **Objednávky** (admin vidí všechny) najděte objednávku zákazníka kolem 14:20 → otevřete ji. Číslo objednávky
   je v adrese `…/Orders/`**`42`**. Na detailu je i **historie stavů** – často už odpoví: *Potvrzená* → v pořádku,
   *Zamítnutá* s důvodem (nedostatek zboží, nezaplaceno), nebo visí v *Čeká na platbu*.

**Krok 2 – časový rozsah.** V Grafaně nastavte rozsah kolem stížnosti s rezervou, např. **14:05 – 14:35**.

**Krok 3 – co uživatel dělal (trasy podle účtu).**
Explore → zdroj **`…-traces`** → *Query type* **TraceQL** → dotaz:

```
{ span.enduser.id = "8f3c…" }
```

→ **Run query**. Dole je tabulka požadavků toho uživatele (*Name* = stránka, např. `POST Checkout/Index`, *Start time*, *Duration*).
Uvidíte, jestli vůbec dorazil k pokladně, kolikrát klikal a jestli některý požadavek trval podezřele dlouho.

**Krok 4 – celý příběh objednávky.** Objednávka neběží v jednom požadavku – po odeslání pokladny jde přes outbox,
RabbitMQ, sklad, ságu a platbu ([vytvoření objednávky](../toky/vytvoreni-objednavky.md), [cesta zprávy](../toky/cesta-zpravy.md)). Všechny tyto kroky nesou číslo objednávky:

```
{ span.minishop.order.id = 42 }
```

Klikněte na první řádek (*Trace ID*) – otevře se **vodopád** (waterfall):

- každý řádek je **span**, odsazení = „kdo koho zavolal“, délka pruhu = jak dlouho trval;
- `publish …` = zpráva odešla z outboxu, `process …` = služba ji zpracovala (např. `process ReserveStockV1` ve skladu,
  `process StockReservedV1` v Orderingu = sága dostala odpověď);
- **červený span / ikona vykřičníku = chyba** – klikněte na něj: v *Span Attributes* a *Events* je text výjimky (`exception.message`);
- mezera mezi spany = čekání (outbox se zpracovává po 2 s, takže pár sekund mezery je normální).

**Krok 5 – logy k trase.** Ve vodopádu u spanu klikněte na ikonu **logů** („Logs for this span“) – Grafana otevře logy se stejným
Trace ID. Nebo ručně: Explore → zdroj **`…-logs`** → **Code**:

```
{service_name="web"} | trace_id="<Trace ID z kroku 4>"
```

Logy mají úroveň – **Error** a **Warning** jsou to, co hledáte.

**Krok 6 – vyhodnocení.**

| Co vidíte | Co to znamená | Co dál |
| --- | --- | --- |
| trasa končí `OrderConfirmed`, e-mail `process OrderConfirmedV1` v Notifications bez chyby | vše proběhlo, e-mail odešel | zákazník ať zkontroluje spam |
| span s chybou při `process …` a za ním opakované pokusy | handler selhal, zpráva se opakuje (až 5×) | po 5 pokusech je v dead-letter frontě → `/Admin/Messages` |
| sága skončila `Rejected` (důvod v atributech / v historii objednávky) | sklad neměl zboží nebo nezaplaceno | v pořádku z pohledu systému – vysvětlit zákazníkovi |
| trasa končí `publish ReserveStockV1` a dál nic | sklad zprávu nezpracoval | `/health/inventory`, dashboard (fronta `minishop.inventory`), alarm `MiniShopSagaStuck` |
| uživatel nemá v daném čase žádnou trasu | k MiniShopu nedorazil (nebo jiný čas/účet) | rozšířit časový rozsah, ověřit e-mail |

**Rychlá kontrola „je vše v pořádku“:** dashboard *MiniShop – zprávy a ságy* → řádek **Je vše v pořádku?** celý zelený
(0 poison, 0 dead-letter, nic nečeká, 0 chyb 5xx) → systém jako celek funguje a problém je spíš u jedné objednávky.

## Případ 2: Přišel e-mail s alarmem

1. V e-mailu je jméno alarmu (např. `MiniShopDeadLetter`), popis „co dělat“ a odkaz **View alert rule**.
2. V tabulce alarmů v [Monitoring a logy](monitoring.md) je u každého alarmu, kde hledat. Nejčastěji:
   - **dead-letter / poison** → https://minishop-tichy.cz/Admin/Messages → u zprávy je text chyby → opravit příčinu →
     *Vrátit do fronty* / *Zkusit znovu*;
   - **chyby 5xx** → dashboard → řádek HTTP → *Chyby 5xx podle stránky* → víte, kterou stránku hledat → případ 5 (logy).
3. Otevřete dashboard se stejným časovým rozsahem, kdy alarm začal – grafy ukážou, co se v tu chvíli změnilo.
4. Když se problém vyřeší, přijde druhý e-mail **Resolved**. Plánovaná údržba: u alarmu *More → Pause evaluation*
   (a po údržbě zase zapnout).

## Případ 3: „Web je pomalý“ – kde to vázne?

1. Dashboard → řádek **HTTP** → *Doba odpovědi p95* – je pomalý `web`, nebo `inventory-api`? Od kdy?
2. Explore → **`…-traces`** → TraceQL – nejpomalejší požadavky Webu:

   ```
   { resource.service.name = "web" && kind = server && duration > 500ms }
   ```
3. Otevřete nejdelší trasu a ve vodopádu hledejte **nejdelší pruh**:
   - dlouhý SQL span (`SELECT …`) → pomalý dotaz do databáze;
   - dlouhý `GET` na `inventory-api` → pomalý sklad (administrace se ptá skladu přes HTTP);
   - dlouhý span bez potomků → práce v kódu (např. generování PDF faktury).
4. Pro srovnání stejný dotaz o den dřív (časový rozsah) – je to nové, nebo „vždycky“?

## Případ 4: Po nasazení – je nová verze v pořádku?

1. Dashboard, rozsah **Last 3 hours** – nasazení poznáte podle krátké mezery nebo skoku v grafech.
2. Kdy přesně se kopie vyměnily: Explore → **`…-prom`** → **Code**:

   ```
   target_info{job="web"}
   ```

   Každá kopie Webu je jedna čára; nové nasazení = nové čáry s jiným `instance` (jméno kontejneru).
3. Porovnejte před a po: *Chyby 5xx*, *Doba odpovědi p95*, *Chyby doručení a zpracování*, *Potvrzení objednávky p95*.
4. Tlačítko **Split** (vpravo nahoře v Explore) rozdělí obrazovku – vlevo dotaz za včerejšek, vpravo za dnešek.

## Případ 5: Hledání v logu podle textu

Nejjednodušší: menu → **Drilldown → Logs** → služba **web** → nahoře pole pro hledání textu a filtry úrovně (*error*, *warn*) klikáním.

Ručně: Explore → **`…-logs`** → **Code**:

| Chci | Dotaz |
| --- | --- |
| všechny logy Webu | `{service_name="web"}` |
| řádky s textem | `{service_name="web"} \|= "objednávk"` |
| bez textu (vyřadit šum) | `{service_name="web"} != "health"` |
| logy skladu | `{service_name="inventory-api"}` |
| kdo je vedoucí kopie | `{service_name="web"} \|= "Vedoucí"` |

Kliknutím na řádek logu se rozbalí jeho detail – u řádků z požadavku je **Trace ID** s odkazem na trasu (zpět do případu 1, krok 4).
Do logu se nikdy nepíše e-mail ani heslo – hledejte podle ID účtu nebo čísla objednávky.

## Případ 6: Graf ukazuje nesmysl – jak zjistit proč

Skutečný případ z MiniShopu (observabilita G3): graf *Požadavky za minutu* ukazoval v noci špičky 6 000 požadavků/min,
přitom MiniShop má jednotky. Postup:

1. **Neopravovat hned graf – ověřit data.** Explore → **`…-prom`** → **Code** → otevřít surová čísla metriky a přepnout dole
   na **Table / Raw**. Rostla čísla i v noci, kdy nikdo nenakupoval → chyba v datech, ne provoz.
2. **Zkontrolovat, kdo data posílá:** `target_info{job="web"}` vrátil **1 řadu**, i když Web běží ve **2 kopiích** →
   obě kopie posílaly metriky pod stejným jménem a čítače se střídaly (1, 0, 1…), což Prometheus bral jako nové přírůstky.
3. Oprava byla v aplikaci (vlastní `service.instance.id` pro každou kopii) a po nasazení měl `target_info` **2 řady**
   a špičky zmizely. Detail: [Observabilita – metriky stavu](../eda/observabilita.md).

Obecně: **Query inspector** (pod dotazem) ukáže, co přesně Grafana poslala a co dostala zpět; **Table** místo grafu ukáže
čísla bez vyhlazování.

## Případ 7: Kolik objednávek a jak rychle?

Explore → **`…-prom`** → **Code** (rozsah klidně *Last 7 days*):

| Otázka | Dotaz |
| --- | --- |
| kolik objednávek se potvrdilo za posledních 24 h (podle platby) | `sum by (payment) (increase(minishop_order_confirmation_duration_seconds_count[24h]))` |
| za kolik sekund se potvrzuje 95 % objednávek | `histogram_quantile(0.95, sum by (le) (rate(minishop_order_confirmation_duration_seconds_bucket[1h])))` |
| kolik ság je rozpracovaných a v jakém stavu | `max by (saga, state) (minishop_saga_active)` |
| kolik zpráv doručil outbox za hodinu (podle modulu) | `sum by (module) (increase(minishop_outbox_delivered_total[1h]))` |

Vysvětlivky: `increase(x[24h])` = o kolik čítač vzrostl za 24 h; `rate` = rychlost za sekundu; `sum by (štítek)` = sečíst
a rozdělit podle štítku; `histogram_quantile(0.95, …)` = hodnota, pod kterou je 95 % měření.

## Tipy

- **Uložit dotaz:** v Explore pod dotazem *Add to saved queries* (nebo hvězdička v *Query history*).
- **Poslat odkaz:** *Share* vpravo nahoře – odkaz obsahuje dotaz i časový rozsah.
- **Z grafu do Explore:** u panelu dashboardu menu (⋮) → *Explore* – otevře dotaz panelu k úpravám.
- **Ask Assistant** (horní lišta) umí dotaz napsat podle českého popisu – výsledek ale zkontrolujte (jména metrik a štítků
  MiniShopu jsou v tabulkách výše).
- Data jsou **14 dní** – starší stížnost už v Grafaně nedohledáte (zůstává historie stavů objednávky v MiniShopu a soubory logů na VM 7 dní).

## Tahák

| Chci najít | Zdroj | Dotaz |
| --- | --- | --- |
| požadavky uživatele | traces | `{ span.enduser.id = "<ID účtu>" }` |
| příběh objednávky | traces | `{ span.minishop.order.id = 42 }` |
| požadavky na stránku | traces | `{ resource.service.name = "web" && span.url.path = "/Shop" }` |
| pomalé požadavky | traces | `{ resource.service.name = "web" && kind = server && duration > 500ms }` |
| logy k trase | logs | `{service_name="web"} \| trace_id="<Trace ID>"` |
| text v logu | logs | `{service_name="web"} \|= "text"` |
| běžící kopie služby | prom | `target_info{job="web"}` |
| poison / dead-letter | prom | `minishop_outbox_poison`, `minishop_queue_dead_letters` |
