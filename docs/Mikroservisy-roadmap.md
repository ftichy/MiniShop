# MiniShop: vyjmutí Reportingu a Notifications do samostatných služeb

17. 11. 2026 · František

Cíl: vyzkoušet si na dvou modulech, co znamená **mikroservisa** – samostatný proces s vlastním nasazením, restartem
a výpadkem – a pak se vědomě rozhodnout, jestli pokračovat. Ne převést celý MiniShop.

MiniShop je dnes modulární monolit „nakročený“ k mikroservisám: moduly mluví jen přes zprávy (outbox, inbox, fronta
na modul), každý má vlastní schéma a jedna služba už je venku (Inventory.Api, EDA krok 10B). Ve Webu ale dál běží
Ordering, Notifications, Payments, Reporting, Reviews, Invoicing, Journeys, Accounts a celé UI.

Proč právě tyto dva:
- **Reporting** – nejčistší odběratel událostí (CQRS, e-shop krok 11). Nic nevolá, jen čte zprávy a staví přehled.
  Ukáže, že odběratel může běžet kdekoli.
- **Notifications** – odběratel, který ale za běhu potřebuje data jiných modulů (kontakt zákazníka, role zaměstnance,
  název produktu, PDF faktury). Ukáže, **co na mikroservisách bolí**: synchronní závislosti a výpadek druhé služby.

Rozpočet zůstává 0 Kč (stejná VM, stejný Grafana Cloud Free). Souhrny kroků do `docs/kroky-mikroservisy/`.

## Rozhodnutí

1. **Jen Reporting a Notifications.** Ordering, Accounts a UI zůstávají ve Webu (jádro, identita, stránky).
   Payments, Invoicing, Reviews se rozhodnou v kroku M3.
2. **Sdílené knihovny zůstávají** – nová služba referencuje `MiniShop.Application` a `MiniShop.Infrastructure` celé,
   stejně jako Inventory.Api. CI staví všechny image z jednoho commitu a nasazuje je spolu, takže „změna jádra =
   nasadit všechny“ nic nestojí. Rozdělení knihoven na projekty po modulech až po M3, pokud se pokračuje.
3. **Jedna databáze, schéma beze změny** – `reporting` a `notifications` už jsou samostatná schémata s vlastní
   historií migrací. Žádný převod dat; migrace schématu spouští nově služba, ne Web. Zálohy beze změny.
4. **Přechod bez odstávky přes stejnou frontu a stejný inbox.** Při postupném nasazení chvíli poslouchá frontu
   `minishop.reporting` (`minishop.notifications`) stará kopie Webu i nová služba – RabbitMQ dá každou zprávu jen
   jednomu (competing consumers) a inbox ve schématu modulu je společný, takže nic neproběhne dvakrát.
   Nová služba se v `deploy.sh` spouští **před** Webem.
5. **UI zůstává ve Webu, služby mají REST API se servisním klíčem** (vzor Inventory.Api – `X-Api-Key`, účty krok 7).
   Uživatele služba nezná z cookie: Web ověří přihlášení a roli a pošle `userId` jako parametr. Služba věří Webu
   (klíč), ne prohlížeči. JWT není potřeba – volá jen Web.
6. **Klíč pro každou volanou službu** (zero trust – uniklý klíč otevře jen jednu službu): nové secrets
   `REPORTING_API_KEY`, `NOTIFICATIONS_API_KEY` a `WEB_INTERNAL_API_KEY` (Notifications → Web). Registr `docs/secrets.md`.
7. **Notifications se na data jiných modulů ptá Webu synchronně** (interní API `/internal/…`), nekopíruje je ze
   zpráv. Kopie kontaktů by znamenala nové kontrakty a další kopii osobních údajů (e-mail, souhlas) mimo Accounts;
   synchronní dotaz je jednodušší a údaje zůstanou na jednom místě. Cena: výpadek Webu = e-mail počká (viz rizika).
8. **Jedna kopie každé nové služby** (Web dál `WEB_REPLICAS=2`). Zprávy ve frontě počkají; vedoucí kopie přes
   `LeaderElection` s vlastním zámkem (`minishop.reporting-api`, `minishop.notifications-api`), aby šlo škálovat později.
9. **Nejdřív hlídání, pak služby** (krok M0): víc služeb = víc metrických řad v Grafaně a víc práce pro Alloy.

## Kroky

