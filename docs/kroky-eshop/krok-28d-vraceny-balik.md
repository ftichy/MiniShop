# E-shop krok 28D: Vrácený balík = vratka po částech (směr C, krok C4)

Plán: [Sklady-roadmap](../Sklady-roadmap.md). Poslední krok směru C. Dopravce nedoručí asi každou desátou zásilku. U rozdělené
objednávky to od kroku 28C znamenalo jen štítek „vrácen“ u balíku – peníze, sklad ani doklady se neupravily. Teď se vrácený
balík **kompenzuje sám**: Ordering z něj udělá **automatickou vratku po částech** a pustí stávající ságu vratky.
Sekvenční diagram: [Dělená objednávka](../prirucka/toky/delena-objednavka.md).

## Jak to funguje

```
ShipmentReturnedV1 (číslo zásilky balíku 1)
  → Order.ReturnParcelToSender → balík „vrácen“ + OrderReturn s položkami balíku (sleva poměrně, doprava ne)
  → OrderParcelReturnedToSender (doménová událost)
  → ReturnSagaOrchestrator: ReturnSaga.StartWithGoodsBack (rovnou Restocking – zboží už je zpátky)
                            + RestockReturnedGoodsV2 v téže transakci
  → sklad naskladní do skladu původu → ReturnedGoodsRestockedV2
  → kartou RefundPaymentV2 (částka balíku) → PaymentRefundedV2;  dobírka bez refundu
  → Order.CompletePartialReturn → ReturnCompletedV2
       → Invoicing: faktura je → dobropis za balík
                    faktura ještě není (dobírka) → odečíst z kopie (faktura po doručení zbytku jen za převzaté)
       → Reporting (tržba bez balíku), Reviews, Notifications (e-mail o vrácení)

Všechny balíky skončily: aspoň jeden doručen → doručeno; všechny vrácené → „vráceno dopravcem“ BEZ OrderReturnedToSenderV1
```

Teorie:

- **Kompenzace znovupoužitím:** nová kompenzace nepotřebuje nové odběratele. Vrácený balík je z pohledu peněz, skladu
  a dokladů totéž co vratka po částech (e-shop krok 22) – jen začíná o krok později (zboží už je u nás).
- **Kompenzovat jen jednou:** když se vrátí všechny balíky, `OrderReturnedToSenderV1` (krok 26D) se **neposílá** – vrátil by
  peníze i zboží podruhé. Objednávka jen změní stav.
- **Pořadí událostí mezi moduly:** vratka smí naskladnit jen vydané zboží. Sklad proto vydává rezervace s každým odeslaným
  balíkem (`ParcelShippedV1`), ne až s posledním.
- **Účetní pohled u dobírky:** nezaplacený vrácený balík nemá být na faktuře – Invoicing ho odečte z kopie dřív, než faktura
  vznikne. Zpráva se tak neopakuje do nekonečna (čekání na fakturu, která by u „vrátilo se všechno“ nikdy nevznikla).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Invoicing/Migrations/…_ReturnsBeforeInvoice.cs` | sloupec `BillableOrders.ReturnsBeforeInvoice` (`integer[]`, výchozí `'{}'`) |
| `docs/prirucka/toky/delena-objednavka.md` | sekvenční diagramy: rozdělení a odeslání po balících, vrácený balík |
| `tests/MiniShop.Tests/Domain/BillableOrderReturnTests.cs` | vratka před fakturou – faktura jen za převzaté, vše vrácené = nic |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Domain/Orders/Order.cs` | `StartParcelReturn` (automatická vratka), `BuildReturnLines` (společné se zákaznickou vratkou), konec po vrácení všech balíků |
| `src/MiniShop.Domain/Orders/Events/OrderParcelEvents.cs` | `OrderParcelReturnedToSender` |
| `src/MiniShop.Domain/Orders/Saga/ReturnSaga.cs` | `StartWithGoodsBack` |
| `src/MiniShop.Application/Ordering/Saga/ReturnSagaOrchestrator.cs` | start ságy vráceného balíku + naskladnění v jedné transakci |
| `src/MiniShop.Application/Inventory/FulfilReservationHandler.cs`, `Inventory.Api/Program.cs` | `ParcelShippedV1` → vydat rezervace skladu balíku |
| `src/MiniShop.Domain/Invoicing/BillableOrder.cs`, `Application/Invoicing/InvoicingHandler.cs`, konfigurace | `ApplyReturnBeforeInvoice` |
| `src/MiniShop.Infrastructure/Ordering/OrderingDomainEvents.cs`, `OrderingDomainEventReader.cs`, registrace (Web, testy) | nová událost |
| `tests/…/OrderServiceTests.Parcels.cs`, `Domain/OrderParcelTests.cs` | oba balíky vrácené celou cestou; jeden vrácený, druhý doručený |
| příručka: `moduly/shipping.md`, `toky/index.md`, `historie.md` | popis a diagram |

## Rozhodnutí

- **Automatická vratka místo nové kompenzační zprávy** – žádný nový kontrakt; Payments, Invoicing, Reporting, Reviews
  i Notifications fungují beze změny (Invoicing jen doplnil případ „ještě bez faktury“).
- **Doprava se u vráceného balíku nevrací** (jako u vratky po částech). Vrátí-li se všechny balíky, zůstává zaplacená doprava
  u karty nevrácená – stejně jako u vratky po částech; řeší podpora ručně.
- **Jednobalíková objednávka beze změny** – vrácení jako krok 26D (`OrderReturnedToSenderV1`).
- **Contract starých cest odložen:** `ShipmentCreated/FailedV1`, `CreateShipmentV1` a jejich zpracování zůstávají, dokud
  v produkci mohou ležet starší zprávy; odebrání je samostatná úklidová změna.

## Známá omezení

- Vrátí-li se všechny balíky, přehled tržeb objednávku nevidí jako „vrácenou dopravcem“ (chybí `OrderReturnedToSenderV1`),
  jen odečte obě vratky – v tržbě zůstane doprava.
- Produkt rozdělený do dvou balíků: naskladnění vratky bere vydané rezervace produktu, nemusí trefit sklad vráceného balíku.
- Zákazník dostane e-mail „vrácení dokončeno“ s položkami balíku – text je obecný (stejný jako u vratky po částech).

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `Baliky_VracenyDopravcem_*`, `OrderParcelTests`, `BillableOrderReturnTests`.
2. V produkci to přijde samo (asi 10 % zásilek dopravce vrátí): rozdělená objednávka s balíkem „Vrácen dopravcem“ má na detailu
   dokončenou vratku po částech, zboží je zpátky ve skladu, u karty se vrátila částka balíku.

## Vyzkoušejte sami

- Lokálně nastavte simulátoru dopravce `Carrier:Chaos:UndeliverableRate` na 1, udělejte rozdělenou objednávku a předejte
  oba balíky: po pár minutách má dvě vratky a stav „vráceno dopravcem“, ale jen jednou vrácené peníze za každý balík.
