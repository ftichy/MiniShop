# E-shop krok 22A: Vratka po částech – sklad a Payments

Cíl (issue #119): zákazník vrátí jen vybrané kusy a k jedné objednávce může udělat víc vratek; admin převezme zboží,
sklad ho naskladní, peníze a dobropis jen za vrácené. Krok je rozdělený na tři PR a zavádí se **od konce**:

| Část | Co | Kdo posílá nové zprávy |
| --- | --- | --- |
| **22A (tento)** | sklad a Payments umí vratku po částech | nikdo (jen testy) |
| 22B | dobropisy, e-maily, tržby, hodnocení | nikdo |
| 22C | Ordering – výběr položek, sága pro každou vratku, obrazovky | Ordering – funkce se zapne |

Proč od konce: kdyby první přišel Ordering, částečná vratka by poslala dnešní `RefundPaymentV1` (vrací **celou** platbu)
a Invoicing by vystavil **celý** dobropis. Nová zpráva se zavádí jako nový tiskopis na poště – nejdřív se ho naučí
přepážky, pak se rozdá zákazníkům. Každý PR jde nasadit samostatně, bez vypínače a bez nebezpečného mezistavu.

## Jak to funguje

1. **Sklad:** `RestockReturnedGoodsV2(OrderId, ReturnNumber, Lines)` → `RestockReturnedGoodsHandler` vezme kusy z vydaných
   rezervací produktu (`StockReservation.Return`, nový sloupec `ReturnedQuantity`; poslední kus nastaví `ReturnedAtUtc`),
   naskladní je a zapíše `StockReturn` (unikátní objednávka + číslo vratky) → stejná vratka podruhé nic nenaskladní, jen
   znovu odpoví `ReturnedGoodsRestockedV2` se skutečným počtem kusů. Vratka celé objednávky (V1) po částečných vrátí jen zbytek.
2. **Payments:** `RefundPaymentV2(OrderId, ReturnNumber, Amount)` → `Payment.RefundPart` (pravidla: jen zaplacená, částka
   0,01 až zbývající, každá vratka jednou – `PaymentRefund`, unikátní platba + číslo) → brána (`RefundPartAsync`, Stripe
   `amount` v haléřích, idempotency key `minishop-refund-{platba}-return-{číslo}`) → uložení → `PaymentRefundedV2`
   (částka vratky + vráceno celkem). Když vratky vrátí všechno, platba je `Refunded`. `Refund` (V1) po částečných vrátí jen
   zbytek a `PaymentRefundedV1` nese skutečně vrácenou částku.
3. **Souběh:** dvě vratky téže platby najednou – `Payment.Version` (concurrency token) pustí jen jednu, druhá se zopakuje nad
   aktuálním součtem. Stejná vratka dvakrát – unikátní indexy (`StockReturns`, `PaymentRefunds`).

## Nové soubory

- Kontrakty `RestockReturnedGoodsV2`, `ReturnedGoodsRestockedV2` (Inventory), `RefundPaymentV2`, `PaymentRefundedV2` (Payments)
- `src/MiniShop.Domain/Inventory/StockReturn.cs`, `src/MiniShop.Domain/Payments/PaymentRefund.cs`
- `Infrastructure/Inventory/Configurations/StockReturnConfiguration.cs`, `Infrastructure/Payments/Configurations/PaymentRefundConfiguration.cs`
- Migrace `PartialReturns` (Inventory – sloupec `ReturnedQuantity` + data pro starší vratky + tabulka `StockReturns`;
  Payments – sloupec `Version` + tabulka `PaymentRefunds`)
- `tests/MiniShop.Tests/Domain/StockReservationTests.cs`

## Změněné soubory

`StockReservation` (`ReturnedQuantity`, `ReturnableQuantity`, `Return`, `MarkReturned` vrací počet), `IStockReservationRepository`
(`GetReturnAsync`, `AddReturn`), `RestockReturnedGoodsHandler` (V1 zbytek, V2), `Payment` (`Refunds`, `RefundedAmount`,
`RefundableAmount`, `Version`, `RefundPart`), `PaymentEvents` (`PaymentRefunded.Amount`, `PaymentPartlyRefunded`),
`IPaymentGateway` + Fake + Stripe (`RefundPartAsync`), `PaymentCommandHandlers` (V2), `PaymentsDbContext`, `InventoryDbContext`,
registrace (Inventory.Api `Program.cs`, `ApplicationServiceCollectionExtensions`, testy), příručka (moduly Inventory a Payments,
tok Vrácení zboží, observabilita), CLAUDE.md.

## Rozhodnutí

- **Nové verze, staré beze změny:** V1 dál používá sága objednávky (pozdní platba, zrušení) i vratka celé objednávky.
- **Číslo vratky v objednávce** (`ReturnNumber` 1, 2, …), ne globální ID – sklad a Payments objednávky neznají, dvojice
  objednávka + číslo je pro ně jednoznačná. Celé vrácení peněz má v `PaymentRefunds` číslo 0.
- **Payments kontroluje součet sám** (pojistka) – hlavní pravidlo „nevrátit víc, než se koupilo“ bude v `Order` (22C).
- **Sklad je shovívavý:** vratka chce víc kusů, než bylo vydáno → naskladní, co jde, a skutečný počet pošle v odpovědi
  (+ atribut trasy `minishop.stock.missing`). Výjimka by zablokovala vrácení peněz zákazníkovi kvůli evidenci skladu.
- **Pořadí v Payments: pravidla → brána → uložení** – brána nikdy nedostane částku nad zbývající; pád po bráně zopakuje volání
  se stejným klíčem idempotence.
- **Migrace jen přidávají** (expand): sloupce s výchozí hodnotou 0, nové tabulky; starší verze kódu s nimi běží.
  Platby vrácené před krokem 22 nemají řádky vrácení – `RefundedAmount` u nich = celá částka.

## Známá omezení

- Odpovědi V2 zatím nikdo neposlouchá (sága vratky je začne číst v 22C) – zprávy v RabbitMQ bez fronty zaniknou, nevadí to.
- Souběh, kdy brána už vrátila a uložení prohraje se souběžnou vratkou, která mezitím vyčerpala částku, by peníze vrátil
  dvakrát – nemůže nastat, dokud `Order` (22C) nepustí vratky nad koupené.

## Jak ověřit

- `dotnet test` – `StockReservationTests`, `PaymentTests` (vrácení po částech), `OrderServiceTests.VratkaPoCastech_*`
  (sklad naskladní jen uvedené kusy a každou vratku jednou; platba vrátí jen část, jen jednou, celé vrácení pak zbytek),
  `StripeGatewayTests.RefundPart_*` (`amount` v haléřích, klíč vratky), `IntegrationContractTests` (tvar JSON V2).
- Produkce: chování se nemění (V2 nikdo neposílá) – stačí, že vratka celé objednávky funguje jako dřív.

## Vyzkoušejte sami

1. V `PaymentTests` změňte částku druhé vratky na 150,01 Kč – `RefundPart` odmítne vrátit víc, než zbývá.
2. V `OrderServiceTests.VratkaPoCastech_SkladNaskladniJenUvedeneKusy_KazdouVratkuJednou` pošlete první vratku třikrát –
   sklad naskladní jednou, odpoví třikrát.
