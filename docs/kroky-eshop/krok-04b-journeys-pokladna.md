# E-shop krok 4B: Průběh objednávky v pokladně

Sledování průběhu objednávky (modul Journeys z EDA kroku 12) se přesunulo z formuláře „Nová objednávka“ na pokladnu.
Starý formulář na stránce Objednávky a tlačítko „Objednat“ ve výloze zmizely – objednává se jen přes košík a pokladnu.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 4A](krok-04a-pokladna.md).

## Jak to funguje

Přirovnání: archiv vyplněných tiskopisů. Úřad zavede nový tiskopis, ale staré vyplněné ve skříni nikdo nepřepisuje –
jen k nim přiloží správnou šablonu pro čtení. Proud událostí funguje stejně: uložené akce se nemění, projekce a
přehrávač je jen umějí číst ve správném „formuláři“.

```
 GET /Checkout ── nový JourneyId ──► <form data-journey-id=… data-journey-form="checkout">
   order-form-tracker.js (beze změny typů událostí):
     FormOpened(target = "checkout") · FieldChanged(Input.ShippingMethod, „Doprava“, Courier, „Kurýr na adresu“)
     · FieldChanged(Input.PostalCode …) · Clicked · SubmitClicked
     ── dávka každé 2 s ──► POST /api/journeys/{id}/events ──► journeys.db (jen INSERT)

 POST /Checkout (JourneyId, JourneyOffsetMs)
   chyba (PSČ, změněné ceny, košík se změnil) → ValidationFailed (hlášky)   – stránka pokračuje ve stejném proudu
   objednávka vznikla                         → OrderSubmitted (OrderId)   – proud je spojený s objednávkou

 /Orders/Journey/42, /Orders/Replay/42
   OrderJourneyProjection: Form = FormOpened.Target ?? "order-form" (staré proudy jméno nemají)
                           „Otevřel pokladnu“ / „Otevřel formulář objednávky“
   Replay.cshtml: kopie pokladny, nebo kopie původního formuláře – podle Form
   journey-player.js: přepínač dopravy (stejný Target, rozhoduje hodnota), odeslání = [data-replay-submit]
```

- **Žádný nový typ události.** Pokladna se skládá z polí, přepínačů, kliků a odeslání – na to stačí typy z kroku 12.
  `JourneyEventType` se nezměnil (nové typy by šly jen na konec, ukládají se jako text).
- **Staré proudy se nepřepisují.** V produkci jsou proudy ze starého formuláře. Projekce pozná formulář podle
  `FormOpened.Target` – starý proud ho nemá, takže je to `order-form`. Přehrávač pro něj má pořád kopii starého
  formuláře. Kdybychom proudy „opravovali“ nebo mazali, přišli bychom o historii, kterou proud uchovává.
- **Přepínač dopravy srozumitelně.** Skript u přepínače uloží jako popisek název skupiny (`legend` = „Doprava“)
  a jako `ValueText` název možnosti bez ceny (`data-track-value`). Projekce ho tak ukáže jako výběr
  („Vybral „Doprava“: Kurýr na adresu“), ne jako psaní celého textu možnosti.
- **Jméno formuláře v `data-journey-form`, ne v `data-track`.** Skript zaznamenává kliky na prvky s `data-track`;
  kdyby ho měl celý formulář, každý klik dovnitř by se zapsal jako „klik na formulář“.
