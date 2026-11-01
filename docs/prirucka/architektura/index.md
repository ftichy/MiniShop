---
title: Architektura aplikace
summary: Z čeho se MiniShop skládá – od běžících procesů přes moduly a vrstvy až po principy SOLID v kódu.
order: 10
---

> **V jedné větě:** MiniShop jsou **dvě služby** (Web a Inventory.Api), které si posílají zprávy přes RabbitMQ.
> Uvnitř jsou rozdělené na **moduly** (Ordering, Inventory, Journeys) a každý modul na **vrstvy**
> (Domain, Application, Infrastructure).

## Tři pohledy na stejnou aplikaci

Představte si budovu. Můžete ji popsat podle **budov v areálu** (které procesy běží), podle **oddělení**
(moduly – kdo za co odpovídá) a podle **pater** (vrstvy – co je business logika a co technika).
Všechny tři pohledy platí současně:

```mermaid
flowchart TB
    subgraph s1["1 – Služby (procesy)"]
        direction LR
        w[Web]
        i[Inventory.Api]
        r[(RabbitMQ)]
    end
    subgraph s2["2 – Moduly (business oblasti)"]
        direction LR
        o[Ordering<br/>objednávky]
        n[Inventory<br/>sklad]
        j[Journeys<br/>průběh formuláře]
    end
    subgraph s3["3 – Vrstvy (projekty)"]
        direction LR
        d[Domain] --- a[Application] --- inf[Infrastructure] --- web[Web / Api]
    end
    s1 --> s2 --> s3

    click w "/Docs/architektura/kontejnery"
    click i "/Docs/architektura/kontejnery"
    click o "/Docs/architektura/moduly/ordering"
    click n "/Docs/architektura/moduly/inventory"
    click j "/Docs/architektura/moduly/journeys"
    click d "/Docs/architektura/vrstvy"
    click a "/Docs/architektura/vrstvy"
    click inf "/Docs/architektura/vrstvy"
    click web "/Docs/architektura/vrstvy"
```

- Služba **Web** hostuje moduly *Ordering* a *Journeys*, služba **Inventory.Api** modul *Inventory*.
- Každý modul má kód ve všech vrstvách (např. `Domain/Orders`, `Application/Ordering`, `Infrastructure/Ordering`).
- Společné „potrubí“ pro zprávy (outbox, inbox, RabbitMQ) je v [sdíleném jádru](sdilene-jadro.md) a nezná žádný modul.

## Hlavní pravidla

1. **Závislosti vedou dovnitř** – doména nezávisí na ničem, databáze a web jsou jen „detail“. Viz [Vrstvy](vrstvy.md).
2. **Moduly se navzájem neznají** – žádná reference, žádná sdílená tabulka. Mluví spolu jen zprávami
   z `MiniShop.Contracts`. Hlídá to test [ModuleBoundariesTests](code:tests/MiniShop.Tests/Architecture/ModuleBoundariesTests.cs).
3. **Každý modul má vlastní databázi** (`ordering.db`, `inventory.db`, `journeys.db`, `users.db`).
4. **Business tok jde jen přes zprávy** – HTTP mezi službami slouží jen k zobrazení dat v UI.

## Proč takhle (a ne jednodušeji)

Pro tak malý e-shop by stačila jedna aplikace s jednou databází. MiniShop je ale **výukový** – postupně prošel
celou cestou od „jedné aplikace“ po „samostatné služby“ (viz `docs/EDA-roadmap.md` v repozitáři). Každé
rozdělení má svou cenu (víc pohyblivých částí, data se synchronizují se zpožděním) a dokumentace u každého
konceptu říká, **proč** se vyplatí a **co stojí**.

## Na internetu

- [Model C4](https://c4model.com/) – jak kreslit architekturu na několika úrovních přiblížení
- [Microsoft: Běžné architektury webových aplikací](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
