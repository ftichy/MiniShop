---
title: Mikroservisy – co stály
summary: Vyhodnocení vyjmutí Reportingu a Notifications do samostatných služeb – naměřená cena (paměť, metriky, nasazení), zkouška výpadku a rozhodnutí dál nevyjímat.
order: 11
---

> **V jedné větě:** Z Webu se na zkoušku vystěhovaly dva moduly (Reporting a Notifications). Fungují spolehlivě
> a výpadek služby nepřijde o žádnou zprávu, ale v aplikaci téhle velikosti nic nezrychlily a nejvíc stály
> **metrické řady v Grafaně**. Proto se **dál nevyjímá**: dvě služby zůstávají jako ukázka, zbytek je modulární monolit.

## Proč se to zkoušelo

MiniShop byl modulární monolit „nakročený“ k mikroservisám: moduly spolu mluví jen zprávami a každý má vlastní schéma
v databázi. Plán ([Mikroservisy-roadmap](code:docs/Mikroservisy-roadmap.md)) proto nepřeváděl všechno, ale jen dva moduly:

- **Reporting** – čistý odběratel událostí, nic nevolá. Ukázal celý postup nové služby (projekt, image, nasazení, alarm).
- **Notifications** – odběratel, který za běhu potřebuje data Webu (kontakt, role, PDF faktury). Ukázal, co na
  mikroservisách bolí: synchronní volání a výpadek druhé služby.

Poslední krok (M3) byl záměrně **vyhodnocení**, ne další kód. Mikroservisy nejsou cíl, ale nástroj.

## Co se naměřilo

Měřeno 19. 11. 2026 v produkci (jedna VM, 4 .NET služby, Web ve dvou kopiích). Zátěž dodal
[generátor provozu](../nasazeni/generator-provozu.md): 4 zákazníci, každý 3 nákupy (zrušená, doručená a vrácená objednávka).

| Oblast | Před (17. 11.) | Po M2C | Hodnocení |
| --- | --- | --- | --- |
| Peníze | 0 Kč | 0 Kč | beze změny |
| Paměť kontejnerů | 1,34 GB | 1,67 GiB (14 % VM) | každá služba ~170–190 MiB, i když nic nedělá |
| Paměť pod zátěží | — | +~25 MiB | paměť stojí počet služeb, ne provoz |
| CPU pod zátěží | — | Web 9 %, Reporting a Notifications ~0 % | práce je ve Webu, služby zahálí |
| Potvrzení objednávky | 4,9 s | průměr 4,3 s, 12/12 OK | beze změny – vyjmuté služby nejsou na cestě ságy |
| Doba nasazení | ~82 s | ~128 s | +45 s (každá služba = další postupná výměna) |
| Metrické řady, běžně | ~3 350 | ~4 700 | odhad byl 4 000 |
| Metrické řady při nasazení | ~6 300 | **až 10 200** | **nad limitem free tieru 10 000** |
| Build a testy v CI | ~170 s | ~200 s | +2 image, testy služeb |

### Proč tolik metrických řad

Řada = jedna kombinace jména metriky a štítků (služba, kopie, stránka, stavový kód, přihrádka histogramu…).
Každá služba posílá i bez provozu stovky řad (běh .NET, HTTP klient, Kestrel). Při
[postupném nasazení](vice-instanci.md) běží chvíli staré i nové kopie a Grafana počítá obě sady ještě ~20 minut.
Pět kopií místo tří tedy znamená i víc řad navíc v každém nasazení. Ve špičce se sešlo nasazení a provoz z generátoru:
nové kopie se hned „naplnily“ řadami všech stránek.

Alarm `MiniShopMetricsNearLimit` (víc než 8 000 řad po 30 minut) přešel do *Pending* a sám se vrátil – na krátkou
špičku záměrně nereaguje. Nad limitem ale Grafana nové řady odmítá, takže v grafech mohou být krátké díry.

## Zkouška výpadku

Služba `notifications-api` se na VM zastavila (`docker stop`) a mezitím proběhly dva nákupy z generátoru.

