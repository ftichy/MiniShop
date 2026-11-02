# E-shop krok 4A: Pokladna

Zákazník z košíku přejde do pokladny `/Checkout`, vybere dopravu (osobní odběr nebo kurýr na adresu) a objedná
celý košík najednou. Objednávka má tedy víc položek, cenu dopravy a doručovací adresu. Košík se vyprázdní ve stejné
transakci, ve které objednávka vznikne. Dál běží stejná sága jako dřív.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 3](krok-03-kosik.md). Krok 4B přesune
sledování průběhu (Journeys) z dnešního formuláře objednávky na pokladnu a starý formulář odstraní.

## Jak to funguje

Přirovnání: u pokladny v obchodě se zboží namarkuje, zaplatí a vozík se vyprázdní – nikdy jen půlka. Pokladní se
podívá na vozík, který před ní stojí: když mezitím někdo přihodil zboží nebo se změnila cena na cedulce, řekne to.

```
 /Cart ── [K pokladně] ──► GET /Checkout
   CheckoutService.GetAsync: košík + aktuální ceny z kopie katalogu, slevy, doprava (strategie), poslední adresa
   ◄── formulář se skrytým Input.CheckoutId = VERZE košíku (mění se při každé změně košíku)

 POST /Checkout (CheckoutId, doprava, adresa)
   CheckoutService.PlaceOrderAsync:
     1. objednávka s tímto CheckoutId už existuje?        → vrátit ji (dvojklik, F5 – idempotentní příkaz)
     2. new DeliveryAddress(…)                            → PSČ, povinné údaje (hodnotový objekt)
     3. cart.CheckOut(CheckoutId, katalog)                → stejná verze? nic staženého/vyprodaného?
                                                            ceny = PriceWhenAdded? → řádky, košík prázdný, nová verze
     4. new Order(…, CheckoutId) + AddItem (+ sleva) + SetShipping (cena ze strategie)
     5. SaveChanges = JEDNA transakce: Orders + Carts + Outbox[OrderPlaced]
          souběh → UPDATE Carts … WHERE CheckoutId = původní → 0 řádků (nebo unikátní index Orders.CheckoutId)
          → ConcurrencyConflictException → najít objednávku prvního požadavku a vrátit ji
   ◄── 302 /Orders „Objednávka č. N přijata, ověřujeme sklad…“
   … sága → ReserveStockV1 se všemi položkami → sklad zarezervuje všechno, nebo nic
```

- **Idempotentní příkaz.** Formulář nese ID odeslání (`CheckoutId`). Druhé odeslání se stejným ID nic nezaloží,
  vrátí první objednávku. Je to stejná myšlenka jako inbox u zpráv (krok 6 EDA), jen pro požadavek od člověka.
  Funguje i pro REST API: `POST /api/orders` s volitelným `checkoutId`.
- **Verze košíku.** `Cart.CheckoutId` se mění při každé změně obsahu (přidání, množství, odebrání, přijetí cen,
  objednání). Starý formulář pokladny po změně košíku v jiné záložce neprojde, takže zákazník nikdy neobjedná
  něco jiného, než viděl. Stejná hodnota slouží jako **concurrency token**: EF ukládá
  `UPDATE Carts … WHERE CheckoutId = původní` a souběžná změna selže, místo aby se přepsala.
- **Žádné tiché zdražení.** Když se cena liší od `PriceWhenAdded`, `Cart.CheckOut` objednávku nepustí. Pokladna
  ukáže upozornění a tlačítko **Přijmout nové ceny** (`Cart.AcceptCurrentPrices`). Pravidlo je v doméně, ne jen
  ve stránce, takže platí i pro změnu ceny mezi otevřením stránky a odesláním.
- **Jedna transakce.** Objednávka i košík jsou v `ordering.db`, takže „založit objednávku + vyprázdnit košík“
  je jeden `SaveChanges`. Proto byl košík v kroku 3 umístěn do Orderingu.
- **Doprava jako strategie.** `IShippingPriceCalculator` → `StandardShippingPriceCalculator` (osobní odběr zdarma,
  kurýr 99 Kč, od 1 000 Kč za zboží po slevách zdarma). Cena dopravy se v objednávce uloží jako snímek (jako ceny položek).
  Kontrakt `OrderPlacedV1` se nemění – jeho `TotalPrice` už je celková cena, teď včetně dopravy.
