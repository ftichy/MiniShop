---
title: Historie – jak MiniShop vznikal
summary: Deník kroků od první doménové události po zálohy v cloudu, s odkazy na podrobné souhrny v repozitáři.
order: 85
---

> **V jedné větě:** MiniShop nevznikl naráz – začal jako obyčejná vrstvená aplikace a ve 13 krocích EDA a 13 krocích
> nasazení se z něj stal systém řízený událostmi, běžící v cloudu. Každý krok má v repozitáři podrobný souhrn.

Souhrny odkazované níže jsou **návody a záznamy rozhodnutí** – co se v kroku měnilo, proč, a jak to vyzkoušet.
Dokumentace v aplikaci (tahle příručka) popisuje **výsledný stav**.

## Cesta k event-driven architektuře

Plán: [docs/EDA-roadmap.md](code:docs/EDA-roadmap.md)

| Krok | Co přinesl | Souhrn |
| --- | --- | --- |
| 1 | Doménové události v entitách (`Entity`, `IDomainEvent`) | součást kroku 2 |
| 2 | Dispatcher – doručení událostí handlerům po uložení | [krok-02](code:docs/kroky/krok-02-dispatcher.md) |
| 3 | Handlery místo přímých volání v `OrderService` | [krok-03](code:docs/kroky/krok-03-handlery.md) |
| 4 | Outbox – události v téže transakci jako data | [krok-04](code:docs/kroky/krok-04-outbox.md) |
| 5 | Background worker – asynchronní doručení s opakováním | [krok-05](code:docs/kroky/krok-05-worker.md) |
| 6 | Inbox – idempotentní handlery | [krok-06](code:docs/kroky/krok-06-inbox.md) |
| 7 | Integrační události a kontrakty `…V1` | [krok-07](code:docs/kroky/krok-07-integracni-udalosti.md) |
| 8 | RabbitMQ jako message broker | [krok-08](code:docs/kroky/krok-08-rabbitmq.md) |
| 9 | Sága objednávky s timeoutem a kompenzací | [krok-09](code:docs/kroky/krok-09-saga.md) |
| 10A | Modulární monolit – databáze na modul, kopie katalogu | [krok-10a](code:docs/kroky/krok-10a-modularni-monolit.md) |
| 10B | Sklad jako samostatná služba, .NET Aspire | [krok-10b](code:docs/kroky/krok-10b-samostatna-sluzba.md) |
| 11 | Observabilita – trasování přes zprávy | [krok-11](code:docs/kroky/krok-11-observabilita.md) |
| 12A | Proud událostí formuláře a projekce | [krok-12a](code:docs/kroky/krok-12a-prubeh-objednavky.md) |
| 12B | Přehrání průběhu objednávky | [krok-12b](code:docs/kroky/krok-12b-prehrani-objednavky.md) |
| 13 | Úklid a odolnost zpráv – retence outboxu (7 dní) a inboxu (30 dní), exponenciální backoff v outboxu, omezené opakování a dead-letter fronty v RabbitMQ, stránka Zprávy, health check dead-letters | [krok-13](code:docs/kroky/krok-13-uklid-a-odolnost-zprav.md) |

## Cesta do cloudu

Plán (včetně výběru cloudu a rozpočtu): [docs/Cloud-roadmap.md](code:docs/Cloud-roadmap.md)

| Krok | Co přinesl | Souhrn |
| --- | --- | --- |
| 1 | Dockerfile, produkční konfigurace, data v `/data` | [cloud-01](code:docs/kroky-cloud/krok-01-kontejnery.md) |
| 2 | Docker Compose – „produkce na mém PC“ | [cloud-02](code:docs/kroky-cloud/krok-02-compose.md) |
| 3 | Účet Oracle Cloud, rozpočet, compartment, IAM | [cloud-03](code:docs/kroky-cloud/krok-03-ucet-oci.md) |
| 4 | Síť a VM ručně, první nasazení | [cloud-04](code:docs/kroky-cloud/krok-04-sit-a-vm.md) |
| 5 | DuckDNS, HTTPS přes Caddy, heslo | [cloud-05](code:docs/kroky-cloud/krok-05-https.md) |
| 6 | CI – build a testy, ochrana `main` | [cloud-06](code:docs/kroky-cloud/krok-06-ci.md) |
| 7 | Image pro x86 i ARM v GHCR | [cloud-07](code:docs/kroky-cloud/krok-07-registry.md) |
| 8 | CD – automatické nasazení přes Tailscale | [cloud-08](code:docs/kroky-cloud/krok-08-cd.md) |
| 9 | Infrastruktura jako kód (OpenTofu) | [cloud-09](code:docs/kroky-cloud/krok-09-iac.md) |
| 10 | Secrets jen v GitHubu, rotace hesel | [cloud-10](code:docs/kroky-cloud/krok-10-secrets.md) |
| 11 | Monitoring, Aspire dashboard v cloudu, alarmy | [cloud-11](code:docs/kroky-cloud/krok-11-monitoring.md) |
| 12 | Zálohy a obnova, cvičná havárie | [cloud-12](code:docs/kroky-cloud/krok-12-zalohy.md) |
| 13 | Vlastní doména `minishop-tichy.cz`, přesměrování ze staré adresy | [cloud-13](code:docs/kroky-cloud/krok-13-vlastni-domena.md) |

