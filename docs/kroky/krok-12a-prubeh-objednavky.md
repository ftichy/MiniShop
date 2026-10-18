# Krok 12A: Průběh zakládání objednávky – proud událostí a projekce

Chceme vědět, **jak** uživatel objednávku zakládal: kam klikal, co psal, kolikrát měnil produkt, na čem se zasekl.
Výsledek (hotová objednávka) to neřekne. Proto každou akci ve formuláři zapíšeme jako **událost** do proudu
a čitelný průběh z něj spočítá **projekce**. Vstupem je číslo objednávky: `/Orders/Journey/3`.

## Jak to funguje

Přirovnání: GPS záznam jízdy. Navigace si neukládá „jsem v Brně“, ale každých pár sekund bod trasy. Z bodů pak
umí nakreslit mapu, spočítat průměrnou rychlost nebo trasu přehrát. Body jsou **události**, mapa a statistika
jsou **projekce**. A z jednoho záznamu jde udělat víc pohledů, i takových, na které jsme při jízdě nemysleli.

```
Prohlížeč (order-form-tracker.js)                  Web                                     journeys.db
FormOpened, FieldFocused, FieldChanged "J", "Ja", "Jan", …
  ── dávka každé 2 s ──► POST /api/journeys/{id}/events ─► JourneyService ─► INSERT JourneyEvents
SubmitClicked ── sendBeacon ──►                                              (EventId unikátní → duplicity pryč)
formulář ── POST /Orders?handler=Create (JourneyId, JourneyOffsetMs) ──►
                                       chyba validace → ValidationFailed ─► INSERT
                                       objednávka č. 3  → OrderSubmitted(OrderId=3) ─► INSERT

GET /Orders/Journey/3 ─► JourneyService.GetByOrderIdAsync(3)
                         1. najdi proud s OrderSubmitted(OrderId=3)
                         2. načti všechny jeho události (podle času)
                         3. OrderJourneyProjection: Apply(e1), Apply(e2), … → časová osa + souhrn
```

1. **Proud (event stream).** Jedna návštěva formuláře = jeden `JourneyId` (Guid ve skrytém poli formuláře).
   Každá akce = řádek v `JourneyEvents`: typ, prvek (`Target` technicky, `Label` lidsky), hodnota (skutečně napsaný
   text, ID produktu), čas `OffsetMs` od otevření formuláře. Řádky jen přibývají, nikdy se nemění ani nemažou.
2. **Prohlížeč posílá dávky.** Skript sbírá události do fronty a každé 2 s je pošle. Při odeslání formuláře nebo
   odchodu ze stránky použije `navigator.sendBeacon`, který doručí i ve chvíli, kdy prohlížeč stránku zavírá.
3. **Idempotence.** Každá událost má `EventId` z prohlížeče. Když dávka selže a jde znovu (nebo odejde fetchem
   i beaconem), server už uložené přeskočí. Stejný princip jako inbox z kroku 6, jen jednodušší: stačí unikátní index.
4. **Události serveru.** Chybu validace (`ValidationFailed`) a vznik objednávky (`OrderSubmitted`) zapisuje stránka
   objednávek, ne prohlížeč (ten by mohl podvrhnout cizí číslo objednávky – služba takové typy od prohlížeče odmítne).
   `OrderSubmitted` je **spojka** mezi proudem a číslem objednávky. Ordering o sledování vůbec neví.
5. **Projekce = fold.** `OrderJourneyProjection` začne s prázdným stavem a pro každou událost v pořadí zavolá `Apply`.
   Jako zůstatek účtu z výpisu pohybů. Psaní do jednoho pole (desítky událostí) složí do jednoho kroku
   „Vyplnil „Jméno zákazníka“: „Jan Novák““, výběr ze seznamu je vždy samostatný krok.
   Souhrn: doba vyplňování, neúspěšná odeslání, kolikrát se které pole měnilo, čas strávený v polích, počet kliků.

**Pořadí podle času formuláře, ne příchodu.** Beacon při odeslání může předběhnout pomalejší fetch. Řadí se proto
podle `OffsetMs` (hodiny prohlížeče), ne podle toho, kdy dávka dorazila. Po neúspěšném odeslání pošle server
čas zpátky (`data-journey-offset`) a skript na něj navazuje, takže oprava chyby je ve **stejném** proudu.

