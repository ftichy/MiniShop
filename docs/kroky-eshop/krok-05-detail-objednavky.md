# E-shop krok 5: Detail objednávky a historie stavů

Zákazník po objednání skončí na detailu objednávky `/Orders/{id}`. Vidí položky, dopravu, adresu a hlavně časovou
osu stavů: kdy objednávka vznikla, kdy ji sklad potvrdil (nebo proč ji zamítl), kdy byla vyřízená nebo zrušená.
Historie se ukládá přímo s objednávkou.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 4A](krok-04a-pokladna.md). Na detail naváže
krok 6 (expedice – další stavy) a krok 7 (odkaz z e-mailu).

## Jak to funguje

Přirovnání: sledování zásilky u přepravce. Zásilka nemá jen „aktuální stav“, ale seznam zastávek s časem:
*přijato 9:12 → na depu 14:30 → doručeno*. Každou zastávku zapíše ten, kdo zásilku zrovna převzal.

```
 Order.Confirm(čas)                                          ordering.db, JEDNA transakce
   ├─ EnsureStatus(Pending)          ── jinak DomainException, nic se nezmění
   └─ ChangeStatus(Confirmed, čas)   ── Status = Confirmed     ─► UPDATE Orders
        └─ _statusHistory.Add(…)     ── nový řádek historie    ─► INSERT OrderStatusChanges
   Raise(OrderConfirmed)             ── doménová událost       ─► INSERT OutboxMessages

 /Orders/{id} ─► OrderService.GetDetailAsync(id, OrderAccess)
                   ├─ cizí / neexistující ─► NotFoundException ─► 404
                   └─ OrderDetailDto(OrderDto, historie podle Id) ─► časová osa
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Historie stavů** | seznam přechodů objednávky s časem | `OrderStatusChange` (stav, `ChangedAtUtc`, `Note`) v tabulce `OrderStatusChanges` |
| **Invariant agregátu** | pravidlo, které musí platit po každé změně | „poslední řádek historie = aktuální stav“ – hlídá jediná metoda `ChangeStatus` |
| **Projekce** | stav dopočítaný z proudu událostí | tady záměrně **ne** (viz Rozhodnutí); ukázka projekce je modul Journeys |

**Proč v agregátu, ne projekcí?** Doménové události objednávky se nikam trvale neukládají. Outbox je jen „pošta“:
zprávu doručí a označí jako zpracovanou, archivem událostí není. Projekce by proto potřebovala nové úložiště
událostí a navíc by mohla zaostávat za stavem. Řádek historie vzniká ve stejné transakci jako změna stavu,
takže se od stavu nikdy nerozejde.

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Domain/Orders/OrderStatusChange.cs` | řádek historie (vzniká jen uvnitř `Order`) |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_OrderStatusHistory.cs` | tabulka `OrderStatusChanges` + doplnění historie existujícím objednávkám |
| `src/MiniShop.Web/Pages/Orders/Detail.cshtml(.cs)` | stránka `/Orders/{id}`: položky, doprava, adresa, časová osa, zrušit / vyřídit, pro admina odkazy na průběh zadání |
| `docs/kroky-eshop/krok-05-detail-objednavky.md` | tento souhrn |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs` | `StatusHistory`, privátní `ChangeStatus` – volá ho konstruktor, `Confirm`, `Reject` (s důvodem), `Complete`, `Cancel` |
| `Infrastructure/Ordering/Configurations/OrderConfiguration.cs` | vazba `StatusHistory` (privátní pole), `OrderStatusChangeConfiguration` |
| `Infrastructure/Ordering/OrderRepository.cs` | `GetByIdAsync` načte i historii (seznam ne) |
| `Application/Ordering/OrderContracts.cs`, `OrderMapping.cs`, `IOrderService.cs`, `OrderService.cs` | `OrderStatusChangeDto`, `OrderDetailDto`, `ToDetailDto`, `GetDetailAsync` |
| `Web/Pages/Orders/Index.cshtml` | číslo objednávky = odkaz na detail |
| `Web/Pages/Checkout/Index.cshtml.cs` | po objednání přesměrování na detail nové objednávky (dřív seznam) |
| testy | `OrderTests` (historie), `OrderServiceTests` (celá sága, zamítnutí, cizí = NotFound), `DatabaseMigrationTests` (doplnění historie), `OrderOwnershipTests` (stránka detailu), `CheckoutPageTests`, `AuthorizationMatrixTests` |
| příručka | `architektura/moduly/ordering.md`, `architektura/stavove-diagramy.md`, `toky/pokladna.md`, `historie.md` |

