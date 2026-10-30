# MiniShop: dokončení e-shopu

30. 10. 2026 · František

MiniShop má dnes silnou „kostru“ – ságu, outbox, inbox, dvě služby, účty, cloud – ale jako obchod je chudý:
zákazník vybere **jeden** produkt ve formuláři na stránce Objednávky, admin objednávku jedním tlačítkem „vyřídí“
a tím proces končí. V 10 krocích z něj uděláme e-shop, jak ho zná každý: výloha → košík → pokladna →
platba → expedice → doručení, se zákazníkem informovaným e-mailem v každém kroku.

Každý krok je malý, po každém aplikace funguje a nasadí se do cloudu (CD nasazuje každé sloučení do `main`).
Dokument je zatím jen plán, do kódu se nic nemění.

## Výchozí stav – co už máme a co chybí

Při procházení kódu vyšlo najevo, že část zadání je hotová „pod kapotou“, jen to není vidět v UI:

| Oblast | Dnes | Chybí |
| --- | --- | --- |
| Objednávka s více položkami | Doména (`Order.AddItem`), API (`CreateOrderRequest.Items`) i kontrakty (`OrderPlacedV1.Lines`, `ReserveStockV1.Lines`) **už více položek umí**; sklad rezervuje „všechno, nebo nic“ | Formulář ve Webu posílá vždy jednu položku (`Pages/Orders/Index.cshtml.cs`) |
| Košík | – | Celý: výběr více produktů, změna množství, uložení mezi návštěvami |
| Výloha | Produkty jen jako `<select>` ve formuláři; kopie katalogu zná jen název a cenu | Stránka s nabídkou, popis, dostupnost |
| E-maily o stavu | `EmailOrderNotifier` posílá při Confirmed, Rejected, Completed, Cancelled | Potvrzení přijetí (Pending), odeslání zásilky, odkaz na detail, HTML podoba; notifikace jsou „přilepené“ k Orderingu |
| Workflow | Pending → Confirmed/Rejected → Completed/Cancelled | Expedice (balení, odeslání, číslo zásilky), platba, zrušení v Pending (odložený účty krok 4) |
| Sklad po vyřízení | Rezervace zůstane navždy „aktivní“ – sklad se nikdy nedozví, že zboží odešlo | Odepsání rezervace při odeslání |
| Doručení | – | Doručovací adresa, způsob dopravy |
| Změny schématu | `EnsureCreated` – změna = smazat `ordering.db` | **V produkci jsou skutečné objednávky** – mazat už nejde, potřebujeme migrace |

## Úvod: proč e-shop „dodělávat“ přes události

V e-shopu se přirozeně potkává několik **bounded contexts** (oblastí s vlastním jazykem a pravidly):

| Oblast | Ptá se | V MiniShopu |
| --- | --- | --- |
| Katalog / výloha | Co prodáváme a za kolik? | Inventory (zdroj), Ordering (kopie katalogu) |
| Košík | Co si zákazník zatím vybral? | **nový** – v modulu Ordering (krok 3) |
| Objednávka | Co si zákazník závazně objednal? | Ordering (`Order`) |
| Sklad | Máme to? Je to rezervované, odeslané? | Inventory |
| Platby | Je zaplaceno? | **nový** modul Payments (krok 8) |
| Notifikace | Komu, co a kdy napsat? | **nový** modul Notifications (krok 7) |

Přirovnání: košík je **nákupní vozík** v supermarketu – můžete ho libovolně měnit a nikoho to nezavazuje, zboží
v regálu zůstává. Teprve u **pokladny** (checkout) vznikne závazná objednávka, přepočítají se ceny a sklad
zboží zarezervuje. Expedice pak je **zadní sklad**, který o pokladně nic neví – jen dostane lísteček „zabalit
a odeslat objednávku 42“. A to, že vám přijde SMS „balík je na cestě“, zařizuje někdo, kdo jen **poslouchá**,
co se děje – nikdo mu nic přímo neříká.

### Základní pojmy