**Projekce počítaná při čtení vs. uložená.** Kopie katalogu v Orderingu (krok 10) je projekce **uložená** do tabulky –
handler ji průběžně aktualizuje. Průběh objednávky se počítá **při čtení** z proudu. Je to jednodušší a změna projekce
(nový souhrn) hned platí i pro staré objednávky. Uložená projekce se vyplatí, až bude dotazů hodně (statistiky přes
všechny objednávky).

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `src/MiniShop.Domain/Journeys/JourneyEvent.cs`, `JourneyEventType.cs` | událost proudu (neměnná), typy akcí, `IsServerOnly` |
| `src/MiniShop.Application/Journeys/IJourneyService.cs`, `JourneyService.cs` | zápis dávek (idempotentní, max. 200), události serveru, průběh podle čísla objednávky |
| `src/MiniShop.Application/Journeys/OrderJourneyProjection.cs` | projekce (fold): časová osa + souhrn |
| `src/MiniShop.Application/Journeys/JourneyContracts.cs` | `RecordJourneyEventRequest`, `OrderJourneyDto`, kroky, souhrn, události pro přehrání |
| `src/MiniShop.Application/Journeys/IJourneyEventRepository.cs`, `IJourneysUnitOfWork.cs` | „event store“ – jen přidávání a čtení |
| `src/MiniShop.Infrastructure/Journeys/*` | `JourneysDbContext` (journeys.db, bez outboxu/inboxu), konfigurace, repozitář, registrace, inicializace DB |
| `src/MiniShop.Web/Controllers/JourneysController.cs` | `POST /api/journeys/{journeyId}/events`, `GET /api/journeys/orders/{orderId}` |
| `src/MiniShop.Web/Pages/Orders/Journey.cshtml(.cs)` | stránka průběhu: souhrn a časová osa, vstup = číslo objednávky |
| `src/MiniShop.Web/wwwroot/js/order-form-tracker.js` | sledování formuláře v prohlížeči |
| `tests/MiniShop.Tests/Application/JourneyServiceTests.cs` | projekce, pořadí dávek, idempotence, zakázané typy, 404 |
| `tests/MiniShop.Tests/Domain/JourneyEventTests.cs` | pravidla události |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Web/Pages/Orders/Index.cshtml(.cs)` | skrytá pole `JourneyId`/`JourneyOffsetMs`, zápis `ValidationFailed`/`OrderSubmitted`, pole „Průběh zakládání objednávky č.“, odkaz „Průběh zadání“ u objednávky, `reloadWhenIdle()` |
| `Web/Pages/Shared/_Layout.cshtml` | sekce `Scripts` |
| `Web/Program.cs`, `appsettings.json` | connection string `Journeys`, `AddJourneysInfrastructure`, `JourneysDatabaseInitializer` |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | `AddJourneysModule()` |
| `tests/.../Architecture/ModuleBoundariesTests.cs` | modul Journeys: nesmí znát Ordering ani Inventory a naopak |

## Rozhodnutí v tomto kroku

- **Samostatný modul `Journeys` s vlastní `journeys.db`.** Jiná oblast než objednávky, jiná životnost dat
  (sledování se dá kdykoli smazat bez dopadu na objednávky). Existující databáze se nemění.
- **Bez outboxu a inboxu.** Modul neposílá ani nepřijímá zprávy. Proto `JourneysDbContext` nedědí z `ModuleDbContext`
  a idempotenci řeší unikátní `EventId`.
- **Ukládá se skutečně napsaný text** (výukový projekt bez reálných dat). V provozu by jméno a e-mail byly osobní údaje
  (GDPR) – maskovaly by se už v prohlížeči.
- **Každý znak = událost s celou hodnotou pole.** Pro přehrání (12B) je to nejjednodušší (žádné skládání rozdílů)
  a formulář má jen pár krátkých polí.
- **Kliky jen na tlačítka a odkazy**, ne souřadnice myši. Klik do pole zachytí `FieldFocused`.
- **Sledování nesmí rozbít objednávku.** Když zápis do journeys.db selže, objednávka platí dál a jen se zaloguje varování.
- **Automatické obnovení stránky** (čekající objednávky, načítání katalogu) se zastaví, jakmile uživatel začne
  vyplňovat. Dřív obnovení smazalo rozepsaný text – a teď by navíc rozdělilo proud na kusy.
- **Prázdné proudy se neposílají.** Stránka otevřená bez jediné akce (např. automatické obnovení) nic neuloží.
- **Projekce při čtení** (viz výše).

## Známá omezení

- Sleduje se jen formulář na stránce Objednávky. Objednávky přes REST API (`POST /api/orders`) průběh nemají (404 s vysvětlením).
- Události přijaté těsně po odeslání (beacon) se uloží, i když objednávka už vznikla – jsou v proudu podle času, to je v pořádku.
- Dvě souběžné stejné dávky (fetch + beacon ve stejný okamžik) můžou narazit na unikátní index → jedna skončí chybou 500.
  Data jsou v pořádku (uloží je ta druhá).
- Proudy bez objednávky (uživatel odešel) zůstávají v databázi, nic je nečistí. Stejně jako inbox.
- Čas začátku (`StartedAtUtc`) je odhad: čas příchodu události na server minus `OffsetMs`.
- `POST /api/journeys/...` nemá ochranu proti zneužití (kdokoli může zapsat události do libovolného proudu).
  Číslo objednávky ale podvrhnout nejde – to zapisuje jen server.

⚠️ **Nová databáze:** `src/MiniShop.Web/journeys.db` vznikne sama při startu. Existující `ordering.db` a `inventory.db`
se nemění, **mazat nic nemusíte**.

## Jak ověřit

1. `dotnet test` – projde všech 154 testů. Nové:
   - `Prubeh_SlozeniCasoveOsyASouhrnu`: tři napsané znaky = jeden krok, dva výběry produktu = dva kroky, čas v polích, souhrn.
   - `Prubeh_DavkyMimoPoradi_RadiSePodleCasuFormulare`, `Zapis_OpakovanaDavka_NicNezdvoji`,
     `Zapis_NeuspesneOdeslani_JeVSouhrnu`, `Zapis_TypKteryProhlizecNesmiPoslat_JeChyba` (4×),
     `Zapis_PrilisVelkaDavka_JeChyba`, `Prubeh_ObjednavkaBezProudu_NotFound`,
   - `JourneyEventTests` (4×), `ModuleBoundariesTests` (4 nové kombinace modulů).
2. Spusťte `dotnet run --project src/MiniShop.AppHost` a otevřete `https://localhost:7180/Orders`.
3. Vyplňte formulář, schválně zadejte špatný e-mail, odešlete, opravte a odešlete znovu.
4. U nové objednávky klikněte na **Průběh zadání** (nebo zadejte číslo do pole „Průběh zakládání objednávky č.“).
   Uvidíte souhrn a časovou osu včetně chyby validace.
