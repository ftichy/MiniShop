# E-shop krok 17: Event sourcing košíku

Košík už neukládá jen „jak teď vypadá“, ale **co se s ním dělo**. Zdrojem pravdy je proud událostí `CartEvents`
(„přidána myš“, „odebrána myš“, „objednáno“), stav agregátu `Cart` je jejich součet. Tabulky `Carts`/`CartLines`
zůstaly jako projekce pro rychlé dotazy. Navenek se košík ani pokladna nezměnily; přibyla historie košíku pro
zákazníka a statistika „co zákazníci z košíku odebírají“ pro admina.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md) („kam dál“ – event sourcing košíku).

## Jak to funguje

Přirovnání: výpis z bankovního účtu. Banka nedrží „zůstatek“, drží pohyby – zůstatek (i k libovolnému dni) z nich
spočítá a na novou otázku odpoví zpětně.

1. **Příkaz → pravidlo → událost → stav.** `Cart.AddItem` ověří pravidla (vyprodáno, 20 řádků, 1–100 ks), pak
   `Raise(new CartItemAdded(…))` → `Apply` změní stav. `Apply` je jediné místo, kde se stav mění (nová událost
   i přehrávání historie). Neplatná událost se nikdy nezapíše.
2. **Uložení.** `OrderingDbContext.SaveChangesAsync` vloží nezapsané události sledovaných košíků do `CartEvents`
   (verze 1, 2, 3…) ve stejné transakci jako projekci, objednávku a outbox.
3. **Načtení.** `CartRepository` načte projekci a události **od poslední „nuly“** (`CartCheckedOut`, `CartCleared`,
   `CartSnapshotTaken`) a `Cart.Rehydrate` stav přehraje. Pravdu mají události – projekce se podle nich srovná.
4. **Souběh.** Unikátní (`CartId`, `Version`): druhý zápis téže verze = `ConcurrencyConflictException`.
5. **Převzetí.** Košík bez událostí (starší než krok 17) nebo projekce změněná starší verzí kódu (postupné nasazení)
   → `CartSnapshotTaken` (`imported` / `resynced`) z projekce; změna zákazníka se neztratí.
6. **Nové pohledy = projekce při čtení** (`CartHistoryService`): historie na `/Cart`, statistika `/Admin/CartInsights`.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Orders/Carts/CartEvents.cs` | události košíku (`CartItemAdded`, `…Removed`, `CartCheckedOut`, `CartSnapshotTaken`…), `VersionedCartEvent` |
| `src/MiniShop.Application/Ordering/Carts/ICartEventStore.cs` | čtení událostí pro historii a statistiku |
| `src/MiniShop.Application/Ordering/Carts/CartHistoryService.cs` | historie košíku zákazníka, statistika pro admina (projekce při čtení) |
| `src/MiniShop.Infrastructure/Ordering/CartEventStore.cs` | `CartEventRecord` (řádek `CartEvents`), `CartEventSerializer` (JSON + jméno typu), čtení proudu od poslední „nuly“ |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_CartEvents.cs` | tabulka `CartEvents` (unikátní `CartId`+`Version`, `ON DELETE CASCADE`) |
| `src/MiniShop.Web/Pages/Admin/CartInsights/Index.cshtml(.cs)` | statistika košíků pro admina |
| `tests/MiniShop.Tests/Domain/CartEventSourcingTests.cs` | Given–When–Then nad událostmi, přehrání, snapshot, resync |

## Změněné soubory

- `Cart` – metody vyvolávají události (`Raise` → `Apply`), `Version`, `UncommittedEvents`, `Rehydrate`, `FromHistory`,
  `FromLastReset`, `IsReset`, `MarkEventsCommitted`; `CartLine.Restore` místo `Increase`.
- `CartRepository` – po načtení projekce přehraje události; `OrderingDbContext` – `CartEvents` a uložení událostí v `SaveChangesAsync`;
  `CartConfiguration` – `CartEventRecordConfiguration`, `Version`/`UncommittedEvents` se nemapují; `OrderingInfrastructure` – `ICartEventStore`.