| Pojem | Význam | Příklad v MiniShopu |
| --- | --- | --- |
| Košík (cart) | Rozpracovaný, nezávazný výběr | `Cart` s řádky produkt + množství |
| Checkout | Převod košíku na objednávku – jediné místo, kde vzniká závazek | `CheckoutService` (krok 4) |
| Idempotentní příkaz | Opakované odeslání (dvojklik, F5) nevytvoří druhou objednávku | `CheckoutId` v košíku i objednávce |
| Stavový automat | Povolené přechody mezi stavy, vše ostatní = chyba | `Order` (Confirmed → Shipped …) |
| Read model / projekce | Data připravená pro čtení, plněná událostmi | dostupnost „Skladem“ v kopii katalogu |
| Verzování kontraktu | Změna zprávy = nová verze, stará dál funguje | `ProductUpdatedV2` (krok 2) |
| Event-carried state transfer | Událost nese data, která příjemce potřebuje, aby se nemusel ptát | `OrderPlacedV1` nese e-mail a položky |
| Webhook | Vnější systém nám pošle událost HTTP požadavkem | simulovaná platební brána (krok 8) |
| Kompenzace | Vrácení dřívějšího kroku, když pozdější selže | uvolnění rezervace, vrácení platby |
| Migrace schématu | Řízená změna databáze bez ztráty dat | EF Core migrace (krok 1) |

## Principy, kterých se držíme

**EDA**

1. **Ne všechno musí být zpráva.** Košík a objednávka žijí ve stejném modulu (Ordering) a stejné databázi – checkout
   je **jedna transakce** (založit objednávku + vyprázdnit košík). Zprávy jen tam, kde se překračuje hranice modulu.
2. **Událost = fakt v minulém čase** (`OrderShipped`), **příkaz = žádost** (`ReserveStockV1`). Nové stavy objednávky
   přinášejí nové události; kdo na ně reaguje (e-mail, sklad), Ordering nezajímá.
3. **Kontrakty se nemění, jen verzují** (`ProductUpdatedV2` vedle `V1`) – hlídají `IntegrationContractTests`.
4. **Každý handler je idempotentní** (inbox) a každá vnější akce (e-mail, platba) jde přes outbox.
5. **Eventual consistency přiznat v UI:** „Objednávka přijata, potvrzení přijde e-mailem“, „Dostupnost se může
   lišit o pár sekund“ – ne předstírat okamžitou pravdu.
6. **Osobní údaje v událostech jen tam, kde jsou nutné** – a kdo si je zkopíruje, musí umět zapomenout
   (anonymizace z účty kroku 9 se musí dostat i do nových modulů).

**SOLID a clean architecture**

1. **SRP** – `Cart` řeší výběr, `Order` závazek, `CheckoutService` převod mezi nimi, šablona e-mailu jen text.
2. **OCP** – nový stav objednávky = nová událost + nová šablona e-mailu (nová třída), bez úprav existujících
   handlerů. Nová platební metoda = nová implementace `IPaymentGateway`. Doprava a slevy = strategie
   (`IShippingPriceCalculator`, existující `IDiscountPolicy`).
3. **LSP** – simulovaná brána a (jednou) skutečná brána se chovají stejně z pohledu ságy, včetně chyb a timeoutů.
4. **ISP** – úzká rozhraní: `ICartRepository`, `IOrderEmailTemplate<TEvent>`, ne jedno velké `IShopService`.
5. **DIP** – doména a aplikace nevědí o SMTP, platební bráně ani o EF Core; vše přes rozhraní v `Application`.
6. **Bohatá doména** – pravidla jsou v entitách (`Cart.AddItem` hlídá maximum, `Order.Ship` hlídá stav),
   služby jen orchestrují. Čas parametrem z `TimeProvider`.
7. **Hranice modulů hlídá test** (`ModuleBoundariesTests`) – nové moduly Payments a Notifications se navzájem ani
   s Orderingem nereferencují.

## Jak to zapadá do architektury

```
              Web (7180)                                   Inventory.Api (7181)
 ┌──────────────────────────────────────────┐          ┌──────────────────────────┐
 │ Výloha ─► Košík ─► Pokladna               │          │ Inventory                │
 │            └── Ordering (Cart, Order) ────┼─ zprávy ─┤  rezervace, odepsání,    │
 │                 │        ▲                │ RabbitMQ │  dostupnost (V2)         │
 │                 │        │                │          └──────────────────────────┘
 │   Payments ◄────┘ sága ──┘ (krok 8)       │
 │   Notifications  ◄── poslouchá události   │
 │   Journeys (průběh pokladny)              │
 └──────────────────────────────────────────┘
```

