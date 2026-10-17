# Krok 10A: Modulární monolit – každý modul má vlastní data

Ordering a Inventory spolu už v kroku 9 mluvily jen zprávami, ale sdílely jednu databázi a tabulku `Products`.
Teď má každý modul **vlastní databázi** (`ordering.db`, `inventory.db`) s vlastním outboxem a inboxem.
Ordering zná produkty jen z **vlastní kopie katalogu** a **test architektury** hlídá, aby moduly na sebe
nesahaly ani v kódu. Pořád běží jeden proces, takže jde o modulární monolit. Vyjmutí skladu do samostatné
služby je krok 10B.

## Jak to funguje

Přirovnání: dva podniky v jedné budově, které zatím sdílely sklep. Teď má každý svůj sklep a svůj sešit
došlé a odeslané pošty. Do sousedova sklepa nesmí, domlouvají se jen dopisy. Ceník si objednávky
opisují z oběžníků, které sklad rozesílá.

```
            ordering.db                                              inventory.db
┌───────────────────────────────────────┐              ┌───────────────────────────────────────┐
│ Orders, OrderSagas, CatalogProducts   │              │ Products, StockReservations           │
│ OutboxMessages, ProcessedMessages     │              │ OutboxMessages, ProcessedMessages     │
└───────────────┬───────────────────────┘              └──────────────────┬────────────────────┘
   OutboxProcessor<OrderingDbContext>                     OutboxProcessor<InventoryDbContext>
                └──────── ReserveStockV1, ReleaseStockV1 ──────►│
                │◄─── StockReservedV1, …, ProductUpdatedV1 ─────┘
                       (RabbitMQ nebo InProcess)
```

**Proč vlastní outbox a inbox v každé databázi?** Outbox funguje jen tehdy, když se zpráva uloží ve stejné
transakci jako změna dat (krok 4). A transakce nepřekročí hranici databáze. Stejně tak inbox: odškrtnutí musí
být v téže transakci jako změna, kterou handler udělal.

**Kopie katalogu (read model).** Ordering potřebuje k objednávce název a cenu, ale databázi skladu číst nesmí:

1. Produkt v Inventory při založení nebo změně vyvolá doménovou událost `ProductChanged`, při smazání `ProductRemoved`.
2. `InventoryDbContext` ji při uložení rovnou převede na integrační `ProductUpdatedV1` / `ProductRemovedV1` v outboxu.
3. `CatalogProjectionHandler` v Orderingu podle ní aktualizuje tabulku `CatalogProducts`.
4. `OrderService` bere název a cenu z `CatalogProducts`.

Důsledek je **eventual consistency**: nový produkt jde objednat až po pár sekundách. Ověřeno naostro:

```
POST /api/products  {"name":"Webkamera", …}            → 201 (produkt 6)
POST /api/orders    [produkt 6] hned                    → 404 "Produkt s ID 6 není v katalogu."
… ProductUpdatedV1 doputuje do Orderingu …
POST /api/orders    [produkt 6] po 5 s                  → 202 → Confirmed
```

**Pořadí a duplicity.** Zprávy můžou přijít dvakrát (to vyřeší inbox) i v jiném pořadí (retry). `CatalogProduct`
si pamatuje čas poslední změny (`ChangedAtUtc`) a starší změnu ignoruje. Smazaný produkt se jen označí
(`IsRemoved`), aby ho pozdě doručená starší změna nevzkřísila.

**Dvě cesty překladu na integrační zprávy.** Teď jsou v kódu obě varianty z kroku 7:

| | Ordering | Inventory |
| --- | --- | --- |
| Kde | handler `PublishOrderIntegrationEventsHandler` | `InventoryDbContext.ToOutboxMessage` při uložení |
| Kdy | o kolo workera později (doménová zpráva → handler → integrační) | hned, v téže transakci |
| Proč | Ordering má i jiné doménové handlery (sága, notifikace) | Inventory žádné vlastní handlery nemá |

**Hranice v kódu** hlídá `ModuleBoundariesTests` (NetArchTest):
- `…Ordering` / `…Domain.Orders` nesmí odkazovat na `…Inventory` a naopak, v Domain, Application i Infrastructure,
- společné jádro (outbox, inbox, bus, `ModuleDbContext`) nesmí znát žádný modul,
- kontrakty nesmí záviset na ničem dalším z MiniShopu.

