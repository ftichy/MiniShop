# E-shop krok 16: Skutečná platební brána (Stripe, testovací režim)

Simulovanou bránu z kroku 8 doplnila **skutečná** – Stripe Checkout v testovacím režimu. Je to jen další implementace
rozhraní `IPaymentGateway` (adaptér); sága, Ordering, kontrakty ani stav platby se nezměnily. Přepíná se konfigurací
`Payments:Gateway` (`Fake` / `Stripe`). Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md) („kam dál“ – skutečná brána).

## Jak to funguje

Přirovnání: dosud jsme platili u kamaráda, který hrál banku. Teď chodíme do skutečné banky – jen s hracími penězi.
Banka má vlastní formuláře, vlastní razítka (podpis webhooku) a někdy nám stejný dopis pošle dvakrát.

1. `RequestPaymentV1` → `StripePaymentGateway.CreateAsync` založí **Checkout Session** (částka v haléřích, CZK,
   `metadata.order_id`, `expires_at`, návrat na detail objednávky) s **idempotency key** = ID příkazu.
2. Zákazník na detailu klikne *Zaplatit* → `/Pay/{id}` ho přesměruje (GET 302) na `checkout.stripe.com`.
   Kartu zadává u Stripu – MiniShop ji nevidí.
3. Stripe pošle `checkout.session.completed` na `/api/payments/stripe-webhook`. `StripeWebhookProcessor` ověří podpis
   (`Stripe-Signature`, zpráva max. 5 min stará), najde platbu podle `order_id` **a** ID session a výsledek předá
   společnému jádru `PaymentWebhookProcessor.ApplyResultAsync` → `PaymentSucceededV1` → sága potvrdí objednávku.
4. `checkout.session.expired` = nezaplaceno. Jiné události účtu = 200 bez efektu.
5. `RefundPaymentV1` (pozdní platba, zrušení, vratka) → `IPaymentGateway.RefundAsync` → refund u Stripu
   (PaymentIntent ze session, idempotency key podle ID platby) → teprve pak náš stav `Refunded`.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Payments/Stripe/StripePaymentGateway.cs` | Checkout Session a refund přes Stripe.net, idempotency key, haléře |
| `src/MiniShop.Infrastructure/Payments/Stripe/StripeWebhookProcessor.cs` | podpis, typy událostí, platba podle `order_id` + ID session |
| `src/MiniShop.Infrastructure/Payments/Stripe/StripeSettings.cs` | klíče z konfigurace, jen `sk_test_`, volba brány `FromConfiguration` |
| `src/MiniShop.Web/Controllers/StripeWebhookController.cs` | `POST /api/payments/stripe-webhook` (bez Stripu 404) |
| `src/MiniShop.Infrastructure/Payments/Migrations/…_StripeCheckoutUrl.cs` | `CheckoutUrl` 500 → 2048 znaků (adresa Stripu je delší) |
| `tests/MiniShop.Tests/Infrastructure/StripeGatewayTests.cs` | brána přes podvržený HttpClient, podepsané webhooky, nastavení |

## Změněné soubory

- `IPaymentGateway` – `CreateAsync(payment, requestId)`, nové `RefundAsync`; `PaymentGatewayInfo` (která brána běží, testovací karta).
- `PaymentCommandHandlers` – ID příkazu jako idempotency key; refund nejdřív u brány, pak stav.
- `PaymentWebhookProcessor` – společné jádro `ApplyResultAsync` (simulace i Stripe).
- `PaymentQueryService` – `PaymentDto.CheckoutUrl`; `FakePaymentGateway` – `RefundAsync` nic nedělá.
- `PaymentsInfrastructure.AddPaymentsInfrastructure(…, stripe)` – registrace podle brány; `Program.cs` – `StripeSettings.FromConfiguration`.
- `/Pay` – se Stripem přesměruje na bránu, simulovaná tlačítka i `/api/payments/webhook` vrací 404; detail objednávky
  ukáže testovací kartu; `/Privacy` – Stripe jako zpracovatel.
- `MiniShop.Infrastructure.csproj` – balíček **Stripe.net**.
- Nasazení: `compose.yaml` (`Payments__Gateway`, `Payments__Stripe__*`), `production.env` (`PAYMENTS_GATEWAY`),
  `.env.example`, `deploy.yml` + `deploy.sh` (secrets `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, kontrola tvaru),
  `docs/secrets.md` (R13).