- **Cart** je nový agregát v modulu Ordering (`Domain/Orders/Carts`, tabulky v `ordering.db`). Používá stejnou
  kopii katalogu jako objednávka, proto nepotřebuje žádnou novou zprávu.
- **Notifications** a **Payments** jsou nové moduly ve Webu (stejný vzor jako Journeys/Accounts), Payments
  s vlastním outboxem a inboxem (účastník ságy), Notifications jen s inboxem.
- **Sága objednávky** získá nové kroky (platba, odeslání) – nejzajímavější EDA část plánu.
- **Journeys** budou sledovat pokladnu místo dnešního formuláře (krok 4) – `Replay.cshtml` je ruční kopie
  formuláře, upravit spolu s ním.
- `POST /api/orders` zůstane (už dnes umí více položek), přibude jen `checkoutId` pro idempotenci.

## Přehled kroků

| Krok | Fáze | Co přinese | Nový nástroj / vzor | Cena |
| --- | --- | --- | --- | --- |
| 1. Migrace databází | A – příprava | Změny schématu bez mazání `ordering.db`/`inventory.db`/`journeys.db` | EF Core migrace | 0 |
| 2A. Výloha | B – nakupování | Stránka s nabídkou, popis, dostupnost „Skladem“, kategorie, hledání | verzování kontraktu (`V2`), read model, CQRS | 0 |
| 2B. Obrázky produktů | B | Obrázky ve výloze, upload v administraci | bezpečný upload, soubory na volume, cache | 0 |
| 3. Košík | B | Přidat, změnit množství, odebrat, košík přežije odhlášení | agregát, invarianty | 0 |
| 4. Pokladna a objednávka s více položkami | B | Checkout z košíku, adresa, doprava, změna ceny, žádné dvojí objednání | idempotentní příkaz, strategie | 0 |
| 5. Detail objednávky a historie stavů | C – vyřizování | Stránka objednávky s časovou osou pro zákazníka i admina | projekce | 0 |
| 6. Expedice | C | Balí se → Odesláno (číslo zásilky) → Doručeno, sklad odepíše rezervaci | stavový automat, souběžnost | 0 |
| 7. Notifikace jako samostatný odběratel | C | E-mail při každé změně stavu, HTML šablony, odkaz na detail | pub/sub, OCP, PII v událostech | 0 |
| 8. Platba (simulovaná brána) | D – peníze | Zaplatit / dobírka, timeout platby, uvolnění zboží | sága se 2 účastníky, webhook, HMAC | 0 |
| 9. Zrušení kdykoli před odesláním | D | Zrušení i v Pending a při čekání na platbu, vrácení peněz | kompenzace, optimistická souběžnost | 0 |
| 10. Opuštěný košík (volitelně) | E – navíc | Připomínka e-mailem po 24 h | zpožděná zpráva, události košíku | 0 |

---

## Fáze A: příprava

## Krok 1: Migrace databází

*Hotovo 30. 10. 2026 – souhrn v `docs/kroky-eshop/krok-01-migrace.md`. Produkční schéma ověřeno předem
(export jen struktury z VM, shoda s migrací u všech tří databází).*

**Teorie.** `EnsureCreated` vytvoří databázi jen jednou a o dalších změnách neví. Dokud byla data jen na zkoušku,
stačilo DB smazat. Teď jsou v produkci skutečné objednávky zákazníků – a téměř každý další krok mění schéma
(košík, adresa, historie stavů). **Migrace** je očíslovaný skript „z verze N na N+1“; aplikace si při startu
zjistí, které ještě neproběhly, a spustí je. Přirovnání: místo stavby nového domu při každé přestavbě kuchyně
máme stavební deník úprav.

**V MiniShopu:**

- Stejný postup jako u `users.db` (účty krok 6): `dotnet ef migrations add Initial` pro `OrderingDbContext`,
  `InventoryDbContext` a `JourneysDbContext` (`Ordering/Migrations`, `Inventory/Migrations`, `Journeys/Migrations`).