| Krok | Co | Proč |
| --- | --- | --- |
| M0. Příprava a Grafana | Alarm `MiniShopMetricsNearLimit` (aktivní řady > 8 000 po 30 min, zdroj `grafanacloud-usage`, samostatný soubor `deploy/grafana/minishop-alarmy-limity.yaml` + složka „MiniShop limity“ – import přepíše pravidla ve složce); `GrafanaConfigTests` a `promtool` pro všechny `minishop-alarmy*.yaml`. Alloy `mem_limit` 256 → 384 MiB. Nová metrika `minishop.queue.consumers` (počet posluchačů fronty – `QueueDeclarePassive` ho vrací spolu s `MessageCount`) + alarm `MiniShopQueueNoConsumer` (fronta modulu bez posluchače 5 min) a `MiniShopQueueSilent` (o frontě 5 min nepřišel stav = služba neběží – stav měří sama služba, takže spadlá služba nulu nenahlásí) + panel v dashboardu. Souhrn [krok-m0](kroky-mikroservisy/krok-m0-priprava.md). Kód modulů beze změny. | Dnes frontu všech modulů poslouchá Web – „nikdo neposlouchá“ nemůže nastat. Po vyjmutí ano: spadlá služba Reporting by nebyla vidět (zpráv je málo, alarm na backlog > 50 by se neozval). Limit řad a paměť Alloye hlídat dřív, než porostou. |
| M1. Reporting jako služba | Projekt `MiniShop.Reporting.Api` (vzor Inventory.Api): handlery `SalesProjectionHandler`, `SalesReportOnStartupWorker`, úklid zpráv, migrace schématu `reporting`, API `GET /api/sales` a `POST /api/sales/rebuild` (klíč). Web: `ReportingApiClient` (`https+http://reporting-api`, resilience ze ServiceDefaults), `/Admin/Sales` přes něj, modul Reporting z Webu pryč (`RebuildSalesReportHandler` zůstává v Orderingu). Test architektury: Web nesmí na `Domain/Infrastructure.Reporting`. AppHost, Dockerfile, `compose.yaml` (+ E2E, ZAP), CI `images` (nový image x86 + ARM), `deploy.sh` (`rolling-update.sh reporting-api 1` před Webem), secret `REPORTING_API_KEY`, příručka (moduly, nasazení, toky). | Nejjednodušší služba – naučí celý postup (projekt, image, nasazení, alarm) bez synchronních závislostí. Výpadek = jen `/Admin/Sales` hlásí „přehled je nedostupný“, e-shop běží. Souhrn [krok-m1](kroky-mikroservisy/krok-m1-reporting.md). |
| M2A. Interní API Webu (expand) | Ve Webu `/internal/…` jen s `WEB_INTERNAL_API_KEY`: kontakt zákazníka (potvrzený e-mail, jméno, souhlas s připomínkou), filtr zaměstnanců podle role, název produktu, PDF faktury a dobropisu. HTTP implementace `ICustomerContacts`, `IStaffDirectory`, `IProductNames`, `IInvoiceDocuments` (projekt pro budoucí službu). Notifications dál ve Webu s implementacemi v procesu – API jen připravené a otestované. Endpointy bez osobních údajů v trase a logu (ID ano, e-mail ne). | Rozdělení na dvě nasazení (expand → contract): API se ověří v produkci dřív, než na něm začnou záviset e-maily. Souhrn [krok-m2a](kroky-mikroservisy/krok-m2a-interni-api.md). |
| M2B. Zprávy ze všech služeb | `MessagesOverview` ve sdíleném jádru (stav outboxů a dead-letter front služby, „Zkusit znovu“, „Vrátit do fronty“), `MessagesController` `/api/messages` ve skladu i přehledu tržeb (jejich servisní klíč), stránka `/Admin/Messages` přes `IServiceMessages` – Web v procesu, ostatní služby HTTP, nedostupná služba = upozornění jen u ní. Bez ručních kroků. Souhrn [krok-m2b](kroky-mikroservisy/krok-m2b-zpravy-vsech-sluzeb.md). | Přehled tržeb jde přestavět, e-mail ne – dřív, než Notifications odejde z Webu, musí jít jeho dead-letter zprávy vrátit z `/Admin/Messages`. Bonus: konečně je vidět i sklad. |
| M2C. Notifications jako služba | Projekt `MiniShop.Notifications.Api`: všechny handlery modulu (e-maily, push, oznámení k práci, „Dejte mi vědět“), SMTP a klíče VAPID (secrets `SMTP_PASSWORD`, `PUSH_VAPID_PRIVATE_KEY` i do nového kontejneru), HTTP implementace z M2A. API pro UI (klíč, `userId` parametrem): zařízení push (seznam, přidat, odebrat, zkušební oznámení), oznámení k práci (načíst, uložit – povolené typy pošle Web podle rolí), „Dejte mi vědět“ (stav, přihlásit). Web: `NotificationsApiClient`, stránky *E-maily a oznámení* a `/Shop/{id}` přes něj, modul z Webu pryč; Web si nechá SMTP jen pro e-maily účtu (`AccountEmails`) a veřejný klíč VAPID pro `push.js`. Test architektury, AppHost, Compose, CI, `deploy.sh`, secret `NOTIFICATIONS_API_KEY`, příručka, `/Privacy` (kde leží údaje se nemění). | Služba, na které jsou vidět skutečné problémy mikroservis – a jejich řešení (retry, dead-letter, circuit breaker, trasa přes tři služby). |
| M3. Vyhodnocení | Co se zhoršilo/zlepšilo (čas nasazení, paměť, řady v Grafaně, složitost ladění), shrnutí do příručky. Rozhodnutí: dál (Payments, Invoicing, Reviews + rozdělení knihoven) nebo zastavit. | Mikroservisy nejsou cíl, ale nástroj – rozhodnout podle zkušenosti, ne podle módy. |