| Co | Výsledek |
| --- | --- |
| Nákup | prošel, objednávky potvrzené – sága na Notifications nečeká |
| Fronta `minishop.notifications` | 11 zpráv čekalo, 0 posluchačů, dead-letter 0 |
| Alarm `MiniShopQueueSilent` | podmínka po ~6 min, *Firing* po ~13 min od výpadku |
| Alarm `MiniShopQueueNoConsumer` | zůstal *Normal* – hlídá jiný případ (služba běží, consumer ne) |
| Po spuštění služby | všech 11 zpráv zpracováno do 25 s, nic v dead-letter |

Přirovnání: podatelna byla zavřená, pošta dopisy dál přinášela do schránky. Po otevření se všechny rozeslaly,
žádný se neztratil. Tohle zařídila **fronta** (RabbitMQ), ne mikroservisy – stejně by to fungovalo i ve Webu.

Proč alarm až po ~13 minutách: stav fronty hlásí služba sama. Když neběží, Grafana ještě ~5 minut považuje poslední
hodnotu za platnou, pak se rozběhne `for: 5m` a vyhodnocení každou minutu. Pro e-maily, které počkají, to stačí.

## Co se zlepšilo, co zhoršilo

**Lepší:**
- Výpadek nebo pomalost jedné služby nezasáhne stránky – Web ukáže upozornění a nákup jde dál.
- Soukromý klíč VAPID a SMTP objednávek jsou jen v jedné malé službě.
- Stránka *Zprávy* ukazuje všechny služby (M2B) – to zůstane užitečné bez ohledu na rozhodnutí.
- Hlídání front (M0) a zkušenost s celým postupem nové služby.

**Horší:**
- Metrické řady v Grafaně – největší cena, free tier je na hraně.
- Každá služba = Dockerfile, Compose, CI, `deploy.sh` (ruční instalace na VM), secret, alarmy, příručka.
- Notifications závisí na Webu (interní API) – výpadek Webu zdrží e-maily s kontaktem nebo PDF.
- Ladění přes víc procesů – bez trasování v Grafaně by se e-mail Web → RabbitMQ → Notifications → Web hledal těžko.

**Co nepřinesly:** samostatné škálování (služby mají CPU ~0 %) ani nezávislé nasazení – služby sdílí knihovny
`Application` a `Infrastructure`, takže CI nasazuje všechny z jednoho commitu.

## Rozhodnutí: zastavit

Další moduly (Payments, Invoicing, Reviews) se **nevyjímají**:

- zahálely by stejně jako Reporting a Notifications,
- každá další služba = ~180 MiB paměti, stovky metrických řad (při nasazení dvojnásobek) a další ruční krok,
- hlavní výhoda mikroservis – nezávislé nasazení týmů – tu nemá komu sloužit (jeden vývojář, sdílené knihovny).

Reporting.Api a Notifications.Api **zůstávají** jako funkční ukázka. Vrátit je do Webu by byla práce bez užitku
a přišlo by se o příklad.

Kdy by se rozhodnutí změnilo: když bude některý modul potřebovat jiný rytmus nasazení, víc výkonu než zbytek,
nebo když na aplikaci začne pracovat víc lidí. Pak nejdřív rozdělit knihovny po modulech, jinak nezávislé nasazení nejde.

## Co z toho plyne

- **Modulární monolit je dobrý výchozí stav.** Hranice modulů (zprávy, schéma na modul, test architektury) daly
  vyjmutí skoro zadarmo – kód handlerů se nezměnil. Hranice jsou důležitější než počet procesů.
- **Spolehlivost dělá fronta, ne rozdělení.** Outbox, inbox a dead-letter fungují stejně ve Webu i ve službě.
- **Cena mikroservis je hlavně provozní** – metriky, nasazení, alarmy, secrets. Proto vznikl krok M4: méně metrik
  v Alloyi (vyplatí se i bez dalších služeb).

## Na internetu

- [Martin Fowler: MonolithFirst](https://martinfowler.com/bliki/MonolithFirst.html)
- [Martin Fowler: Microservice Premium](https://martinfowler.com/bliki/MicroservicePremium.html)