- **Baseline existující produkční DB:** tabulky už existují, takže první migrace se nesmí spustit „naostro“.
  `DatabaseInitializer` pozná DB vytvořenou přes `EnsureCreated` (chybí `__EFMigrationsHistory`) a zapíše
  `Initial` jako provedenou. Ověřit na kopii produkční zálohy (`restore.sh` / `backup.py` z cloudu krok 12).
- Testy dál nad SQLite v paměti – přes `Migrate()`, ať testy ověřují i migrace.
- `CLAUDE.md`: pravidlo „při změně schématu smazat DB“ nahradit „nová migrace“.
- Cloud: před nasazením ruční záloha (`sudo systemctl start minishop-backup`).

**Hotovo, když:** produkční `ordering.db` po nasazení obsahuje `__EFMigrationsHistory`, objednávky zůstaly,
a testovací migrace (např. nový sloupec) projde lokálně i v cloudu bez mazání.

## Fáze B: nakupování

## Krok 2: Výloha

*Rozděleno na 2A (výloha, kategorie, hledání – hotovo 30. 10. 2026, souhrn v `docs/kroky-eshop/krok-02a-vyloha.md`)
a 2B (obrázky). Detail produktu `/Shop/{id}` se ukázal jako zbytečný – karta ukazuje vše podstatné.*

**Teorie.** Kopie katalogu v Orderingu je **read model** – data připravená pro čtení, plněná událostmi ze skladu.
Výloha potřebuje víc než název a cenu: popis a dostupnost. Kontrakt `ProductUpdatedV1` měnit nesmíme
(někdo ho možná čte) → **nová verze `ProductUpdatedV2`**, sklad nějakou dobu posílá obě.
Dostupnost se mění při každé rezervaci – posílat ji v `ProductUpdated` by bylo „ukecané“ a míchalo by dvě
věci. Proto samostatná událost **`StockAvailabilityChangedV1`** a v kopii jen hrubý stav
(Skladem / Poslední kusy / Vyprodáno), ne přesné číslo – přesná zásoba patří skladu a zákazník ji nepotřebuje.

**V MiniShopu:**

- `ProductUpdatedV2` (+ `Description`), `StockAvailabilityChangedV1` (posílá sklad, jen když se **změní pásmo**
  dostupnosti – ne při každém kusu). `CatalogProduct` s popisem a dostupností, `CatalogProjectionHandler` zpracuje V2.
- `SyncCatalogV1` posílá nově V2 + dostupnost. `V1` sklad posílá dál (odstranění = samostatné rozhodnutí později).
- Nová veřejná stránka `/Shop` (`[AllowAnonymous]`, řádek v `AuthorizationMatrixTests`) – karty produktů.
  Dnešní dotaz Webu na sklad kvůli zásobě (`InventoryApiClient` ve formuláři) odpadne.
- **2A – kategorie a hledání:** entita `Category` ve skladu (jméno jde ve `ProductUpdatedV2`, přejmenování znovu
  oznámí produkty), úprava produktu v administraci; v kopii katalogu `SearchText` bez diakritiky, filtry v URL
  (text, kategorie, jen skladem), řazení, stránkování.
- **2B – obrázky:** upload v administraci → `PUT /api/products/{id}/image` → volume skladu `/data/images`
  (jen JPEG/PNG/WebP podle prvních bajtů, max. 1 MB, bez SVG, jméno souboru generuje server s otiskem obsahu);
  Web je zpřístupní přes `GET /media/products/{soubor}` (stáhne ze skladu se servisním klíčem, `Cache-Control:
  immutable`), ve zprávě jen `ImageFileName` (V2 ho už má). Složka obrázků do `backup.py`. Bez zmenšování obrázků.

**Hotovo, když:** nepřihlášený návštěvník vidí nabídku s popisem a dostupností, vyprodání produktu se ve výloze
projeví do pár sekund a výloha funguje i při výpadku skladu.

## Krok 3: Košík

**Teorie.** Košík je **agregát** s vlastními pravidly (invarianty): nejvýš 20 řádků, množství 1–100, stejný produkt
jen jednou (přidání = navýšení množství). Košík **neblokuje zboží** – rezervace vzniká až u pokladny; jinak by
deset lidí s plným košíkem vyprodalo sklad, aniž by cokoli koupili. Cena v košíku je **orientační** (vždy aktuální
z katalogu); závazná je až v objednávce (snímek v `OrderItem`, jako dnes).