Ověřeno: dočasná třída v Orderingu, která sáhla na `Product`, test shodila
(„MiniShop.Application.Ordering sahá do modulu Inventory: …TempViolation“).

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `Contracts/Inventory/ProductUpdatedV1.cs`, `ProductRemovedV1.cs` | změny katalogu pro ostatní moduly |
| `Domain/Inventory/Events/ProductChanged.cs`, `ProductRemoved.cs` | doménové události produktu |
| `Domain/Orders/CatalogProduct.cs` | kopie produktu v Orderingu (název, cena, `IsRemoved`, `ChangedAtUtc`) |
| `Application/Ordering/IOrderingUnitOfWork.cs`, `IOrderingMessagePublisher.cs` | uložení / odesílání zpráv modulu Ordering |
| `Application/Inventory/IInventoryUnitOfWork.cs`, `IInventoryMessagePublisher.cs` | totéž pro Inventory |
| `Application/Ordering/ICatalogProductRepository.cs` | repozitář kopie katalogu |
| `Application/Ordering/Catalog/CatalogProjectionHandler.cs` | projekce `ProductUpdatedV1`/`ProductRemovedV1` → `CatalogProducts` |
| `Application/Inventory/ProductMapping.cs` | `Product` → `ProductDto` (dřív společné `MappingExtensions`) |
| `Infrastructure/Persistence/ModuleDbContext.cs` | společný základ: outbox + inbox + uložení doménových událostí (z `MiniShopDbContext`) |
| `Infrastructure/Ordering/OrderingDbContext.cs`, `OrderingInfrastructure.cs`, `OrderingMessagePublisher.cs` | databáze a registrace modulu Ordering (včetně `AddDomainEventHandler`) |
| `Infrastructure/Ordering/OrderingDomainEvents.cs` | doménová událost objednávky → řádek outboxu (z `OutboxMessageFactory`) |
| `Infrastructure/Ordering/CatalogProductRepository.cs`, `Configurations/CatalogProductConfiguration.cs` | kopie katalogu |
| `Infrastructure/Inventory/InventoryDbContext.cs`, `InventoryInfrastructure.cs`, `InventoryMessagePublisher.cs` | databáze a registrace modulu Inventory |
| `Infrastructure/Outbox/IDomainEventReader.cs` | čtení doménových událostí z outboxu – implementuje modul |
| `tests/.../Architecture/ModuleBoundariesTests.cs` | 10 testů architektury |
| `tests/.../Domain/CatalogProductTests.cs` | 4 testy kopie katalogu |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| **přesuny** (`git mv`) | Application: `Orders/`, `Services/`, `Contracts/`, `Mapping/`, `Pricing/` → `Ordering/` a `Inventory/`; Domain: `Products/Product.cs` → `Inventory/`; Infrastructure: `Repositories/`, `Notifications/`, konfigurace → `Ordering/` a `Inventory/`; `OutboxEventReader` → `Ordering/OrderingDomainEventReader`; `DbInitializer` → `DatabaseInitializer` |
| `Domain/Inventory/Product.cs` | čas v konstruktoru a `Update`, události `ProductChanged`, `MarkRemoved` |
| `Domain/Common/Entity.cs` | `RemoveDomainEvents<T>()` – při více změnách před uložením odejde jen poslední |
| `Domain/Orders/Order.cs` | `AddItem(CatalogProduct …)`, odebraný produkt nejde objednat |
| `Application/Ordering/OrderService.cs` | čte `ICatalogProductRepository` místo produktů skladu |
| `Application/Ordering/Pricing/*` | sleva počítaná z `CatalogProduct` |
| `Application/Inventory/ProductService.cs` | `TimeProvider`, `MarkRemoved` před smazáním |
| handlery a služby modulů | `IOrderingUnitOfWork` / `IInventoryUnitOfWork` a modulové publishery místo obecných |
| `Infrastructure/Outbox/OutboxProcessor.cs` | `OutboxProcessor<TDbContext>` (jeden na modul) + `IOutboxProcessor`; konstanty ve statické `OutboxProcessor` |
| `Infrastructure/Outbox/OutboxMessageFactory.cs` | jen obecné `Create(domainEvent, payload)` a `Create(integrationMessage)` |
| `Infrastructure/Inbox/InboxGuard.cs`, `IdempotentDomainEventHandler.cs` | inbox nad `ModuleDbContext` |
| `Infrastructure/Integration/IntegrationMessageRouting.cs`, `IntegrationMessageDispatcher.cs` | `IntegrationModule(name, DbContextType)` – inbox v databázi modulu |
| `Infrastructure/Integration/OutboxIntegrationMessagePublisher.cs` | abstraktní základ pro publishery modulů |
| `Infrastructure/DependencyInjection.cs` | `AddInfrastructure(orderingCs, inventoryCs)`, `AddIntegrationModule<TDbContext>` |
| `Web/Program.cs`, `appsettings.json` | dva connection stringy `Ordering` a `Inventory`, `DatabaseInitializer` |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | `AddOrderingModule` (vč. projekce katalogu), `AddInventoryModule` |
| `Web/BackgroundServices/OutboxWorker.cs` | prochází outboxy všech modulů |
| `Web/Controllers/*`, `Pages/*`, `_ViewImports.cshtml` | nové namespace |
| testy | dvě `InMemoryDatabase`; produkty přes `IProductService` + zpracování outboxu; `ProcessOutboxAsync` přes všechny moduly; `IdempotentHandlerTests` pracují s kopií katalogu |

