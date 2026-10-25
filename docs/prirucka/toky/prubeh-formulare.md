---
title: Průběh formuláře a přehrání
summary: Jak se zaznamenává vyplňování formuláře objednávky a jak se pak zobrazí jako časová osa nebo „video“.
order: 8
---

> **Shrnutí:** Skript v prohlížeči sbírá akce ve formuláři a každé 2 s je pošle dávkou na server. Server
> doplní své události (chyba validace, vznik objednávky). Stránky *Průběh* a *Přehrát* pak proud promítnou.

## Záznam

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant T as order-form-tracker.js
    participant JC as JourneysController
    participant P as Orders/Index (server)
    participant JS as JourneyService
    participant DB as journeys.db

    Z->>T: otevře formulář → FormOpened (journeyId = nové GUID)
    Z->>T: píše, kliká → FieldFocused, FieldChanged, Clicked…
    loop každé 2 s
        T->>JC: POST /api/journeys/{journeyId}/events (dávka)
        JC->>JS: RecordAsync – přeskočí známá EventId
        JS->>DB: INSERT nové události
    end
    Z->>T: Objednat → SubmitClicked
    T->>JC: sendBeacon (zbytek dávky při opuštění stránky)
    Z->>P: POST formulář (s journeyId a časem)
    alt validace selhala
        P->>JS: ValidationFailed (hlášky)
    else objednávka vznikla
        P->>JS: OrderSubmitted (OrderId) – tím je proud spojený s objednávkou
    end
```

## Zobrazení a přehrání

```mermaid
sequenceDiagram
    autonumber
    actor O as Obsluha
    participant PG as Orders/Journey, Orders/Replay
    participant JS as JourneyService
    participant P1 as OrderJourneyProjection
    participant P2 as JourneyReplayProjection
    participant PL as journey-player.js

    O->>PG: Průběh objednávky 42
    PG->>JS: GetByOrderIdAsync(42)
    JS->>P1: fold přes všechny události proudu
    P1-->>PG: časová osa + souhrn
    O->>PG: Přehrát
    PL->>JS: GET /api/journeys/orders/42/replay
    JS->>P2: scénář (pauzy nad 3 s zkrácené)
    P2-->>PL: snímky s časem přehrání
    PL->>PL: přehraje akce nad kopií formuláře
```

## Co si všimnout

- **Sledování nesmí rozbít objednávku** – chyba zápisu do `journeys.db` se jen zaloguje.
- Čas událostí je relativní k otevření formuláře – dávky můžou dorazit v libovolném pořadí.
- Prvky, které má přehrávač najít, mají atribut `data-track`; `Replay.cshtml` má ruční kopii formuláře
  (nové pole formuláře je potřeba přidat i tam).

## Kód

[order-form-tracker.js](code:src/MiniShop.Web/wwwroot/js/order-form-tracker.js) ·
[JourneysController](code:src/MiniShop.Web/Controllers/JourneysController.cs) ·
[JourneyService](code:src/MiniShop.Application/Journeys/JourneyService.cs) ·
[journey-player.js](code:src/MiniShop.Web/wwwroot/js/journey-player.js)