Kde má košík žít? Možnost „vlastní modul a vlastní DB“ zní EDA-čistě, ale pokladna by pak musela koordinovat dvě
databáze (sága kvůli vyprázdnění košíku). Košík a objednávka mluví stejným jazykem a používají stejný katalog →
**patří do Orderingu** a pokladna je obyčejná transakce. Hranice modulu má kopírovat hranici konzistence.

**V MiniShopu:**

- `Domain/Orders/Carts/Cart` (`CustomerId`, `Lines`, `AddItem`, `ChangeQuantity`, `RemoveItem`, `Clear`,
  `UpdatedAtUtc`), `CartLine` (`ProductId`, `Quantity`, `PriceWhenAdded` – jen pro upozornění na změnu ceny).
- `ICartService` v `Application/Ordering` (DTO s aktuálními cenami z katalogu, řádek se staženým produktem
  označený „už není v nabídce“), `ICartRepository`, `CartRepository`, migrace.
- UI: tlačítko „Do košíku“ ve výloze, stránka `/Cart`, počet kusů v hlavičce. Košík jen pro přihlášené
  (objednávat stejně smí jen přihlášený `RequireCustomer`); **anonymní košík v cookie se sloučením po přihlášení**
  = volitelné rozšíření (3b).
- Doménové události košíku (`CartItemAdded` …) zatím nikdo neposlouchá – připraví krok 10.
- Smazání účtu (účty krok 9) smaže i košík.

**Hotovo, když:** zákazník přidá několik produktů, změní množství, odhlásí se, přihlásí a košík je pořád tam;
testy domény hlídají všechny invarianty.

## Krok 4: Pokladna a objednávka s více položkami

**Teorie.** Pokladna je jediné místo, kde nezávazný výběr přechází v závazek. Tři klasické problémy:

1. **Dvojí odeslání** (dvojklik, F5, opakování po výpadku sítě) → **idempotentní příkaz**: košík nese `CheckoutId`
   (Guid), objednávka si ho uloží s unikátním indexem. Druhý pokus se stejným ID vrátí už existující objednávku.
2. **Změna ceny mezi vložením a zaplacením** → při checkoutu se ceny přepočítají; liší-li se od `PriceWhenAdded`,
   zákazník dostane upozornění a musí potvrdit znovu (žádné tiché zdražení).
3. **Košík a objednávka nesmí rozejít** → založení objednávky a vyprázdnění košíku v **jedné transakci**
   (oba v `ordering.db`), `OrderPlaced` odejde přes outbox jako dnes.

**V MiniShopu:**

- `CheckoutService` (Application/Ordering): načte košík, ověří ceny, `Order` + `AddItem` pro každý řádek
  (`IDiscountPolicy` jako dnes), `cart.CheckOut()`, jeden `SaveChangesAsync`.
- Doručovací adresa (hodnotový objekt `DeliveryAddress` v `Order`) a způsob dopravy (`ShippingMethod`:
  osobní odběr / kurýr) s cenou přes strategii `IShippingPriceCalculator` (zdarma nad 1 000 Kč).
  Cena dopravy v `Order` → `TotalPrice` ji zahrne; `OrderPlacedV1` se nemění (`TotalPrice` už je celková).
- **Nový osobní údaj** (adresa) → `Order.Anonymize` ji smaže, stránka `/Privacy` ji zmíní (pravidlo účty krok 9).
- Stránka `/Checkout` (souhrn, adresa předvyplněná z poslední objednávky, doprava, „Objednat a zaplatit“).
  Dnešní formulář na `Orders/Index` zmizí, stránka zůstane jako „Moje objednávky“.
- Zamítnutí skladem uvede, **který produkt** chybí (sklad to dnes píše do `Reason` – stačí ho ukázat).
- Journeys: sledování se přesune na pokladnu (pole adresa, doprava, změna množství), úprava `Replay.cshtml`
  a `JourneyReplayProjection`; nové typy událostí jen na konec `JourneyEventType`.
- `POST /api/orders` přijme volitelné `checkoutId` (idempotence i pro API).

