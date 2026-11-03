# E-shop krok 9: Zrušení kdykoli před odesláním

Objednávku jde nově zrušit i tehdy, když ještě čeká na sklad (`Pending`) nebo na platbu (`AwaitingPayment`), ne jen
po potvrzení. Odpověď skladu nebo brány ale může být „na cestě“. Sága proto zrušení **zaznamená a dožene**:
pozdní rezervaci uvolní, pozdní platbu vrátí. Zároveň se zavírá díra z kroku 8: zrušená **zaplacená** objednávka
teď vrací i peníze, ne jen zboží. Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na
[krok 8](krok-08-platba.md) a nahrazuje odložený [účty krok 4](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: zrušíš objednávku v restauraci, když kuchař už možná vaří. Číšník to nemůže „vzít zpátky“ – zapíše si
to, a když jídlo přece jen přinese, vrátí ho do kuchyně. Zaplatil-li jsi předem, vrátí ti peníze.

```
 zrušení v ságe:
   AwaitingReservation ──OrderCancelled──► Cancelled ──StockReservedV1 (pozdě)──► ReleasingStock ──► Released
                                              └─ StockReservationFailedV1 / timeout ──► nic (není co vracet)
   AwaitingPayment ──────OrderCancelled──► ReleasingStock ──PaymentSucceededV1 (pozdě)──► RefundPaymentV1
   Reserved (kartou) ────OrderCancelled──► ReleasingStock + RefundPaymentV1   (dobírka jen ReleaseStockV1)

 orchestrátor před každým krokem:  objednávka zrušená, ale sága to neví?  → nejdřív OrderCancelled, pak odpověď
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Kompenzace** | nová akce, která ruší účinek předchozí (čas vrátit nejde) | `ReleaseStockV1`, `RefundPaymentV1` |
| **Zprávy mimo pořadí** | odpověď skladu/brány může k ságe dorazit dřív než událost o zrušení | `OrderSagaOrchestrator.StepAsync` dožene zrušení podle stavu objednávky |
| **Stav „zrušeno, čeká se“** | sága si pamatuje, že se má zachovat jinak, až odpověď přijde | `OrderSagaState.Cancelled` |
| **Optimistická souběžnost** | dva zápisy naráz – jeden vyhraje, druhý se zopakuje / dostane hlášku | `Order.Version` → `ConcurrencyConflictException` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| tento souhrn | dokumentace kroku |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs`, `OrderStatus.cs` | `Cancel` povolí `Pending`, `AwaitingPayment`, `Confirmed`, `Packing` |
| `Domain/Orders/Saga/OrderSaga.cs` | stav `Cancelled` (na konec enumu), `OrderCancelled` pro každý stav, `StockReserved` v `Cancelled` → uvolnit, akce `ReleaseStockAndRefundPayment` (+ `RefundRequested`) |
| `Application/Ordering/Saga/OrderSagaOrchestrator.cs` | před každým krokem dožene zrušení (`order.Status == Cancelled`), provedení akcí v `Execute`, nová akce posílá `ReleaseStockV1` + `RefundPaymentV1` |
| `Application/Payments/PaymentCommandHandlers.cs` | komentář – refund i pro zrušenou zaplacenou objednávku |
| `Web/Orders/OrderStatusLabels.cs`, `Pages/Orders/Index`, `Detail` | tlačítko Zrušit i u čekajících a nezaplacených, nová hláška |
| `Web/Pages/Pay/Index.cshtml(.cs)` | u zrušené objednávky žádná tlačítka, POST nic nezaplatí (`CanPay`) |
| `docs/prirucka/toky/zruseni-a-vyrizeni.md`, `architektura/stavove-diagramy.md`, `eda/saga.md`, `toky/platba.md`, `historie.md` | dokumentace |
| `tests/…/OrderSagaTests.cs`, `OrderServiceTests.cs`, `OrderTests.cs`, `Web/PaymentPageTests.cs` | tabulka stav × zpráva, celé cesty přes sklad a platby, stránka brány u zrušené objednávky |

Migrace není potřeba – stav ságy se ukládá jako text.

## Rozhodnutí v tomto kroku

- **Žádný nový kontrakt.** Zrušení během čekání na platbu neposílá „zruš platbu“ – stránka brány u zrušené
  objednávky platit nedovolí a kdyby platba přesto prošla (skutečná brána, webhook na cestě), vrátí ji sága
  dnešním `RefundPaymentV1`. Méně zpráv, stejný výsledek.
- **Zrušení dohání orchestrátor**, ne „čekání na pořadí“. `OrderCancelled` jde přes outbox a může přijít až po
  odpovědi. Orchestrátor si proto vždy načte objednávku – je-li zrušená, nejdřív posune ságu o `OrderCancelled`.
  Pozdější `OrderCancelled` pak už nic neudělá (stavový automat).
- **Nový stav `Cancelled`** místo nového „čekacího“ stavu s termínem – chová se jako `Rejected` po timeoutu
  (pozdní rezervace → uvolnit). Termín u něj neběží, timeout worker ho nehledá.
- **Zaplacená objednávka vrací peníze při zrušení** (`ReleaseStockAndRefundPayment`) – v kroku 8 to chybělo.
  `RefundRequested` hlídá, aby se peníze nevracely dvakrát.
- **E-maily beze změny** – „zrušeno“ a „vracíme peníze“ jsou dva samostatné e-maily ze dvou faktů; Ordering nic nepíše.
- **Zrušená zůstává `Cancelled`** i když pozdě přijde rezervace nebo platba – objednávka se „neoživí“.

## Známá omezení

- Platba zrušené objednávky zůstane v `payments.db` jako `Pending` (vyprší) – Payments o zrušení neví.
- Skutečná brána by měla dostat i „zruš platební stránku“ (void) – simulace to nepotřebuje.
- Admin ruší stejným tlačítkem jako zákazník, bez důvodu zrušení.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. **Zrušení čekající na platbu:** pokladna → **Kartou** → na detailu „Čeká na platbu“ klikni **Zrušit objednávku**
   → „Zrušená“, zboží zpět na skladu, e-mail „zrušeno“. Stránka `/Pay/{id}` napíše „Objednávka je zrušená“ bez tlačítek.
2. **Zrušení zaplacené:** kartou → Zaplatit → „Potvrzená“ → Zrušit → zboží zpět, dva e-maily: „zrušeno“
   a „platbu vracíme“.
3. **Zrušení hned po odeslání pokladny:** dobírka → na detailu ještě „Ověřujeme sklad…“ rychle Zrušit → „Zrušená“,
   zboží se po chvíli vrátí (v logu `ReleaseStockV1`).
4. Odeslanou objednávku zrušit nejde (tlačítko chybí, API vrátí chybu).

### Produkce

Nic nového k nastavení (žádný secret, migrace ani `deploy.sh`). Sloučit PR → CD nasadí → ověřit body 1–3.

## Vyzkoušejte sami

- Najdi v `OrderSagaOrchestrator.StepAsync`, co by se stalo bez „dohnání“ zrušení, kdyby `PaymentSucceededV1`
  dorazila dřív než `OrderCancelled`. (Nápověda: `Order.Confirm` u zrušené objednávky.)
- Proč stav `Cancelled` nemá vlastní termín? Co by se stalo, kdyby sklad neodpověděl nikdy?
- Test `Zruseni_PlatbaPredbehlaZruseni_SagaZruseniDozeneAPenizeVrati` posílá `PaymentSucceededV1` dvakrát
  (ručně a z outboxu Payments). Proč odejde jen jeden `RefundPaymentV1`?
- Navrhni kontrakt `CancelPaymentV1` – kdy by se vyplatil a co by se změnilo v Payments?