## Rozhodnutí v tomto kroku

- **Moduly jako namespace a složky, ne nové projekty.** Hranice hlídá test architektury. Přesun do projektů přijde,
  až se modul skutečně odstěhuje (10B).
- **Modulová rozhraní `IOrderingUnitOfWork` / `IInventoryUnitOfWork`** místo obecného `IUnitOfWork`. Se dvěma
  databázemi by obecné rozhraní bylo dvojznačné a handler by mohl omylem uložit do cizí databáze.
- **Kopie katalogu nese jen název a cenu** („fat event“ `ProductUpdatedV1` s celým stavem, ne jen „cena se změnila“).
  Ordering se pak nemusí na nic doptávat. Zásoba v kopii není, o tu se stará sklad v sáze.
- **Ochrana pořadí přes čas změny**, ne přes číslo verze. Pro jeden zdroj s jedním workerem to stačí.
- **Doménové události přes outbox má jen Ordering**, proto `AddDomainEventHandler` žije v `OrderingInfrastructure`.
  Inventory své doménové události hned převádí na integrační.
- **UI skládá data z obou modulů.** Stránka Objednávky nabízí produkty ze skladu (se zásobou), objednávka ale
  vzniká z kopie katalogu. Proto čerstvě založený produkt pár sekund hlásí „není v katalogu“.
- **Stránka Produkty i API produktů zůstávají ve Webu**, ale volají jen modul Inventory.

## Známá omezení

- **Pořád jeden proces.** Když spadne aplikace, spadnou oba moduly. Samostatná služba je krok 10B.
- **Nový produkt jde objednat až po ~2–4 s.** Ve formuláři ho uvidíte hned, ale objednávka do té doby skončí chybou 404.
- ~~Request logging (Serilog) loguje ošetřené výjimky (404/400) jako 500 se stack tracem.~~ Opraveno po kroku 10A:
  `UseSerilogRequestLogging` je v `Program.cs` teď před (= vně) `UseExceptionHandler`, takže loguje až výsledný status.
- Platí omezení z kroků 5–9: jedna instance workera, retry bez backoffu, inbox se nečistí, consumery bez dead-letter fronty.

⚠️ Místo `minishop.db` vznikají `ordering.db` a `inventory.db`. Starý `src/MiniShop.Web/minishop.db` je možné smazat,
aplikace ho už nepoužívá.

## Jak ověřit

1. `dotnet test` – projde všech 110 testů. Nové jsou:
   - `ModuleBoundariesTests` (10×), `CatalogProductTests` (4×), nové `ProductTests` (4×),
   - `OrderServiceTests`: `NovyProdukt_JdeObjednatAzPoPromitnutiDoKatalogu`, `ZmenaCeny_NoveObjednavkyMajiNovouCenu_StareSeNemeni`,
     `SmazanyProdukt_NejdeObjednat`, `Moduly_MajiOddeleneDatabaze`,
   - `IntegrationMessageRoutingTests.ModulBezDatabaze_AplikaceNenastartuje`.
2. Spusťte RabbitMQ a aplikaci. Ve složce `src/MiniShop.Web` vzniknou `ordering.db` a `inventory.db`.
3. V DB Browseru for SQLite: `inventory.db` má `Products`, `ordering.db` má `CatalogProducts` se stejnými názvy a cenami.
   Každá databáze má svůj `OutboxMessages` a `ProcessedMessages`.
4. V management UI RabbitMQ má fronta `minishop.ordering` nově vazby i na `ProductUpdatedV1` a `ProductRemovedV1`.

## Vyzkoušejte sami

1. **Eventual consistency.** Na stránce Produkty založte produkt a hned ho zkuste objednat. Hláška bude
   „není v katalogu“. Za pár sekund to projde. V logu uvidíte `ProductUpdatedV1` cestou do `minishop.ordering`.
2. **Změna ceny.** Změňte cenu produktu. Nové objednávky mají novou cenu, staré si drží tu původní
   (snímek v `OrderItems`).
3. **Porušte hranici.** Do `OrderService` přidejte závislost na `IProductRepository` (sklad). Build projde,
   ale `ModuleBoundariesTests` spadne a řekne, která třída hranici porušila.
4. **Výpadek „brokeru“ pro katalog.** Zastavte RabbitMQ a změňte cenu produktu. Kopie v Orderingu zůstane stará,
   dokud broker nenastartujete. Ordering mezitím objednává za poslední známou cenu, ne s chybou.