- **Adresa jako hodnotový objekt.** `DeliveryAddress` nemá identitu, je neměnná a platnost (PSČ = 5 číslic,
  povinné údaje) ověří už konstruktor. V databázi jsou to tři sloupce tabulky `Orders` (EF *owned type*).
  Je to osobní údaj: `Order.Anonymize` ji smaže, stránka `/Privacy` ji zmiňuje.
- **Výjimky EF zůstávají v infrastruktuře.** `ModuleDbContext.SaveChangesAsync` převede `DbUpdateConcurrencyException`
  a porušení unikátního indexu na `ConcurrencyConflictException` (`Application/Exceptions`). REST API ji vrací jako 409,
  stránka Košík jako hlášku „Košík se mezitím změnil v jiném okně“.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Orders/DeliveryAddress.cs`, `ShippingMethod.cs` | Hodnotový objekt adresy (ověření PSČ, formát), způsob dopravy |
| `src/MiniShop.Application/Ordering/Checkout/CheckoutService.cs`, `ICheckoutService.cs`, `CheckoutContracts.cs` | Pokladna: souhrn, přijetí cen, objednání (idempotence, jedna transakce, souběh) |
| `src/MiniShop.Application/Ordering/Pricing/IShippingPriceCalculator.cs`, `StandardShippingPriceCalculator.cs` | Ceník dopravy (strategie) |
| `src/MiniShop.Application/Ordering/Pricing/DiscountPolicyExtensions.cs` | `BestDiscount` – stejná sleva pro API i pokladnu |
| `src/MiniShop.Application/Ordering/Carts/CartMapping.cs` | Košík → DTO, sdílí košík a pokladna |
| `src/MiniShop.Application/Exceptions/ConcurrencyConflictException.cs` | Souběžná změna bez závislosti na EF Core |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_Checkout.cs` | Sloupce objednávky (doprava, adresa, `CheckoutId` + unikátní index), `Carts.CheckoutId` (ručně: náhodná verze pro existující košíky, `None` pro staré objednávky) |
| `src/MiniShop.Web/Pages/Checkout/Index.cshtml(.cs)` | Stránka pokladny |
| `docs/prirucka/toky/pokladna.md` | Sekvenční diagramy pokladny, dvojího odeslání a souběhu |
| `tests/…/Domain/CheckoutDomainTests.cs`, `Web/CheckoutPageTests.cs` | Adresa, doprava, `Cart.CheckOut`, ceník; formulář pokladny |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs` | `CheckoutId`, `ShippingMethod`, `ShippingPrice`, `DeliveryAddress`, `SetShipping`, `ItemsTotal`; `TotalPrice` s dopravou; `Anonymize` maže adresu |
| `Domain/Orders/Carts/Cart.cs`, `CartLine.cs` | `CheckoutId` (verze), `CheckOut`, `AcceptCurrentPrices` |
| `Application/Ordering/OrderService.cs`, `OrderContracts.cs`, `OrderMapping.cs`, `IOrderRepository.cs` | API s `checkoutId` (idempotence i při souběhu); `OrderDto` s dopravou a adresou; dotazy podle `CheckoutId` a poslední adresy |
| `Application/Ordering/Carts/CartService.cs` | Mapování přes `CartMapping` |
| `Infrastructure/Persistence/ModuleDbContext.cs` | Převod konfliktů EF na `ConcurrencyConflictException` |
| `Infrastructure/Ordering/Configurations/OrderConfiguration.cs`, `CartConfiguration.cs`, `OrderRepository.cs` | Owned type adresy, unikátní `CheckoutId`, concurrency token košíku; útrata zákazníka včetně dopravy |
| `Infrastructure/Ordering/EmailOrderNotifier.cs` | Doprava a adresa v e-mailu |
| `Web/Pages/Cart/Index.cshtml(.cs)` | Aktivní tlačítko **K pokladně**; hláška při souběžné změně |
| `Web/Pages/Orders/Index.cshtml`, `Privacy.cshtml`, `ErrorHandling/GlobalExceptionHandler.cs`, `Extensions/…` | Doprava a adresa u objednávky; adresa jako osobní údaj; 409 Conflict; registrace služeb |
| testy | `OrderServiceTests` (sekce Pokladna: 3 produkty přes ságu, dvojí i souběžné odeslání, změna ceny, starý formulář, nedostatek jednoho produktu, doprava zdarma, předvyplnění a anonymizace adresy, API s `checkoutId`), `AuthorizationMatrixTests` |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | Ordering, Accounts, toky (Pokladna, Vytvoření objednávky), slovníček (hodnotový objekt, idempotentní příkaz), historie |

## Rozhodnutí v tomto kroku

- **Rozdělení na 4A a 4B.** Starý formulář na stránce Objednávky a tlačítko „Objednat“ ve výloze zůstávají,
  dokud 4B nepřesune sledování průběhu (Journeys) na pokladnu – jinak by sledování mezitím přestalo fungovat.
- **Verze košíku místo jednorázového tokenu.** Jedna hodnota dělá tři věci: idempotenci, „objednávám, co vidím“
  a optimistickou souběžnost.
- **Vyprodané nebo stažené zboží pokladna odmítne** (zákazník ho musí odebrat), nevynechá ho potichu.
- **Pokladna neověřuje zásobu** – zná ji jen sklad. Nedostatek řeší sága zamítnutím s důvodem (který produkt chybí),
  sklad zarezervuje všechny položky, nebo žádnou.
- **Adresa jen pro kurýra**, u osobního odběru se neuloží (ani když ji formulář pošle). Předvyplní se z poslední
  objednávky kurýrem – žádná samostatná „kniha adres“.
- **Bez platby** – tlačítko „Objednat“, platba přijde v kroku 8.
- **Převod konfliktu ve sdíleném `ModuleDbContext`** – aplikační vrstva tak nemusí znát EF Core (bez balíčků).

## Známá omezení

- Ceník dopravy je pevně v kódu (`ApplicationServiceCollectionExtensions`), ne v konfiguraci.
- Pokladna nemá JavaScript (CSP) – pole adresy jsou vidět i při osobním odběru („jen pro kurýra“).
- Po chybě „Ceny se změnily“ a přijetí nových cen se zadaná adresa nevrátí (stránka se načte znovu z poslední objednávky).
- Starší objednávky mají dopravu „nezadáno“ a REST API dopravu zatím nezná.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu `Applying migration '…_Checkout'`.
2. Jako zákazník přidej do košíku tři produkty → **Košík** → **K pokladně**.
3. Vyber kurýra, vyplň adresu, **Objednat** → stránka Objednávky „přijata, ověřujeme sklad…“, za pár sekund
   potvrzená, u objednávky doprava a adresa; košík prázdný. V Mailpitu e-mail s dopravou a adresou.
4. Znovu do košíku → v pokladně je adresa předvyplněná.
5. Admin změní cenu produktu v košíku → pokladna ukáže upozornění, **Objednat** nejde, **Přijmout nové ceny** → jde.
6. Otevři pokladnu ve dvou záložkách, v jedné objednej, ve druhé klikni **Objednat** → přesměruje na stejnou objednávku.
7. Do košíku víc kusů, než je na skladě → objednávka zamítnutá s důvodem, sklad ostatních produktů se nezměnil.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí (migrace přidá sloupce, existujícím košíkům dá vlastní verzi).
3. https://minishop-tichy.duckdns.org/Cart → **K pokladně** → objednat; zkontrolovat e-mail.

## Vyzkoušejte sami

- V pokladně si přes nástroje prohlížeče prohlédni skryté pole `Input.CheckoutId`. Přidej v jiné záložce produkt
  do košíku a obnov pokladnu – změnilo se? Proč je to dobře?
- Zkus v `CheckoutService` přesunout `cart.CheckOut(…)` až za `SaveChangesAsync`. Co by se stalo při pádu mezi tím?
- Přidej `FreeShippingWeekendCalculator` (o víkendu doprava zdarma) – co všechno musíš změnit? (Jen registraci.)
- Najdi v kódu dvě pojistky proti souběžnému odeslání. Proč by jedna nestačila pro REST API?