**Hotovo, když:** objednávka se třemi produkty projde ságou, dvojklik na „Objednat“ vytvoří jednu objednávku,
změna ceny v adminu se v pokladně projeví upozorněním a sklad zarezervuje všechny položky, nebo žádnou.

## Fáze C: vyřizování

## Krok 5: Detail objednávky a historie stavů

**Teorie.** Zákazník chce vědět „kde je moje objednávka“. Historie stavů je přirozeně **seznam událostí** –
každý přechod objednávky už událost vyvolává. Uložíme je jako jednoduchý záznam `OrderStatusChange`
(stav, čas, poznámka) přímo v agregátu – nejjednodušší řešení, které je konzistentní se stavem objednávky.
(Alternativa „projekce z proudu událostí“ je ukázaná v Journeys; tady by byla zbytečně složitá.)

**V MiniShopu:**

- `Order` při každém přechodu přidá `OrderStatusChange`; migrace doplní jeden záznam pro existující objednávky.
- Stránka `/Orders/{id}` – položky, adresa, doprava, časová osa stavů; zákazník jen svou (cizí = 404),
  admin jakoukoli. Odkaz ze seznamu objednávek (a v kroku 7 z e-mailu).
- Řádek v `AuthorizationMatrixTests`.

**Hotovo, když:** zákazník vidí časovou osu své objednávky, cizí objednávka vrací 404.

## Krok 6: Expedice

**Teorie.** Dnešní „Vyřídit“ přeskakuje celou expedici. Rozšíříme **stavový automat**:

```
Pending ──► Confirmed ──► Packing ──► Shipped ──► Delivered (= Completed)
   │            │            │
   └► Rejected  └────────────┴──► Cancelled   (po odeslání už zrušit nejde)
```

Nová pravidla jsou čistě doménová (`Order.StartPacking`, `Ship(carrier, trackingNumber)`, `MarkDelivered`).
Odeslání je důležitý fakt i pro sklad: **rezervace se mění v definitivní výdej**. Dnes rezervace po vyřízení
zůstává navždy aktivní – sklad nerozliší „zboží odešlo“ od „rezervace visí“.

Souběh: admin klikne „Odeslat“ ve stejné chvíli, kdy zákazník klikne „Zrušit“. Řešení je **optimistická
souběžnost** – `Order` dostane `Version` (concurrency token, jako `OrderSaga`), druhý zápis selže
a uživatel dostane „Objednávka se mezitím změnila“ (409).

**V MiniShopu:**

- `OrderStatus.Packing`, `OrderStatus.Shipped` (nové hodnoty na konec enumu), `Completed` = doručeno
  (kvůli kompatibilitě s `OrderCompletedV1`). Doménové události `OrderPackingStarted`, `OrderShipped`.
- Kontrakt `OrderShippedV1` (OrderId, dopravce, číslo zásilky) → Inventory `FulfilReservationHandler`:
  `StockReservation.Fulfil()` (rezervace „vydána“, ne uvolněna). Sága: `Reserved → Shipped → Completed`.
- `Order.Version` jako concurrency token, `DbUpdateConcurrencyException` → 409 / hláška na stránce.
- Admin stránka **Expedice** `/Admin/Fulfilment`: fronty „K zabalení“, „K odeslání“, „Na cestě“,
  hromadné akce; formulář pro číslo zásilky.
- Simulace doručení: tlačítko „Doručeno“ v adminu (skutečný dopravce by poslal webhook – viz krok 8).

**Hotovo, když:** objednávka projde všemi stavy, po odeslání nejde zrušit, rezervace na skladě je po odeslání
„vydaná“ a test souběhu (odeslat × zrušit) skončí jedním vítězem bez poškozených dat.

## Krok 7: Notifikace jako samostatný odběratel

**Teorie.** Dnes e-maily posílají handlery **uvnitř Orderingu** (`SendConfirmationHandler` → `EmailOrderNotifier`).
Každý nový stav znamená upravit Ordering. Čistší je **pub/sub**: Ordering jen oznamuje fakta (integrační události)
a modul **Notifications** si sám rozhodne, komu co napsat. Přidat SMS nebo push = nový odběratel, Ordering
se nezmění (OCP na úrovni architektury).