- **Ordering o Journeys pořád neví.** Spojení proudu s objednávkou zapisuje stránka pokladny (Web), ne `CheckoutService`.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `docs/kroky-eshop/krok-04b-journeys-pokladna.md` | Tento souhrn |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Web/Pages/Checkout/Index.cshtml(.cs)` | Proud `JourneyId`/`JourneyOffsetMs`, `ValidationFailed`/`OrderSubmitted` (chyba sledování jen do logu), `data-journey-form="checkout"`, `data-track-value` u přepínačů, načtení skriptu |
| `Web/wwwroot/js/order-form-tracker.js` | Sleduje `form[data-journey-id]`, `FormOpened` s jménem formuláře, přepínač: popisek skupiny a název možnosti |
| `Application/Journeys/JourneyContracts.cs`, `OrderJourneyProjection.cs` | `OrderJourneyDto.Form`, `JourneyForms` (`order-form`, `checkout`), „Otevřel pokladnu“ |
| `Web/Pages/Orders/Replay.cshtml`, `wwwroot/js/journey-player.js` | Kopie pokladny vedle kopie původního formuláře; přepínače, `data-replay-submit`, stránka bez seznamu produktů |
| `Web/Pages/Orders/Index.cshtml(.cs)` | Bez formuláře „Nová objednávka“ (handler `Create`, `?productId=`, sledování); odkaz **Nakupovat v obchodě** |
| `Web/Pages/Shop/Index.cshtml` | Bez tlačítka „Objednat“ – jen **Do košíku** |
| testy | `JourneyServiceTests` (proud z pokladny, starý proud), `CheckoutPageTests` (chyba + objednávka v proudu, přehrání ukáže pokladnu), `AuthorizationMatrixTests` (bez `/Orders?handler=Create`, stránka Objednávky bez formuláře), `AccountDeletionTests` |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | Journeys, Toky (Průběh formuláře, Pokladna, Vytvoření objednávky, Změna dostupnosti/produktu), Ordering, Accounts, historie |

## Rozhodnutí v tomto kroku

- **Staré proudy beze změny**, přehrávač drží kopii původního formuláře – historie zůstává čitelná.
- **Jméno souboru `order-form-tracker.js` zůstává** – pořád sleduje formulář objednávky (teď pokladnu).
- **REST API `POST /api/orders` beze změny** – sledování se ho netýká.
- **Hláška „katalog se načítá“ jen ve výloze**; obnovu katalogu má admin dál na stránce Objednávky.

## Známá omezení

- Po **Přijmout nové ceny** se pokladna načte znovu a začne nový proud – průběh objednávky ukáže jen část po přijetí cen.
- Souhrn košíku (řádky, ceny) se nesleduje ani nepřehrává – jen to, co zákazník v pokladně vyplňoval a kam klikal.
- Změny v košíku (stránka `/Cart`) se nesledují; sleduje se až pokladna.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. Jako zákazník: košík → **K pokladně** → přepni dopravu, vyplň adresu se špatným PSČ → **Objednat** (chyba) →
   oprav PSČ → **Objednat**.
2. Jako admin: **Objednávky** → u objednávky **Průběh zadání** – první krok „Otevřel pokladnu“, výběr dopravy,
   „Server formulář vrátil: PSČ musí mít 5 číslic.“, „Objednávka č. N vznikla“.
3. **▶ Přehrát** – kopie pokladny: přepínač dopravy se přepne, PSČ se píše znak po znaku, chyba, oprava, objednávka.
4. U starší objednávky (z doby před krokem 4B) **▶ Přehrát** – pořád kopie původního formuláře s produktem a množstvím.
5. Stránka Objednávky nemá formulář, výloha nemá „Objednat“.

### Produkce

1. Sloučit PR → CD nasadí (bez migrace).
2. Objednat přes pokladnu, jako admin přehrát průběh nové i některé starší objednávky.

## Vyzkoušejte sami

- Najdi v `journeys.db` (tabulka `JourneyEvents`) událost `FormOpened` nového a starého proudu. Čím se liší a proč
  to stačí, aby přehrávač zvolil správný formulář?
- Co by se stalo, kdybychom staré proudy „opravili“ – doplnili jim `Target = "order-form"` přímo v databázi? Je to
  v rozporu s tím, že proud je jen INSERT?
- Přidej do pokladny pole „Poznámka pro kurýra“. Co všechno musíš změnit, aby ho přehrávač ukázal? (Tip: `Replay.cshtml`.)
