# E-shop krok 8: Platba (simulovaná platební brána)

Objednávka kartou se po rezervaci zboží nepotvrdí hned – čeká, až zákazník zaplatí. Platbu řeší nový modul
**Payments** jako druhý účastník ságy. Výsledek z brány přijde **podepsaným webhookem**. Nezaplacená objednávka
po 30 minutách vyprší (zamítne se a zboží se vrátí na sklad), pozdní platba se vrátí. Dobírka platbu přeskočí.
Brána je zatím **simulace** (stránka `/Pay/{objednávka}`), skutečná by byla jen jiná implementace `IPaymentGateway`.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 7](krok-07-notifikace.md).

## Jak to funguje

Přirovnání: zboží odložíme pod pult se jménem zákazníka, ale jen na 30 minut. Platí se u banky (brána), která
nám pošle dopis „zaplaceno“ s pečetí, kterou umíme ověřit jen my dva (HMAC podpis). Dopis bez pečeti zahodíme.
Kdo zaplatí až po 30 minutách, zboží už je zpátky v regálu – peníze mu vrátíme.

```
 karta:    Pending ─rezervace─► AwaitingPayment ─PaymentSucceededV1─► Confirmed ─► Packing ─► …
                                   └─ PaymentFailedV1 / 30 min ─► Rejected + ReleaseStockV1
           Rejected/Released ─PaymentSucceededV1 (pozdě)─► RefundPaymentV1 ─► PaymentRefundedV1 (e-mail)
 dobírka:  Pending ─rezervace─► Confirmed

 sága ──RequestPaymentV1──► Payments ── stránka brány /Pay ── klik ──► webhook (JSON + X-MiniShop-Signature)
                                        └─ PaymentWebhookProcessor: podpis? → Payment.Succeed/Fail → outbox ──► sága
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Účastník ságy** | služba, kterou sága řídí příkazy a čeká na její odpověď | Payments (`RequestPaymentV1` → `PaymentSucceededV1`/`FailedV1`) |
| **Webhook** | vnější systém zavolá náš HTTP endpoint, když má výsledek | `POST /api/payments/webhook` |
| **HMAC podpis** | otisk zprávy spočítaný se sdíleným tajemstvím – bez tajemství ho nikdo nespočítá | `WebhookSignature`, hlavička `X-MiniShop-Signature`, 401 bez něj |
| **Idempotence vnějšího vstupu** | stejný webhook dvakrát = jeden výsledek | `Payment.Succeed` podruhé false |
| **Timeout s kompenzací** | čekání má termín; po něm se vrátí to, co už proběhlo | `PaymentTimedOut` → `RejectOrderAndReleaseStock` |
| **Kompenzace pozdní odpovědi** | výsledek přišel po termínu – vrátit ho | `PaymentSucceeded` v `Rejected`/`Released` → `RefundPaymentV1` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Payments/*` | `RequestPaymentV1`, `RefundPaymentV1` (příkazy), `PaymentSucceededV1`, `PaymentFailedV1`, `PaymentRefundedV1` |
| `src/MiniShop.Domain/Orders/PaymentMethod.cs`, `Events/OrderAwaitingPayment.cs` | způsob platby, nová doménová událost |
| `src/MiniShop.Domain/Payments/*` | `Payment` (stavový automat) + doménové události |
| `src/MiniShop.Application/Payments/*` | `PaymentCommandHandlers`, `PaymentWebhookProcessor`, `WebhookSignature`, `IPaymentGateway`, `PaymentsSettings`, `PaymentQueryService` |
| `src/MiniShop.Infrastructure/Payments/*` | `PaymentsDbContext` (outbox + inbox), repozitář, `FakePaymentGateway` + simulátor, migrace `InitialPayments` |
| `src/MiniShop.Web/Pages/Pay/Index.cshtml(.cs)`, `Controllers/PaymentsWebhookController.cs` | stránka simulované brány, webhook |
| `docs/prirucka/architektura/moduly/payments.md`, `docs/prirucka/toky/platba.md`, tento souhrn | dokumentace |
| `tests/…/Domain/PaymentTests.cs`, `Web/PaymentPageTests.cs` | platba, podpis, webhook přes HTTP, stránka brány |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs`, `OrderStatus.cs` | `PaymentMethod`, `SetPayment`, `AwaitPayment`, `AwaitingPayment = 7`; `Confirm` u karty až z `AwaitingPayment`, `Reject` i z `AwaitingPayment` |
| `Domain/Orders/Saga/OrderSaga.cs` | `RequiresPayment`, `PaymentDeadlineUtc`, `RefundRequested`; stav `AwaitingPayment`; `PaymentSucceeded`, `PaymentFailed`, `PaymentTimedOut`, `TimedOut`; akce `RequestPayment`, `RejectOrderAndReleaseStock`, `RefundPayment` |
| `Application/Ordering/Saga/OrderSagaOrchestrator.cs` | provedení nových akcí, handlery `PaymentSucceededV1`/`FailedV1`, `OrderSagaSettings.PaymentTimeout` |
| `Application/Ordering/Checkout/*`, `Web/Pages/Checkout/Index` | volba platby (kartou / dobírka) |
| `Infrastructure/Ordering/*` | konfigurace, timeout worker hledá i prošlé platby, migrace `Payments` (`Orders.PaymentMethod` = `None` pro staré) |
| `Application/Notifications/*` | e-mail „platbu vracíme“ (`PaymentRefundedV1`) |
| `Web/Pages/Orders/Detail`, `Orders/Replay`, `Web/Orders/OrderStatusLabels.cs` | tlačítko Zaplatit, stav „Čeká na platbu“, platba v kopii pokladny |
| `Web/Program.cs`, `Extensions/…`, `appsettings*.json`, `Dockerfile`, `AppHost.cs` | `payments.db`, `Payments:WebhookSecret` (Aspire parametr), `Sagas:PaymentTimeoutMinutes` |
| `deploy/compose.yaml`, `.env.example`, `deploy.sh`, `.github/workflows/deploy.yml`, `backup.py` | secret `PAYMENTS_WEBHOOK_SECRET`, `payments.db` v zálohách |
| `docs/secrets.md` (R10), příručka | nový secret, moduly, toky, stavové diagramy |

## Rozhodnutí v tomto kroku

- **Simulovaná brána, ne skutečná** – webhook skutečné brány musí dojít z internetu (lokálně tunel), další účet
  a klíče, nespolehlivé testy. Simulace umí všechny scénáře na povel; skutečná = jiná `IPaymentGateway`.
- **Simulátor volá zpracování webhooku přímo** (stejný `PaymentWebhookProcessor` a stejný podpis jako HTTP
  endpoint), ne po síti na sebe sama. HTTP endpoint testuje `PaymentPageTests`.
- **Nezaplaceno = `Rejected`**, ne `Cancelled` – `Cancelled` je „zrušil člověk“, `Rejected` „systém nemohl vyřídit“.
- **Zamítnutí a vrácení zboží jednou akcí** (`RejectOrderAndReleaseStock`) – v jedné transakci s outboxem.
- **Termín platby drží sága** (`PaymentDeadlineUtc`), Payments ho dostane v příkazu jen proto, aby simulace po
  termínu nepustila zaplatit. O vypršení rozhoduje jediné místo – sága.
- **Pozdní platba se vrací jen jednou** (`RefundRequested`), i kdyby přišly dvě různé zprávy.
- **Tajemství webhooku je povinné** (fail fast, min. 32 znaků) – stejně jako servisní klíč skladu.
- **E-mail s odkazem na platbu zatím ne** – zákazník po pokladně skončí na detailu s tlačítkem Zaplatit.

## Známá omezení

- Zrušit objednávku čekající na platbu zatím nejde (e-shop krok 9) – jen nechat vypršet nebo zamítnout na bráně.
- Simulace nevrací peníze „doopravdy“ – `Refund` jen změní stav a pošle `PaymentRefundedV1`.
- Detail objednávky ukazuje délku termínu, ne přesný čas vypršení (ten zná sága).
- Skutečná brána by potřebovala ověřit i částku a měnu z webhooku proti platbě – simulace posílá jen ID a výsledek.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu migrace `…_Payments` a `…_InitialPayments`; v RabbitMQ fronta `minishop.payments`.
2. Pokladna → **Kartou** → detail „Čeká na platbu“ s tlačítkem **Zaplatit** → stránka brány → **Zaplatit** →
   za pár sekund „Potvrzená“, e-mail „potvrzená“.
3. Jiná objednávka → na bráně **Zamítnout** → „Zamítnutá“ s důvodem, zboží zpět na skladu.
4. Nastav `Sagas:PaymentTimeoutMinutes` na 1, objednej kartou a nech být → po minutě „Zamítnutá“ (platba nedokončena včas).
5. Dobírka → potvrzená hned po rezervaci, bez platby.
6. `curl -X POST https://localhost:7180/api/payments/webhook -d '{}'` → 401.

### Produkce

1. **Před sloučením:**
   - GitHub → *Environments* → `production` → nový secret `PAYMENTS_WEBHOOK_SECRET` (64 hex znaků, `openssl rand -hex 32`).
   - Znovu nainstalovat `deploy/deploy.sh` na VM (nové nasazení na něm závisí – starý skript by neznámý klíč odmítl).
   - Lokální `deploy/.env` (Compose na PC) doplnit o `PAYMENTS_WEBHOOK_SECRET`.
   - Ruční záloha `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí (nová `payments.db`, migrace `ordering.db`).
3. Objednat kartou → Zaplatit → potvrzená.

## Vyzkoušejte sami

- Pošli na webhook ručně tělo s cizím `paymentId` a podpisem spočítaným jiným tajemstvím. Co vrátí? A se správným?
- Najdi v `OrderSaga`, proč pozdní platba nevrátí peníze dvakrát, i když přijdou dva webhooky.
- Co by se stalo, kdyby `PaymentWebhookProcessor` nejdřív četl JSON a podpis kontroloval až potom?
- Navrhni `StripePaymentGateway` – co by dělala v `CreateAsync` a co by se změnilo jinde?