## Náklady (změřeno 17. 11. 2026)

| | Dnes | Po M2C (odhad) |
| --- | --- | --- |
| Peníze | 0 Kč | 0 Kč |
| Paměť VM (8 kontejnerů, 11,65 GB) | 1,34 GB (11 %) | ~1,7 GB (15 %) |
| Metrické řady Grafana (běžně, limit 10 000) | ~3 350 (web ~2 900 ve 2 kopiích, sklad ~380) | ~4 000 (40 %) |
| Řady během nasazení (staré + nové kopie ~20 min) | ~6 300 | ~7 000 |
| Alloy | 184 / 256 MiB (72 %) | limit 384 MiB (M0) |
| CI a nasazení | — | +2 image, +~1 min nasazení na službu |
| Secrets | — | +3 (klíče API) |

Odhad paměti podle Inventory.Api (151 MB): Reporting ~150 MB, Notifications ~180 MB. Řady: služba bez stránek má
jen `/health`, runtime a zprávy (~350–450), Web přijde o metriky dvou modulů.

## Rizika a známá omezení

- **Výpadek Webu zdrží e-maily.** Notifications se ptá Webu (kontakt, PDF); consumer zkusí zprávu 5× (2–16 s) a pak
  ji odloží do `minishop.notifications.dead`. Postupné nasazení Web nikdy nevypne celý, ale delší výpadek = ruční
  *Vrátit do fronty* na `/Admin/Messages` (alarm `MiniShopDeadLetter` se ozve). Případné zlepšení (delší čekání
  nebo kopie kontaktů ze zpráv) až v M3.
- **Výpadek Notifications nezastaví nákup** – zprávy čekají ve frontě, e-maily odejdou po návratu; stránka
  *E-maily a oznámení* a tlačítko „Dejte mi vědět“ hlásí nedostupnost (circuit breaker ze ServiceDefaults).
- **Limit Grafany.** Při ~7 000 řadách během nasazení zbývá méně místa na třetí kopii Webu nebo další službu –
  hlídá alarm z M0; náprava zdarma: zahodit nepotřebné metriky v Alloyi, méně přihrádek histogramů.
- **Sdílené knihovny** – služby nejsou nezávislé ve verzích (vědomě, rozhodnutí 2).
- **Testy.** `OrderServiceTests` dál skládají všechny moduly v procesu (moduly jsou knihovny) – nehlídají volání po
  síti. Nové: testy API klientů s podvrženým `HttpClient` (jako `InventoryApiClient`), API služeb ve vlastní
  factory (jako `InventoryApiFactory`), E2E nad Compose se všemi službami.
- **Trasa přes víc služeb** – ladění je těžší, ale trasy (EDA krok 11, Grafana G1) propojí Web → RabbitMQ →
  Notifications → Web (interní API) do jedné.
