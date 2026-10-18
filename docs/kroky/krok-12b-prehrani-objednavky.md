# Krok 12B: Přehrání průběhu objednávky – druhá projekce stejného proudu

V kroku 12A jsme zapsali, co uživatel ve formuláři dělal, a složili z toho časovou osu. Teď to samé **přehrajeme**:
zadáte číslo objednávky a na kopii formuláře se znovu odehraje, jak uživatel psal, vybíral produkt, kde ho zarazila
chyba a kdy klikl na Objednat. Data se nemění – je to jen další **projekce** stejného proudu událostí.

## Jak to funguje

Přirovnání: záznam šachové partie. Zápis tahů (1. e4 e5 2. Jf3 …) je proud událostí. Z něj jde vypsat přehled
(„bílý bral 3 figury“ – projekce z 12A), nebo partii **přehrát** na šachovnici tah po tahu. A když chcete vidět pozici
po 20. tahu, postavíte výchozí pozici a zahrajete tahy 1–20 – nic dalšího není potřeba ukládat.

```
GET /Orders/Replay/9?autoplay=1
  JourneyService.GetReplayByOrderIdAsync(9)
    1. proud objednávky 9                     (stejné načtení jako 12A)
    2. OrderJourneyProjection  → časová osa   (12A)
    3. JourneyReplayProjection → snímky:      (12B)
         { playAtMs: 0,    event: FormOpened }
         { playAtMs: 1200, event: FieldFocused Input.CustomerName }
         { playAtMs: 1500, event: FieldChanged "P" }  … každý znak ve skutečném tempu
         { playAtMs: 4300, skippedPauseMs: 37000, … } ← uživatel 40 s nic nedělal, pauza zkrácena na 3 s
  → stránka: kopie formuláře + scénář jako JSON ve <script type="application/json">

journey-player.js (v prohlížeči)
  čas běží (requestAnimationFrame × rychlost 1×/2×/4×) → odehraje snímky s playAtMs ≤ čas
  FieldFocused → modrý rámeček · FieldChanged → hodnota pole · Clicked → bliknutí tlačítka/odkazu v menu
  FieldInvalid → červené pole + hláška prohlížeče · ValidationFailed → hláška serveru · OrderSubmitted → „vznikla“
  posuvník (přetočení) → výchozí stav + všechny snímky do zvoleného času bez animace = fold 0…T
```

1. **Scénář připraví server.** `JourneyReplayProjection` projde události a ke každé spočítá `PlayAtMs` – kdy ji
   přehrát. Pauzy delší než 3 s zkrátí (`SkippedPauseMs` řekne, o kolik). Psaní zůstává ve skutečném tempu,
   každý znak je samostatná událost. `StepIndex` řekne, ke kterému kroku časové osy událost patří (zvýrazní se v seznamu).
2. **Přehrávač jen odehrává.** Nemá žádnou vlastní logiku „co se dělo“ – události aplikuje na kopii formuláře
   se stejnými jmény polí (`data-replay-target="Input.CustomerEmail"`), jaká posílal sledovací skript (`Target`).
3. **Přetočení = projekce od začátku.** Posuvník nevrací „změny zpět“. Formulář se vrátí do výchozího stavu a všechny
   události až do zvoleného okamžiku se aplikují znovu, jen bez animace. To je princip event sourcingu: stav v čase T
   je vždy výsledek událostí 0…T.