## Uživatelské účty a role

Plán (role User a Admin, best practice): [docs/Ucty-roadmap.md](code:docs/Ucty-roadmap.md)

| Krok | Co přinesl | Souhrn |
| --- | --- | --- |
| 1 | Účty, registrace, přihlášení (ASP.NET Core Identity, `users.db`) | [ucty-01](code:docs/kroky-ucty/krok-01-ucty.md) |
| 2 | Role User/Admin, policies, fallback policy, první admin příkazem `create-admin` | [ucty-02](code:docs/kroky-ucty/krok-02-role.md) |
| 3 | Vlastník objednávky – zákazník vidí a ruší jen své, cizí = 404, jméno a e-mail z účtu | [ucty-03](code:docs/kroky-ucty/krok-03-vlastnik-objednavky.md) |
| 4 | Zrušení objednávky ve stavu Pending – **odloženo** | — |
| 5 | Skutečné e-maily – potvrzení e-mailu, nové heslo, stav objednávky (Gmail, lokálně Mailpit) | [ucty-05](code:docs/kroky-ucty/krok-05-emaily.md) |
| 6 | Správa uživatelů – povýšení, blokace, poslední admin, audit; EF Core migrace `users.db` | [ucty-06](code:docs/kroky-ucty/krok-06-sprava-uzivatelu.md) |
| 7 | Zabezpečení skladu – servisní klíč mezi Webem a Inventory.Api (zero trust) | [ucty-07](code:docs/kroky-ucty/krok-07-zabezpeceni-skladu.md) |
| 8 | Zpevnění – zámek účtu, rate limiting, CSP a bezpečnostní hlavičky, logy bez osobních údajů | [ucty-08](code:docs/kroky-ucty/krok-08-zpevneni.md) |
| 9 | Otevření webu – bez basic auth, objednávka jen s potvrzeným e-mailem, smazání účtu, ochrana osobních údajů | [ucty-09](code:docs/kroky-ucty/krok-09-otevreni-webu.md) |

## Dokončení e-shopu

Plán (výloha, košík, pokladna, expedice, platba, notifikace): [docs/Eshop-roadmap.md](code:docs/Eshop-roadmap.md)