Druhé téma: **osobní údaje v událostech.** Notifications potřebuje e-mail a jméno. Buď je ponese každá událost
(jednoduché, ale e-mail se rozkopíruje do mnoha zpráv), nebo si je Notifications uloží jednou
z `OrderPlacedV1` a ostatní události nesou jen `OrderId`. Volíme druhé – a proto Notifications musí poslouchat
i **`CustomerAnonymizedV1`**, aby po smazání účtu zapomněl.

**V MiniShopu:**

- Nové integrační události: `OrderConfirmedV1`, `OrderRejectedV1`, `OrderPackingStartedV1`, (`OrderShippedV1`
  z kroku 6), `CustomerAnonymizedV1`. `PublishOrderIntegrationEventsHandler` je přeloží z doménových.
- Modul Notifications (`…Notifications` ve všech vrstvách, hostuje Web, `notifications.db` s inboxem a kopií
  příjemců, do `backup.py`). Handler na každou událost, šablona = třída `IOrderEmailTemplate<TEvent>`
  (text + jednoduché HTML, odkaz na `/Orders/{id}`, souhrn položek, u odeslání číslo zásilky).
- Nový e-mail „Objednávku jsme přijali“ (Pending) – zákazník ví hned, i když potvrzení skladem trvá pár sekund.
- `EmailOrderNotifier` a `Send…Handler` z Orderingu zmizí. `ModuleBoundariesTests` o Notifications.
- Odeslání přes `IEmailSender` jako dnes, do logu jen předmět.

**Hotovo, když:** každá změna stavu pošle právě jeden e-mail (i při opakovaném doručení zprávy), Ordering
neobsahuje žádný kód o e-mailech a po smazání účtu Notifications nemá e-mail zákazníka.

## Fáze D: peníze a výjimky

## Krok 8: Platba (simulovaná platební brána)

**Teorie.** Platba je druhý **účastník ságy** – a vnější systém, který neovládáme: odpovídá se zpožděním, někdy
vůbec, a výsledek pošle **webhookem** (HTTP požadavek na náš server). Webhook musí být **ověřený podpisem**
(HMAC sdíleným tajemstvím – jinak by kdokoli „zaplatil“ cizí objednávku) a **idempotentní** (brány posílají
opakovaně). Zboží držíme rezervované, ale jen omezeně dlouho – nezaplacená objednávka po 30 minutách
**vyprší** a sága rezervaci uvolní (kompenzace).

```
Pending ─rezervace─► AwaitingPayment ─PaymentSucceeded─► Confirmed ─► Packing ─► …
                          ├─PaymentFailed / 30 min──► Cancelled + ReleaseStock
Dobírka: Pending ─rezervace─► Confirmed (platba až při doručení)
```

**V MiniShopu:**

- Modul Payments (`…Payments`, Web, `payments.db`, outbox + inbox): `Payment` (částka, stav, `ExternalId`),
  příkaz `RequestPaymentV1`, události `PaymentSucceededV1`, `PaymentFailedV1`, `RefundPaymentV1` / `PaymentRefundedV1`.
- `IPaymentGateway` (DIP) + `FakePaymentGateway`: stránka `/Pay/{id}` s tlačítky „Zaplatit“ / „Zamítnout“
  / „Nic nedělat“ (timeout), která pošle podepsaný webhook na `/api/payments/webhook` (`[AllowAnonymous]`,
  ověření HMAC). Skutečná brána (např. GoPay, Stripe test mode) = jen jiná implementace – „kam dál“.
- `OrderSaga`: stav `AwaitingPayment` s vlastním termínem, akce `RequestPayment`; `OrderStatus.AwaitingPayment`.
- Způsob platby v pokladně: kartou (přes bránu) / dobírka (sága platbu přeskočí).
- Secret `PAYMENTS_WEBHOOK_SECRET` (`docs/secrets.md`, `deploy.yml`, `deploy.sh`), lokálně generuje Aspire.

**Hotovo, když:** zaplacená objednávka se potvrdí, zamítnutá nebo nezaplacená do 30 minut se zruší a zboží
je zpět na skladě, webhook se špatným podpisem vrátí 401 a opakovaný webhook nic nezdvojí.

## Krok 9: Zrušení kdykoli před odesláním

*Navazuje na odložený účty krok 4 – analýza souběhu je tam.*