5. Stejná data jako JSON: `https://localhost:7180/api/journeys/orders/{číslo}` (sekce `events` je podklad pro 12B).

Ověřeno i naživo (bez Aspire, HTTP požadavky místo prohlížeče): dávky → 204, opakovaná dávka nic nepřidala,
`OrderSubmitted` od klienta → 400, chybný e-mail → `ValidationFailed` a stránka pokračovala ve stejném proudu
(`data-journey-offset="3002"`), po opravě objednávka vznikla a průběh ukázal 14 kroků z 18 událostí.
Samotný skript v prohlížeči (kliky, psaní) je ověřený v Chrome v kroku 12B – tam se také ukázalo, že chybí
zablokované odeslání prohlížečem (doplněna událost `FieldInvalid`).

## Vyzkoušejte sami

1. **Podívejte se do proudu.** Otevřete `journeys.db` v DB Browseru: tabulka `JourneyEvents`, u psaní jména uvidíte
   řádek pro každý znak („J“, „Ja“, „Jan“…). Projekce z nich udělala jediný krok.
2. **Změňte projekci, ne data.** Upravte text kroku v `OrderJourneyProjection.Apply` (např. „Klikl na“ → „Kliknutí:“).
   Po restartu se změní průběh i **starých** objednávek – proud zůstal stejný, jen se jinak promítá.
3. **Nová statistika.** Přidejte do souhrnu „počet smazání pole“ (`FieldChanged` s prázdnou hodnotou). Nepotřebujete
   žádnou změnu databáze – informace v proudu už je.
4. **Idempotence v praxi.** V DevTools prohlížeče (Network) najděte `POST …/events` a pošlete ho znovu
   (Replay/„Opakovat“). Počet řádků v `JourneyEvents` se nezmění.
5. **Výpadek API.** Zastavte službu `web` v Aspire během vyplňování a znovu ji pusťte. Skript dávky drží ve frontě
   a pošle je, až server odpoví.