4. **Nová událost `FieldInvalid`.** Při ověřování v Chrome se ukázalo, že prohlížeč sám zastaví odeslání
   (např. „eva@“ v poli typu e-mail) a na server nic nedorazí. Průběh pak ukazoval jen „Klikl na Objednat“ bez
   vysvětlení. Sledovací skript proto poslouchá událost `invalid` a zapíše hlášku, kterou uživatel viděl.
   Počítá se do neúspěšných odeslání (víc polí odmítnutých jedním kliknutím = jeden pokus).

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `src/MiniShop.Application/Journeys/JourneyReplayProjection.cs` | druhá projekce: snímky přehrávání (`PlayAtMs`, `SkippedPauseMs`, `StepIndex`), `JourneyReplayDto` |
| `src/MiniShop.Web/Pages/Orders/Replay.cshtml(.cs)` | stránka přehrání: vstup = číslo objednávky, kopie formuláře, ovládání, seznam kroků, scénář jako JSON |
| `src/MiniShop.Web/wwwroot/js/journey-player.js` | přehrávač: přehrát/pauza, od začátku, rychlost 1×/2×/4×, posuvník, `?autoplay=1` |
| `tests/MiniShop.Tests/Application/JourneyReplayProjectionTests.cs` | zkracování pauz, přiřazení ke krokům, pozdní první událost, prázdný proud |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Journeys/JourneyEventType.cs` | nový typ `FieldInvalid` (na konci výčtu, ukládá se jako text → bez změny schématu) |
| `Application/Journeys/OrderJourneyProjection.cs` | krok „Prohlížeč nepustil odeslání – …“, počítání neúspěšných odeslání |
| `Application/Journeys/IJourneyService.cs`, `JourneyService.cs` | `GetReplayByOrderIdAsync` |
| `Web/Controllers/JourneysController.cs` | `GET /api/journeys/orders/{orderId}/replay` |
| `Web/wwwroot/js/order-form-tracker.js` | událost `FieldInvalid` (posluchač `invalid` ve fázi capture) |
| `Web/Pages/Orders/Index.cshtml` | tlačítko „▶ Přehrát“ u pole s číslem objednávky a u každé objednávky, `data-track="order-submit"` u tlačítka Objednat |
| `Web/Pages/Orders/Journey.cshtml(.cs)` | odkaz „▶ Přehrát“, štítek pro `FieldInvalid` |
| `Web/Pages/Shared/_Layout.cshtml` | `data-track` u odkazů v menu (přehrávač je rozbliká) |
| `Web/wwwroot/css/site.css` | styly přehrávání (rámeček pole, bliknutí kliku, posuvný seznam kroků) |
| `tests/.../Application/JourneyServiceTests.cs` | `Prehrani_StejnyProudJakoPrubeh`, `Zapis_ProhlizecNepustilOdeslani_JeVSouhrnu` |

## Rozhodnutí v tomto kroku

- **Scénář počítá server (C#), prohlížeč jen přehrává.** Výpočet časové osy je tak otestovaný v xUnit a JavaScript
  zůstává jednoduchý. Zároveň je to názorná „druhá projekce“ vedle té z 12A.
- **Scénář je vložený do stránky jako JSON** (`<script type="application/json">`), ne načítaný dalším požadavkem.
  `System.Text.Json` escapuje `<` a `>`, takže ani text napsaný uživatelem (třeba `</script>`) stránku nerozbije.
  Stejná data jsou i na `GET /api/journeys/orders/{id}/replay`.
- **Pauzy delší než 3 s se zkracují**, psaní ne. Na přehrání se nečeká minuty a tempo psaní (váhání, opravy) je vidět.
  Při zkrácené pauze přehrávač ukáže „uživatel 40 s nic nedělal“.
- **Kopie formuláře, ne skutečný formulář.** Přehrání nesmí nic odeslat ani spustit sledování. Kopie má stejná
  jména polí, nedá se do ní psát ani klikat.
- **Nabídka produktů v kopii se skládá z proudu** (`ValueText` z výběrů), ne z dnešního katalogu. Přehrání ukáže
  produkt i s cenou a zásobou, jak je uživatel tehdy viděl, i když už v katalogu není.
- **Odkazy v menu blikají ve skutečné horní liště** (`data-track="nav-…"`), ostatní kliky (např. na jinou objednávku)
  ukáže jen popisek kroku.
- **`FieldInvalid` patří do 12B**, i když jde o záznam (12A). Objevil ho až test v prohlížeči. Je to jen nový typ
  události – starší proudy ho nemají a obě projekce fungují dál (to je výhoda proudu: nový typ nic nerozbije).

## Známá omezení

- Přehrává se jen formulář objednávky. Kliky mimo formulář a menu (např. „Vyřídit“ u jiné objednávky) jsou jen popisek.
- Proudy z doby před 12B nemají `FieldInvalid` ani `data-track` u tlačítka Objednat. Přehrávač tlačítko najde
  podle textu, zablokované odeslání prohlížečem ale u nich vidět není.
- Kopie formuláře je ručně udržovaná podle `Orders/Index.cshtml`. Nové pole ve formuláři je potřeba přidat i do
  `Replay.cshtml` (jinak se jeho změny v přehrání neukážou, v časové ose ano).
- Mezi odesláním a novým načtením stránky po chybě serveru (~100 ms) se čas formuláře nezaznamenává – přehrání
  na sebe navazuje bez mezery.
- Hlášky prohlížeče (`FieldInvalid`) jsou v jazyce prohlížeče uživatele.

Schéma databáze se nemění, **mazat nic nemusíte**.

## Jak ověřit

1. `dotnet test` – projde všech 160 testů. Nové: `JourneyReplayProjectionTests` (4×), `Prehrani_StejnyProudJakoPrubeh`,
   `Zapis_ProhlizecNepustilOdeslani_JeVSouhrnu`.
2. Spusťte `dotnet run --project src/MiniShop.AppHost`, na `https://localhost:7180/Orders` vytvořte objednávku –
   klidně nejdřív s e-mailem „jan@“ (prohlížeč odeslání zastaví).