**Teorie.** Zrušit jde dnes jen potvrzenou objednávku. Zákazník ale chce zrušit i čekající nebo nezaplacenou.
Potíž: odpověď skladu nebo brány může být „na cestě“. Sága proto zrušení **zaznamená a dožene**: přijde-li
pozdě rezervace, pošle `ReleaseStockV1`; přijde-li pozdě platba, pošle `RefundPaymentV1`. Stejný vzor MiniShop
už má pro rezervaci po timeoutu.

**V MiniShopu:**

- `Order.Cancel` povolí Pending, AwaitingPayment, Confirmed, Packing (ne Shipped a dál).
- `OrderSaga`: nové přechody pro zrušení v každém stavu + pozdní odpovědi → kompenzace (uvolnit zboží,
  vrátit peníze). Tabulka všech kombinací jako testy (stav × zpráva).
- E-mail o zrušení zmíní vrácení peněz (šablona v Notifications – Ordering se nemění).

**Hotovo, když:** testy pokrývají všechna pořadí (zrušení před/po rezervaci, před/po platbě, před timeoutem)
a zásoba i platby vždy sedí.

## Fáze E: navíc

## Krok 10: Opuštěný košík (volitelně)

**Teorie.** Klasická marketingová funkce je ve skutečnosti pěkný EDA vzor: **zpožděná reakce na „ticho“**.
Nereagujeme na událost, ale na to, že po `CartItemAdded` do 24 hodin nepřišel checkout.

**V MiniShopu:**

- Worker (jako `OrderSagaTimeoutWorker`, vlastní span) najde košíky neměněné 24 h, ne vyprázdněné,
  bez připomínky → doménová událost `CartAbandoned` → e-mail (Notifications, šablona).
- Jen se souhlasem zákazníka (zaškrtávátko v účtu, zmínka v `/Privacy`), nejvýš jedna připomínka na košík.

**Hotovo, když:** zapomenutý košík s povoleným souhlasem dostane jednu připomínku, bez souhlasu žádnou.

## Volitelně: kam dál

- **Slevové kódy** – další `IDiscountPolicy` (strategie), uplatnění v pokladně, limit použití (souběžnost!).
- **Kategorie, vyhledávání a filtrování** ve výloze; obrázky produktů.
- **Skutečná platební brána** (Stripe test mode, GoPay sandbox) – jen nová implementace `IPaymentGateway`.
- **Vrácení zboží / reklamace** – nový proces (sága) po doručení: žádost → přijetí na sklad → refundace.
- **Faktura PDF** po zaplacení (QuestPDF – ověřit licenci) jako příloha e-mailu.
- **Hodnocení produktů** jen od zákazníků, kteří produkt koupili (dotaz na doručené objednávky).
- **Event sourcing košíku** – stav košíku jako fold nad událostmi (Journeys už ten vzor ukazuje),
  analytika „co lidé vkládají a nekupují“.
- **CQRS přehled pro admina** – tržby za den, nejprodávanější produkty jako projekce z integračních událostí.

## Shrnutí nákladů

| Položka | Služba | Cena |
| --- | --- | --- |
| Košík, pokladna, expedice | vlastní kód | 0 Kč |
| E-maily | Gmail SMTP (už máme) | 0 Kč |
| Platební brána | simulovaná (volitelně Stripe/GoPay test mode) | 0 Kč |
| Nové databáze (`notifications.db`, `payments.db`) | SQLite na volume VM, zálohy v bucketu | 0 Kč |
| **Celkem** | | **0 Kč** |

## Postup práce na každém kroku

Stejně jako u EDA, cloudu a účtů: nejdřív shrnutí (teorie + co se změní v MiniShopu) a souhlas, pak malé změny,
build + testy, úprava příručky `/Docs` (nové moduly, toky, stavové diagramy, kontrakty), souhrn do
`docs/kroky-eshop/krok-NN-nazev.md` (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení,
Jak ověřit, Vyzkoušejte sami) a aktualizace tabulky v `CLAUDE.md`. Každý krok se nasadí do cloudu
(nové DB do `backup.py`, nové secrets do registru). Kroky mimo repozitář (GitHub secrets, ověření na VM) dělá
uživatel sám podle návodu.