- Příručka: modul Payments (sekce Stripe), tok Platba, knihovny, online služby, secrets, slovníček (idempotency key,
  adaptér), historie. Testy: matice oprávnění.

## Rozhodnutí v tomto kroku

- **Stripe, ne GoPay** – testovací režim hned po registraci (GoPay sandbox jen na žádost), oficiální .NET knihovna,
  Stripe CLI pro webhooky na localhost.
- **Stripe Checkout (stránka Stripu), ne vlastní formulář karty** – MiniShop kartu nevidí (nejmenší rozsah PCI DSS)
  a CSP se nemění (přesměrování je GET, ne odeslání formuláře).
- **Idempotency key z ID příkazu**, ne z čísla objednávky – testovací účet sdílí vývoj i produkce a čísla objednávek
  se mezi prostředími opakují. Parametry požadavku proto bez ničeho proměnlivého.
- **Platba podle `order_id` + kontrola ID session** – Stripe posílá události celého účtu všem endpointům; cizí session
  = 200 bez efektu.
- **Session vyprší 30 min po našem termínu** (Stripe chce min. 30 min od založení a parametry musí být při opakování
  stejné) – pozdní platbu vrátí existující kompenzace.
- **Bez migrace stavu** – PaymentIntent se zjistí ze session až při refundu.
- **Simulace zůstává** (lokálně a v testech výchozí); se Stripem je vypnutá, aby nešlo „zaplatit“ bez peněz.
- **Jen testovací klíč** – `sk_live_` odmítne Web i `deploy.sh`.
- **Zapnutí v produkci zvlášť** (`PAYMENTS_GATEWAY=Stripe` až po secretech) – Web bez klíčů se nespustí a postupné
  nasazení by skončilo rollbackem.

## Známá omezení

- Jen testovací režim – skutečné peníze přijímat nejde.
- Kdo zaplatí na otevřené stránce Stripu po timeoutu ságy (do 30 min), dostane peníze zpět a objednávka zůstane zamítnutá.
- Bez webhooku (špatně nastavený endpoint) platba „visí“ a po 30 minutách ji zamítne timeout ságy.
- Platby čekající v simulaci při přepnutí na Stripe dojedou timeoutem.
- Částečný refund ani jiné metody než karta (Stripe je podle nastavení účtu může nabídnout) se neřeší zvlášť.

## Jak ověřit

- `dotnet test MiniShop.slnx` – `StripeGatewayTests`, `OrderServiceTests` (Platba_*), `PaymentPageTests`, `AuthorizationMatrixTests`.
- Lokálně se Stripem: user secrets Webu `Payments:Gateway=Stripe`, `Payments:Stripe:SecretKey`, `Payments:Stripe:WebhookSecret`
  (ze Stripe CLI) a `stripe listen --events checkout.session.completed,checkout.session.expired,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed --forward-to https://localhost:7180/api/payments/stripe-webhook`.
- V produkci: objednávka kartou → *Zaplatit* → Stripe → `4242 4242 4242 4242` → objednávka potvrzená, faktura;
  zrušení → refund ve Stripe Dashboardu (*Payments* → stav *Refunded*) a dobropis.

## Vyzkoušejte sami

1. Zaplaťte kartou `4242 4242 4242 4242` – za pár sekund je objednávka potvrzená, ve Stripe Dashboardu je platba
   a u webhooku odpověď 200.
2. Karta `4000 0000 0000 0002` (zamítnutá) – Stripe nechá zkusit jinou kartu; když stránku opustíte, objednávku po 30 minutách zamítne timeout.
3. Zaplacenou objednávku zrušte – ve Stripu přibude refund, zákazník dostane e-mail o vrácení a dobropis.
4. Ve Stripe Dashboardu u webhooku *Resend* stejné události – odpověď 200, nic se nezdvojí.