| Krok | Co přinesl | Souhrn |
| --- | --- | --- |
| 1 | Migrace všech databází (EF Core, sdílený `DatabaseMigrator`, baseline produkčních DB) – schéma se mění bez mazání dat | [eshop-01](code:docs/kroky-eshop/krok-01-migrace.md) |
| 2A | Výloha `/Shop` – kategorie, hledání bez diakritiky, filtry, řazení; `ProductUpdatedV2` (expand → contract), `StockAvailabilityChangedV1` jen při změně pásma | [eshop-02a](code:docs/kroky-eshop/krok-02a-vyloha.md) |
| 2B | Obrázky produktů – bezpečný upload do skladu (typ podle bajtů, bez SVG, 1 MB), ve zprávě jen jméno souboru, `/media/products` s cache na rok, zálohy | [eshop-02b](code:docs/kroky-eshop/krok-02b-obrazky.md) |
| 3 | Košík – agregát `Cart` s invarianty v Orderingu (bez nové zprávy, zboží neblokuje), `/Cart`, počet kusů v hlavičce, ceny vždy aktuální z kopie katalogu | [eshop-03](code:docs/kroky-eshop/krok-03-kosik.md) |
| 4A | Pokladna `/Checkout` – objednávka s více položkami z košíku v jedné transakci, idempotentní příkaz (`CheckoutId` = verze košíku + concurrency token), žádné tiché zdražení, doprava (strategie) a adresa (hodnotový objekt) | [eshop-04a](code:docs/kroky-eshop/krok-04a-pokladna.md) |
| 4B | Sledování průběhu (Journeys) přesunuto na pokladnu – stejné typy událostí, projekce pozná formulář, přehrávač má kopii pokladny i původního formuláře pro staré proudy; starý formulář objednávky a „Objednat“ ve výloze odstraněny | [eshop-04b](code:docs/kroky-eshop/krok-04b-journeys-pokladna.md) |
| 5 | Detail objednávky `/Orders/{id}` s časovou osou stavů – historie `OrderStatusChange` přímo v agregátu (jedna transakce se stavem), migrace doplnila historii starým objednávkám bez vymyšlených časů | [eshop-05](code:docs/kroky-eshop/krok-05-detail-objednavky.md) |
| 6 | Expedice – balení, odeslání (dopravce + číslo zásilky), doručení na stránce `/Admin/Fulfilment`; `OrderShippedV1` jako událost (choreografie) → sklad vydá rezervaci; `Order.Version` proti souběhu odeslat × zrušit | [eshop-06](code:docs/kroky-eshop/krok-06-expedice.md) |
| 7 | Notifikace jako samostatný modul – Ordering jen oznamuje fakta (`OrderPlacedV2`, `OrderConfirmedV1`, `OrderRejectedV1`…), modul Notifications si drží kopii příjemce a posílá e-maily; zprávy mimo pořadí, `CustomerAnonymizedV1` (zapomenout zákazníka) | [eshop-07](code:docs/kroky-eshop/krok-07-notifikace.md) |
| 8 | Platba kartou – modul Payments jako druhý účastník ságy (`RequestPaymentV1` → `PaymentSucceededV1`/`FailedV1`), simulovaná brána a podepsaný webhook (HMAC), termín platby s kompenzací, vrácení pozdní platby, dobírka | [eshop-08](code:docs/kroky-eshop/krok-08-platba.md) |
| 9 | Zrušení kdykoli před odesláním – sága zrušení zapíše a pozdní odpověď skladu/brány dožene kompenzací (uvolnit zboží, vrátit peníze); orchestrátor dožene zrušení, které předběhla odpověď; zrušená zaplacená objednávka vrací peníze | [eshop-09](code:docs/kroky-eshop/krok-09-zruseni.md) |
| 10 | Opuštěný košík – reakce na „ticho“ (worker hledá košíky nezměněné 24 h), `CartAbandonedV1` bez osobních údajů, připomínka jen se souhlasem ověřeným až při odeslání, nejvýš jedna na nákup | [eshop-10](code:docs/kroky-eshop/krok-10-opusteny-kosik.md) |
| 11 | Přehled tržeb pro admina (CQRS) – modul Reporting jako nový odběratel existujících událostí, čtecí model se stavem jen dopředu (zprávy mimo pořadí), přestavění ze snímků `OrderSnapshotV1` | [eshop-11](code:docs/kroky-eshop/krok-11-prehled-trzeb.md) |
| 12 | Vrácení zboží – druhá sága (`ReturnSaga`) s krokem člověka uprostřed: žádost do 14 dní, převzetí adminem, naskladnění podle vydaných rezervací, vrácení peněz stávajícím `RefundPaymentV1`, zamítnutí a vypršení; přehled tržeb vratku odečte | [eshop-12](code:docs/kroky-eshop/krok-12-vraceni-zbozi.md) |
| 13 | Slevové kódy – agregát `Coupon` v Orderingu, použití ve stejné transakci jako objednávka, souběh o poslední použití hlídá verze kuponu, sleva rozpočítaná do položek (bez nového kontraktu), vrácení použití po zamítnutí/zrušení | [eshop-13](code:docs/kroky-eshop/krok-13-slevove-kody.md) |
| 14 | Hodnocení produktů – modul Reviews s vlastní kopií nákupů z událostí (hodnotí jen ten, kdo zboží dostal), souhrn `ProductRatingChangedV1` do kopie katalogu (hvězdičky a řazení ve výloze), detail produktu `/Shop/{id}`, nákupy z doby před modulem přes `SyncPurchasesV1` → `OrderSnapshotV2` | [eshop-14](code:docs/kroky-eshop/krok-14-hodnoceni-produktu.md) |

## Škálování

Plán: [docs/Skalovani-roadmap.md](code:docs/Skalovani-roadmap.md) – víc instancí služeb, nejdřív PostgreSQL.

| Krok | Co přinesl | Souhrn |
| --- | --- | --- |
| 1 | PostgreSQL vedle SQLite – provider podle `Database:Provider`, schéma pro každý modul, druhá sada migrací v `MiniShop.Infrastructure.PostgreSql`, PostgreSQL v Aspire a v Compose (profil); produkce dál na SQLite (expand → contract) | [skalovani-01](code:docs/kroky-skalovani/krok-01-postgresql.md) |

## Proč po krocích

Každý krok řeší **jeden** problém, který předchozí krok odhalil, a po každém aplikace funguje. Tak je u každé
techniky vidět, **proč** vznikla – ne jen jak vypadá.