3. U objednávky klikněte na **▶ Přehrát** (nebo zadejte číslo do pole „Průběh zakládání objednávky č.“ a dejte „▶ Přehrát“).
4. Sledujte kopii formuláře: psaní znak po znaku, modrý rámeček aktivního pole, červené pole s hláškou prohlížeče,
   bliknutí tlačítka Objednat. Vpravo se zvýrazňuje aktuální krok.
5. Zkuste rychlost 4× a posuvník – po přetočení je formulář přesně ve stavu, v jakém byl v tu chvíli.

Ověřeno naživo v Chrome (headless, ovládaný přes DevTools protokol): skutečné psaní do formuláře, výběr produktu,
odeslání s „petr@“ (prohlížeč ho zastavil → `FieldInvalid` s hláškou „Zadejte část za znakem @…“), oprava e-mailu,
objednávka č. 9. Časová osa ukázala 14 kroků a 1 neúspěšné odeslání. Přehrání v tom okamžiku ukázalo e-mail „petr@“
červeně s hláškou, na konci „petr@example.cz“ a „Objednávka č. 9 vznikla“. Přetočení posuvníkem vrátilo formulář
do stavu v daném čase. Tím je ověřen i sledovací skript z 12A ve skutečném prohlížeči.

## Vyzkoušejte sami

1. **Přetáčení jako fold.** Pusťte přehrání, pak posuvníkem skočte doprostřed a zpátky na začátek. V
   `journey-player.js` najděte `seek()` – nevrací změny zpět, jen znovu aplikuje události od začátku.
2. **Jiné zkracování pauz.** V `JourneyReplayProjection` změňte `DefaultMaxPauseMs` na 1 000. Přehrání bude svižnější,
   časová osa (12A) se nezmění – je to jiná projekce.
3. **Nový typ události.** Přidejte do sledovacího skriptu `paste` (vložení ze schránky) jako nový typ v
   `JourneyEventType` a v přehrávači ho zobrazte popiskem „vložil ze schránky“. Staré proudy ho nemají – a nic se nerozbije.
4. **Scénář jako data.** Otevřete `https://localhost:7180/api/journeys/orders/{číslo}/replay` a porovnejte `playAtMs`
   s `event.offsetMs` – u dlouhé pauzy uvidíte rozdíl a `skippedPauseMs`.