- Web: `/Cart` – sbalitelná „Historie košíku“; menu admina *Košíky*; `/Privacy` – historie košíku; registrace `CartHistoryService`.
- Testy: `OrderServiceTests` (5× `KosikES_*`, smazání účtu smaže proud), `CartPageTests` (odebraný produkt je v historii),
  `AuthorizationMatrixTests`.
- Příručka: nová stránka EDA → Event sourcing košíku, EDA přehled, Proud událostí, modul Ordering, slovníček, historie.

## Rozhodnutí v tomto kroku

- **Event sourcing jen pro košík**, objednávka zůstává klasicky (tabulky + doménové události do outboxu). U košíku
  historie přináší hodnotu (co si zákazník rozmyslel), u objednávky ji už dává historie stavů a outbox.
- **Projekce zůstala** a ukládá se ve stejné transakci – odznak v hlavičce a hledání opuštěných košíků jsou SQL dotazy
  na každou stránku / každých 15 minut; z událostí by byly pomalé. Žádné „zaostávání“ projekce.
- **Jedna třída `Cart`** je agregát i řádek projekce (EF ji mapuje na `Carts`) – žádná paralelní struktura,
  `CartService`, `CheckoutService` i stránky beze změny.
- **Události nejdou do outboxu** – nejsou to zprávy pro jiné moduly; `CartAbandonedV1` zůstává.
- **Přirozený snapshot** (od objednání/vyprázdnění) místo ukládaných snapshotů – košík se po nákupu vždy vyprázdní.
- **Převzetí a resync snapshotem** místo migrace dat – migrace jen přidá tabulku (bezpečné pro postupné nasazení),
  starý košík se „vyfotí“ při první změně.
- **Smazání účtu smaže celý proud** (`ON DELETE CASCADE`) – právo na výmaz má přednost před „události se nemažou“.

## Známá omezení

- Historie a statistika začínají nasazením kroku 17 (starší změny košíku se neukládaly).
- Statistika se počítá při každém zobrazení ze všech událostí – při velkém provozu by potřebovala vlastní projekci.
- Události se nesmí přejmenovat; upcasting zatím není potřeba (v `CartEventSerializer` by se doplnil).
- Snapshot jen přirozený – košík se stovkami změn bez objednání by se načítal pomaleji.
- Snapshot při převzetí se uloží až s první změnou košíku (samotné zobrazení nic nezapisuje).

## Jak ověřit

- `dotnet test MiniShop.slnx` – `CartEventSourcingTests`, `OrderServiceTests` (KosikES_*, Kosik_*, SmazaniUctu_*),
  `CartPageTests`, `AuthorizationMatrixTests`; všechny původní testy košíku a pokladny beze změny.
- Lokálně: přidat, změnit, odebrat, objednat → `/Cart` *Historie košíku*; admin `/Admin/CartInsights`.
- V DB: `select "Version", "Type", "Payload" from ordering."CartEvents" order by "CartId", "Version";`

**Nasazeno 11. 11. 2026** (PR #79, postupné nasazení bez ručního kroku, tabulka `CartEvents` vznikla migrací při startu).
V produkci ověřeno: historie košíku (včetně převzetí staršího košíku snapshotem), objednávka z košíku a statistika
*Košíky* pro admina.

## Vyzkoušejte sami

1. Dejte do košíku myš a kabel, myš odeberte. Rozbalte *Historie košíku* – je tam i myš, kterou už v košíku nemáte.
2. Objednejte. V historii přibude „Objednáno“, další nákup začíná od nuly (načtení košíku přehraje jen nové události).
3. Jako admin otevřete *Košíky* – myš je mezi nejčastěji odebíranými produkty.
4. Otevřete košík ve dvou záložkách a v obou změňte množství – druhá dostane „košík se mezitím změnil“.