## Rozhodnutí v tomto kroku

- **Historie v agregátu, ne projekce** – je konzistentní se stavem (jedna transakce) a jednoduchá. Viz výše.
- **Jediné místo změny stavu** – `ChangeStatus` nastaví `Status` a přidá řádek. Nikdo zvenku historii nezmění
  (`IReadOnlyCollection`), nepovolený přechod nepřidá nic.
- **Staré objednávky bez vymyšleného času** – migrace jim zapíše vznik (`CreatedAtUtc`) a aktuální stav s `ChangedAtUtc = NULL`;
  stránka napíše „čas neznámý (starší objednávka)“. Odhadovat čas potvrzení by bylo zavádějící.
- **Pořadí podle `Id`** – pořadí zápisu. Čas by nestačil: přechody ve stejné milisekundě i řádky bez času.
- **Historie bez osobních údajů** – poznámka je jen důvod zamítnutí ze skladu, anonymizace účtu ji nemaže.
- **Seznam historii nenačítá** – `GetAllAsync` ji nepotřebuje; nový řádek EF uloží i bez načtení starých.
- **REST API beze změny** – `GET /api/orders/{id}` vrací dál `OrderDto`; historie zatím jen na stránce (nový kontrakt API až bude potřeba).
- **Zrušit / Vyřídit na detailu posílají na stránku Objednávky** – handlery a kontrola práv zůstávají na jednom místě.
- **Detail se sám obnovuje, dokud čeká na sklad** – stejný `data-reload-when-idle` jako seznam (po pokladně tu zákazník čeká na potvrzení).

## Známá omezení

- Historie zná jen přechody stavů; e-maily, platby ani expedici zatím neukazuje (kroky 6–8 přidají další stavy).
- Neukazuje, **kdo** změnu udělal (zákazník, admin, sága) – stačilo by přidat sloupec, zatím není potřeba.
- Staré objednávky mají u posledního stavu neznámý čas.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu `Applying migration '…_OrderStatusHistory'`.
2. Jako zákazník objednej z košíku → pokladna přesměruje na `/Orders/{id}`: „Objednávka přijata, ověřujeme sklad“,
   stránka se sama obnoví a přibude „Sklad zboží zarezervoval – objednávka potvrzena“ s časem.
3. **Zrušit objednávku** na detailu → v seznamu zrušená; detail má třetí řádek historie.
4. Objednej víc kusů, než je skladem → historie: přijata → zamítnuta s důvodem.
5. Změň v adrese číslo na cizí objednávku → 404. Admin otevře jakoukoli, vidí i *Průběh zadání* a *▶ Přehrát*.
6. Starší objednávka (z doby před krokem 5) → dva řádky, druhý „čas neznámý (starší objednávka)“.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí (migrace vytvoří tabulku a doplní historii existujícím objednávkám).
3. https://minishop-tichy.cz/Orders → klik na číslo objednávky → časová osa.

## Vyzkoušejte sami

- Zkus v `Order.Confirm` nahradit `ChangeStatus(…)` přímým `Status = OrderStatus.Confirmed`. Který test spadne a proč?
- Proč se historie řadí podle `Id`, a ne podle `ChangedAtUtc`? (Napověda: řádky ze staré migrace.)
- Navrhni sloupec „kdo změnu provedl“ – kdo by ho naplnil u přechodů, které dělá sága?
- Jak by vypadala historie jako projekce z událostí (jako Journeys)? Co bys musel začít ukládat?
