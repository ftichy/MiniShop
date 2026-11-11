# MiniShop – instrukce pro Claude

## Komunikace
- S uživatelem mluv **česky** (vysvětlení, souhrny, komentáře v kódu, commit zprávy).
- Uživatel je vývojář, ale v event-driven architektuře (EDA) je **začátečník**. Vysvětluj jednoduše,
  na konkrétních místech v kódu, s přirovnáním ze života, když je to užitečné.
- Identifikátory v kódu (třídy, metody, proměnné) piš **anglicky**, komentáře a texty pro uživatele česky.

## Projekt
Výukový e-shop: ASP.NET Core (.NET 10), EF Core + PostgreSQL (schéma na modul), REST API + Razor Pages, xUnit.
Cíl: postupně ho převést na EDA podle plánu v `docs/EDA-roadmap.md`.

```
src/MiniShop.Contracts       integrační zprávy – události (OrderPlacedV1, StockReservedV1, …) a příkazy (ReserveStockV1, …), žádné závislosti
src/MiniShop.Domain          Common/ (Entity, IDomainEvent), Orders/ (Order, CatalogProduct, CatalogSearchText, Carts/Cart + CartEvents, Coupons/Coupon, Saga/OrderSaga + ReturnSaga), Inventory/ (Product, Category, StockAvailability, StockReservation),
                             Journeys/ (JourneyEvent, JourneyEventType), Notifications/ (OrderRecipient), Payments/ (Payment), Reporting/ (SalesOrder – čtecí model), Reviews/ (Review, PurchasedOrder), Invoicing/ (BillableOrder, Invoice, InvoiceNumberSeries, CreditNote, CreditNoteNumberSeries)
src/MiniShop.Application     Abstractions/ (sdílené: IUnitOfWork, IDomainEvent*, IIntegrationMessage*, TraceTags),
                             Ordering/ (OrderService, DTO, Saga, Catalog projekce + CatalogQueryService, Carts/CartService, Checkout/CheckoutService, IntegrationEvents (překladač), Pricing), Inventory/ (ProductService, CategoryService, Reserve/ReleaseStock),
                             Journeys/ (JourneyService, OrderJourneyProjection, JourneyReplayProjection, DTO),
                             Notifications/ (OrderNotificationsHandler, OrderEmailTemplates, NotificationSettings, CartReminderHandler, ICustomerContacts),
                             Payments/ (PaymentCommandHandlers, PaymentWebhookProcessor, WebhookSignature, IPaymentGateway),
                             Reporting/ (SalesProjectionHandler, SalesReportService),
                             Reviews/ (ReviewService, PurchaseProjectionHandler), Invoicing/ (InvoicingHandler, InvoiceService, IInvoicePdfRenderer)
src/MiniShop.Infrastructure  sdílené: Persistence/ModuleDbContext, Outbox, Inbox (InboxGuard), Integration (bus, routing, dispatcher, RabbitMq/), Events,
                             Observability/ (MessagingTelemetry – ActivitySource + Meter, OutboxHealthCheck),
                             Hosting/ (OutboxWorker, RabbitMqConsumersWorker, AddMessaging);
                             Ordering/ (OrderingDbContext, repozitáře, notifier, DB init), Inventory/ (InventoryDbContext, repozitáře, DB init + seed),
                             Journeys/ (JourneysDbContext – schéma journeys bez outboxu/inboxu, repozitář, DB init),
                             Notifications/ (NotificationsDbContext – schéma notifications s inboxem, repozitář příjemců, DB init),
                             Payments/ (PaymentsDbContext – schéma payments s outboxem + inboxem, FakePaymentGateway + simulátor),
                             Reporting/ (ReportingDbContext – schéma reporting s inboxem + outboxem, jen příkaz na přestavění),
                             Reviews/ (ReviewsDbContext – schéma reviews s inboxem + outboxem, hodnocení + kopie nákupů),
                             Invoicing/ (InvoicingDbContext – schéma invoicing s inboxem + outboxem, QuestPdfInvoiceRenderer)
src/MiniShop.Web             služba Ordering (7180): API objednávek, Razor Pages (celé UI), Inventory/InventoryApiClient (HTTP na sklad),
                             OrderSagaTimeoutWorker, Program.cs (composition root); krok 12: JourneysController, Pages/Orders/Journey + Replay,
                             wwwroot/js/order-form-tracker.js + journey-player.js
src/MiniShop.Inventory.Api   služba skladu (7181): ProductsController, CategoriesController, jen modul Inventory, schéma inventory, fronta minishop.inventory
src/MiniShop.ServiceDefaults AddServiceDefaults (service discovery, HTTP resilience, health checky /health, /alive, OpenTelemetry → OTLP)
src/MiniShop.AppHost         .NET Aspire: RabbitMQ (persistentní kontejner) + inventory-api + web, dashboard
tests/MiniShop.Tests         testy domény, OrderService + ságy + katalogu (PostgreSQL přes Testcontainers, oba moduly v procesu), dispatcheru,
                             outboxu, inboxu, kontraktů, architektury, InventoryApiClient
docs/                        EDA-roadmap.md, kroky/ (souhrny hotových kroků), diagramy/
```

## Příkazy
- Build: `dotnet build MiniShop.slnx`
- Testy: `dotnet test MiniShop.slnx` (potřebuje Docker – Testcontainers spustí PostgreSQL)
- Spuštění (krok 10B): `dotnet run --project src/MiniShop.AppHost` – Aspire spustí RabbitMQ, PostgreSQL, Inventory.Api (7181) a Web (7180),
  dashboard https://localhost:17180 (přihlašovací odkaz v konzoli). Potřebuje Docker.
- Bez Aspire: `docker start minishop-rabbit` (poprvé
  `docker run -d --name minishop-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management`; UI http://localhost:15672, guest/guest),
  `docker start minishop-postgres` (poprvé `docker run -d --name minishop-postgres -e POSTGRES_DB=minishop -e POSTGRES_USER=minishop
  -e POSTGRES_PASSWORD=minishop-vyvoj -p 5432:5432 postgres:18-alpine`), pak `dotnet run --project src/MiniShop.Inventory.Api` a `dotnet run --project src/MiniShop.Web`.
  Transport `InProcess` od 10B pro spuštění nejde (dva procesy) – jen v testech.
- Při ověřování naživo pozor: zastavení AppHostu nechá běžet kontejnery `messaging-…` a `postgres-…` (persistentní); procesy služeb ukončit.
- Po každé změně kódu spusť build a testy a oprav chyby, než ohlásíš hotovo.

## Konvence a záměrná rozhodnutí
- Závislosti vedou dovnitř: Web → Application → Domain, Infrastructure → Application, Application → Contracts.
- `MiniShop.Contracts` = zprávy mezi moduly/službami (DTO pro REST API jsou v modulech, např. `Application/Ordering/OrderContracts.cs`).
  Kontrakty se nepřejmenovávají ani nemění – větší změna = nová verze (`…V2`). Hlídají to `IntegrationContractTests`.
- **Modulární monolit (krok 10):** moduly Ordering (`…Domain.Orders`, `…Application.Ordering`, `…Infrastructure.Ordering`)
  a Inventory (`…Inventory` ve všech vrstvách) se navzájem **nereferencují** – hlídá `Architecture/ModuleBoundariesTests`.
  Sdílené jádro (`Domain.Common`, `Application.Abstractions`, `Infrastructure.Outbox/Inbox/Integration/Persistence/Events`) nezná žádný modul.
  Každý modul má vlastní DB (schéma `ordering`, `inventory` … v jedné PostgreSQL databázi), vlastní outbox + inbox a vlastní `I…UnitOfWork` / `I…MessagePublisher`
  (`IOrderingUnitOfWork`, `IInventoryMessagePublisher`, …) – handler modulu nikdy nebere obecné `IUnitOfWork`.
- **Samostatné služby (krok 10B):** Web hostuje jen Ordering (`AddInfrastructureCore` + `AddOrderingInfrastructure`),
  Inventory.Api jen Inventory. `AddInfrastructure(ordering, inventory)` (oba moduly) používají jen testy.
  Web volá sklad přes HTTP jen kvůli UI (`InventoryApiClient` : `IProductService`, `https+http://inventory-api`,
  bez Aspire adresa v `Services:inventory-api` v `appsettings.Development.json` Webu – jinde ji dává Aspire/Compose,
  `https` adresa má přednost a v kontejneru by přebila `http`); business tok jde jen přes zprávy.
  Výloha, košík i pokladna berou nabídku a dostupnost z kopie katalogu (`ICatalogQueryService`, `ICatalogProductRepository`).
  Web nesmí referencovat `Domain/Infrastructure.Inventory`, Inventory.Api nic z Orderingu (testy architektury).
  Auditní frontu poslouchá jen Web (`AuditQueue` prázdná v Inventory.Api). RabbitMQ adresu dává Aspire v `ConnectionStrings:messaging`.
- Ordering zná produkty jen z kopie katalogu `CatalogProduct` (plní `CatalogProjectionHandler` z `ProductUpdatedV1`/`V2`,
  `StockAvailabilityChangedV1`, `ProductRemovedV1`). Obnova kopie: příkaz `SyncCatalogV1` (`CatalogSyncService`) – automaticky
  po startu Webu, když je kopie prázdná nebo neúplná (produkt s dostupností `Unknown`; `CatalogSyncOnStartupWorker`), nebo
  tlačítkem na stránce Objednávky; sklad (`SyncCatalogHandler`) pošle `ProductUpdatedV1` + `V2` pro každý produkt s novým
  `EventId`, ale s časem `Product.ChangedAtUtc` (ne „teď“ – nesmí přebít novější změnu).
  Inventory převádí doménové události produktu na integrační rovnou v `InventoryDbContext.ToOutboxMessages` (Ordering přes handler);
  jedna událost může dát víc zpráv – druhá má ID odvozené `MessageIds.Derive` (inbox by stejné ID zahodil jako duplicitu).
- **Výloha (e-shop krok 2A):** veřejná `/Shop` čte jen kopii katalogu přes `ICatalogQueryService` (filtr v SQL, řazení a
  stránkování v paměti – z doby SQLite, které neřadilo decimal; hledání přes `CatalogProduct.SearchText` = `CatalogSearchText.Normalize`).
  `ProductUpdatedV2` = celý stav produktu (popis, kategorie, `ImageFileName`, dostupnost jako snímek); sklad posílá zatím
  V1 i V2 (expand → contract), změna produktu/kategorie produktu = obě. `StockAvailabilityChangedV1` jen při změně pásma
  (`StockAvailabilityBands`, víc než 5 / 1–5 / 0) – `Product.AddStock/RemoveStock(quantity, čas)`. Dostupnost má v obou
  modulech vlastní čas (`AvailabilityChangedAtUtc`). Kategorie (`Category`, `CategoryService`, `/api/categories`) patří skladu,
  Ordering zná jen jméno ze zprávy o produktu (přejmenování → `product.CategoryRenamed` → V2 pro každý produkt);
  názvy kategorií porovnávat/řadit česky v paměti. Košík a pokladna berou ceny a dostupnost z kopie katalogu,
  na sklad se Web ptá jen v administraci (`InventoryApiClient` = `IProductService` + `ICategoryService` + `IProductImageService`).
- **Obrázky produktů (e-shop krok 2B):** soubor patří skladu (`IProductImageStore` → `FileSystemProductImageStore`, `ProductImages:Path`,
  kontejner `/data/images`, lokálně `src/MiniShop.Inventory.Api/images/`), do zpráv jen jméno (`ProductUpdatedV2.ImageFileName`).
  Pravidla v `ProductImageFormat` (typ podle bajtů JPEG/PNG/WebP, žádné SVG, 1 MB, jméno `ID-otisk.přípona`, čte se jen jméno
  podle vzoru). Veřejně jen přes Web `/media/products/{soubor}` (`ProductImageEndpoint`, `Cache-Control: immutable`).
  Pořadí: soubor → DB → smazat starý soubor. Testy: `InMemoryProductImageStore`, ve Webu `StubProductImageService`.
- **Košík (e-shop krok 3):** agregát `Cart` + `CartLine` v Orderingu (schéma `ordering`, unikátní `CustomerId` a `CartId+ProductId`),
  invarianty jen v metodách `Cart` (20 řádků, 1–100 ks, ne vyprodaný/stažený). Ukládá jen ID produktu, množství
  a `PriceWhenAdded` (upozornění na změnu); název/cenu/dostupnost bere `CartService` vždy z kopie katalogu.
  Zboží neblokuje, žádné doménové události (zprávu `CartAbandonedV1` posílá až připomínka, e-shop krok 10). Jen `RequireCustomer`; přidání z výlohy =
  POST `/Cart?handler=Add` s `returnUrl` (jen lokální). Odznak v hlavičce `CartBadgeViewComponent`.
  Smazání účtu (`AnonymizeCustomerAsync`) smaže i košík.
  **Event sourcing (e-shop krok 17):** pravda = `CartEvents` (`CartEventRecord`, unikátní `CartId+Version` = souběh, `ON DELETE CASCADE`
  = smazání účtu smaže proud); `Carts`/`CartLines` = projekce ve stejné transakci (odznak, opuštěné košíky). **Stav měnit jen
  `Raise(událost)` → `Apply`** (pravidla před `Raise`; nová událost = record v `CartEvents.cs` + řádek v `CartEventSerializer.Types`
  + větev v `Apply`; jména **nepřejmenovávat**). Uložení v `OrderingDbContext.SaveChangesAsync`, načtení `CartRepository` →
  `Cart.Rehydrate` (události od poslední „nuly“ `CartCheckedOut`/`CartCleared`/`CartSnapshotTaken`; bez událostí nebo jiný
  `CheckoutId` v projekci = `CartSnapshotTaken` imported/resynced). Události nejdou do outboxu. Historie `/Cart` a `/Admin/CartInsights`
  = projekce při čtení (`CartHistoryService`). Testy: `CartEventSourcingTests` (Given–When–Then), `KosikES_*`.
- **Pokladna (e-shop krok 4A):** `CheckoutService` (`Application/Ordering/Checkout`) – objednávka z košíku a vyprázdnění
  košíku v jednom `SaveChanges`. `Cart.CheckoutId` = verze obsahu (nová při každé změně košíku) **a** concurrency token;
  formulář `/Checkout` ji posílá zpět: stejné ID podruhé = vrátit hotovou objednávku (`Orders.CheckoutId` unikátní,
  `POST /api/orders` má volitelné `checkoutId`). Pravidla v `Cart.CheckOut` (verze, nic staženého/vyprodaného, cena =
  `PriceWhenAdded`, jinak `AcceptCurrentPrices`). Doprava `Order.SetShipping` + strategie `IShippingPriceCalculator`
  (99 Kč, od 1 000 Kč zdarma); `TotalPrice` = položky + doprava (`OrderPlacedV1` beze změny). Adresa = hodnotový objekt
  `DeliveryAddress` (owned type, jen kurýr, `Anonymize` ji maže). Konflikty EF (`DbUpdateConcurrencyException`, unikátní
  index) převádí `ModuleDbContext` na `ConcurrencyConflictException` (`Application/Exceptions`, API 409) – aplikační vrstva
  EF nezná. Od kroku 4B jediná cesta k objednávce z webu (formulář na `Orders/Index` a „Objednat“ ve výloze zmizely).
- **Soubory z `wwwroot` (CSS, JS, knihovny) vždy s `asp-append-version="true"`** – jinak prohlížeč po nasazení
  použije starou verzi z cache (e-shop krok 3: košík bez `.cart-thumb` ukázal obrázky v plné velikosti).
- **Stránka s `[BindProperty]` a dalším formulářem:** když formulář pole vlastnosti neposílá, binder ji naváže BEZ prefixu
  (klíče `Name`, `Price`…) – chyby nejdou odfiltrovat podle prefixu. V takovém handleru `ModelState.Clear()` a ověřit jen
  to, co formulář posílá (`TryValidateModel(x, "x")`). Test formuláře = poslat skutečná pole jako prohlížeč.
- `MiniShop.Application` **nemá žádné NuGet balíčky** (ani DI ani Logging) – předchází se tím konfliktu verzí
  (NU1605). Registrace aplikačních služeb a handlerů proto žije ve
  `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs`, infrastruktury v
  `src/MiniShop.Infrastructure/DependencyInjection.cs`.
- Entity mění stav jen přes metody s business pravidly; čas se předává parametrem (z `TimeProvider`),
  doména nevolá `DateTime.UtcNow`.
- Logování přes **Serilog** (jen ve službách – Web a Inventory.Api, ten do `logs/inventory-RRRRMMDD.log`): konzole + denní soubor `src/MiniShop.Web/logs/minishop-RRRRMMDD.log`
  (7 dní, v `.gitignore`; v kontejneru `/data/logs/minishop-{Instance}-.log` – škálování krok 5: `PerInstanceLogFiles` dosadí jméno
  kontejneru, každá kopie vlastní soubor). Úrovně a cíle v sekci `Serilog` v appsettings, ne v `Logging`. Ostatní vrstvy
  logují přes `ILogger<T>` a o Serilogu nevědí. `UseSerilogRequestLogging` musí být v pipeline **před** `UseExceptionHandler`
  (jinak loguje ošetřené 404/400 jako 500).
- **Všechny moduly mají EF Core migrace** (PostgreSQL, schéma na modul, historie migrací ve schématu modulu) –
  **žádnou DB ani schéma nemazat**, změna schématu = nová migrace: `dotnet tool restore`, pak
  `dotnet ef migrations add <Nazev> --project src/MiniShop.Infrastructure --context <X>DbContext --output-dir <Modul>/Migrations`
  (design-time továrny `DesignTimeDbContextFactories.cs`). Při startu je spustí `…DatabaseInitializer.MigrateAsync` →
  `Persistence/DatabaseMigrator`. **Jména migrací neměnit** (`…_InitialPostgres` atd. – produkce je má v historii; hlídá
  `DatabaseMigrationTests.Migrace_ProdukcniHistorieZustavaPlatna`), stejně jako `HasPendingModelChanges` a tabulky jen ve schématu modulu.
  Ruční SQL (data) v migraci EF nevygeneruje – dopsat ručně (tak chyběla kategorie „Nezařazeno“).
  Migrace nesmí ztratit data (pozor na přejmenování – EF z něj udělá DROP + ADD; zkontrolovat vygenerovaný kód).
  **Migrace musí fungovat i se starší verzí kódu** (škálování krok 5B – postupné nasazení: stará a nová verze běží chvíli
  souběžně): přidat sloupec (nullable/s výchozí hodnotou) nebo tabulku ano, přejmenovat/smazat jen ve dvou nasazeních (expand → contract).
- Testy OrderService používají skutečný dispatcher a skutečné handlery (testují celou cestu).
  Služby skládají přes `AddInfrastructure` nad čerstvou PostgreSQL databází (`TestDatabases`), každá akce
  běží ve vlastním scope a outboxy všech modulů se zpracují ručně přes `ProcessOutboxAsync()` – žádné čekání na workera.
  Produkty se v testech zakládají přes `IProductService` (sklad) a do katalogu Orderingu se dostanou až zpracováním outboxu.
- Handlery běží asynchronně (krok 5): `SaveChangesAsync` jen ukládá do outboxu, doručuje `OutboxWorker`.
- Handlery doménových událostí se registrují **jen přes `AddDomainEventHandler<TEvent, THandler>()`**
  (`Infrastructure/Ordering/OrderingInfrastructure.cs` – doménové události přes outbox má jen Ordering) – obalí je
  dekorátorem `IdempotentDomainEventHandler` (krok 6). Nikdy ne přímo `AddScoped<IDomainEventHandler<…>>`.
- Handlery integračních zpráv (`IIntegrationMessageHandler<T>`) se registrují **jen přes
  `AddIntegrationMessageHandler<TMessage, THandler>(modul)`** (krok 9); modul (`IntegrationModules.Ordering/Inventory`)
  = vlastní fronta `minishop.{modul}` v RabbitMQ. Příkaz (`IIntegrationCommand`) smí mít jen jednoho handlera (hlídá `IntegrationMessageRouting`).
  Modul musí být zaregistrovaný přes `AddIntegrationModule<TDbContext>(modul)` – určuje, ve které DB leží inbox jeho handlerů.
- **Observabilita (krok 11):** trace ID (W3C `traceparent`) se přenáší ve „obálce“, ne v kontraktu – `OutboxMessage.TraceParent`
  (plní `OutboxMessageFactory` z `Activity.Current`) a hlavička `traceparent` v RabbitMQ. Spany a metriky zpráv jen přes
  `MessagingTelemetry` (zdroj `MiniShop.Messaging`, `System.Diagnostics` – Infrastructure nemá balíčky OpenTelemetry).
  OpenTelemetry je jen v `ServiceDefaults` (`AddSource/AddMeter("MiniShop.*")`). Nový background worker, který ukládá zprávy,
  má otevřít vlastní span (jako `OrderSagaTimeoutWorker`), jinak zprávy nemají trasu. SQL spany jen s rodičem (filtr v EF instrumentaci).
  Číslo objednávky na span dává `TraceTags.TagOrder` (`Application/Abstractions`, tag `minishop.order.id`) – volá se tam, kde se pracuje s objednávkou.
  Serilog: `ClearProviders()` + `writeToProviders: true` (logy i do OTLP, na konzoli jednou).
- Objednávka: Pending → Confirmed/Rejected (rozhoduje sága), pak expedice (e-shop krok 6) Confirmed → Packing → Shipped
  → Completed (= doručeno; `MarkDelivered`, událost dál `OrderCompleted`/`OrderCompletedV1`). Zrušit kdykoli před odesláním
  (Pending, AwaitingPayment, Confirmed, Packing – e-shop krok 9), po odeslání ne; doručenou jde vrátit (e-shop krok 12: Completed → ReturnRequested → Returned). Nové hodnoty `OrderStatus`/`OrderSagaState` jen na konec (ukládá se text). `POST /api/orders` vrací 202.
  **Historie stavů (e-shop krok 5):** stav měnit jen přes privátní `Order.ChangeStatus` (nastaví `Status` + přidá
  `OrderStatusChange`, tabulka `OrderStatusChanges`, pořadí podle `Id`, zvýší `Version`) – **nový stav/přechod = přes `ChangeStatus`**.
  Detail `/Orders/{id}` (`GetDetailAsync`, cizí = 404), pokladna sem přesměruje; Zrušit postuje na `Orders/Index`.
  Staré objednávky mají u posledního stavu `ChangedAtUtc = NULL` (migrace čas nevymýšlí).
  **Expedice (e-shop krok 6):** `/Admin/Fulfilment` (jen admin; fronty Confirmed/Packing/Shipped, hromadné balení, Doručeno =
  simulace dopravce). Kurýr: `Ship` vyžaduje `Carrier` + `TrackingNumber`, osobní odběr je nemá (texty v `Web/Orders/OrderStatusLabels`).
  `Order.Version` = concurrency token → `ConcurrencyConflictException` (stránky: `Orders.IndexModel.ConcurrencyMessage`, API 409).
  `OrderShippedV1` = **událost** (choreografie): sklad `FulfilReservationHandler` → `StockReservation.Fulfil()`; sága jen
  `Reserved → Shipped → Completed`, nic neposílá. Aktivní rezervace = ani uvolněná, ani vydaná.
- **Notifikace (e-shop krok 7):** modul Notifications (`…Notifications` ve všech vrstvách, hostuje Web, schéma `notifications`
  s inboxem, fronta `minishop.notifications`) – **Ordering zákazníkovi nepíše** (hlídá `ModuleBoundariesTests.Ordering_NeposilaEmaily`),
  jen překladač publikuje fakta: `OrderPlacedV1` + `OrderPlacedV2` (snímek: jméno, položky, doprava, adresa), `OrderConfirmedV1`,
  `OrderRejectedV1`, `OrderShippedV1`, `OrderCompletedV1`, `OrderCancelledV1`; smazání účtu `CustomerAnonymizedV1` (z `AnonymizeCustomerAsync`).
  `OrderNotificationsHandler` uloží příjemce (`OrderRecipient`) z V2, pak hledá podle `OrderId`; chybí-li (zpráva mimo pořadí)
  u potvrzení/zamítnutí výjimka → opakování, nejvýš `MaxWaitForPlacedOrder` (5 min); odeslání/doručení/zrušení bez příjemce =
  kratší e-mail na adresu ze zprávy. Texty jen v `OrderEmailTemplates` (text + HTML, `WebUtility.HtmlEncode`), odkaz z
  `Notifications:PublicBaseUrl` (produkce `PUBLIC_BASE_URL` v `production.env`). **Nový e-mail o objednávce = šablona + handler
  v Notifications, ne v Orderingu.** Testy: `OrderServiceTests` mají modul Notifications + `RecordingEmailSender` (`FailNext`).
- **Platba (e-shop krok 8):** modul Payments (`…Payments` ve všech vrstvách, hostuje Web, schéma `payments` s outboxem + inboxem,
  fronta `minishop.payments`) = druhý účastník ságy. Kartou (`Order.PaymentMethod`, volba v pokladně): `StockReserved` →
  saga `AwaitingPayment` (termín `Sagas:PaymentTimeoutMinutes`, 30) + `Order.AwaitPayment` + `RequestPaymentV1`; `PaymentSucceededV1`
  → `Confirm`; `PaymentFailedV1`/timeout → `RejectOrderAndReleaseStock` (nezaplaceno = **Rejected**, ne Cancelled); pozdní
  platba → `RefundPaymentV1` (jen jednou, `RefundRequested`). Dobírka a objednávky z API (`PaymentMethod.None`) platbu přeskočí.
  Webhook `POST /api/payments/webhook` (`[AllowAnonymous]`, podpis HMAC v `X-MiniShop-Signature`, `PaymentWebhookProcessor`:
  nejdřív podpis, pak obsah; 401/400/404/200, opakovaný = 200 bez efektu). Brána jen přes `IPaymentGateway` (dnes `FakePaymentGateway`
  + stránka `/Pay/{orderId}`, `FakePaymentGatewaySimulator` volá stejný procesor). Tajemství `Payments:WebhookSecret` (min. 32,
  jinak se Web **nespustí**): Aspire parametr `payments-webhook-secret`, dev `appsettings.Development.json`, Compose
  `PAYMENTS_WEBHOOK_SECRET` (povinný), produkce secret → `deploy.yml` → `deploy.sh`. Testy: `MiniShopWebFactory.WebhookSecret`,
  `OrderServiceTests` mají modul Payments (`SimulateGatewayAsync`).
  **Stripe (e-shop krok 16):** `Payments:Gateway` = `Fake` (výchozí, testy, lokálně) | `Stripe` (`StripeSettings.FromConfiguration`;
  jen `sk_test_`, bez klíčů/`Notifications:PublicBaseUrl` se Web **nespustí**). `StripePaymentGateway` (Stripe.net, Checkout Session,
  `metadata.order_id`, `expires_at` = náš termín + 30 min, **idempotency key = `RequestPaymentV1.CommandId`** – v parametrech nic
  proměnlivého; `RefundAsync` přes PaymentIntent session, klíč podle `Payment.Id`). Webhook `/api/payments/stripe-webhook`
  (`StripeWebhookProcessor`: `Stripe-Signature`, 5 min, platba podle `order_id` **a** `ExternalId` = ID session – testovací účet sdílí
  vývoj i produkce; cizí/nezajímavé = 200) → `PaymentWebhookProcessor.ApplyResultAsync`. Se Stripem `/Pay` přesměruje (GET) na bránu,
  simulovaná tlačítka i `/api/payments/webhook` = 404. Produkce `PAYMENTS_GATEWAY` v `production.env`, secrets `STRIPE_SECRET_KEY`,
  `STRIPE_WEBHOOK_SECRET` (volitelné). Testy: `StripeGatewayTests` (podvržený `HttpClient`, ručně podepsané události).
- **Zrušení (e-shop krok 9):** sága zrušení zaznamená a pozdní odpovědi dožene kompenzací: `AwaitingReservation` →
  `OrderSagaState.Cancelled` (pozdní `StockReserved` → `ReleaseStock`), `AwaitingPayment` → `ReleaseStock` (pozdní platba →
  `RefundPayment`), `Reserved` kartou → `ReleaseStockAndRefundPayment`. **`OrderSagaOrchestrator.StepAsync` před každým krokem
  načte objednávku a je-li `Cancelled`, nejdřív dožene `saga.OrderCancelled`** (odpověď může předběhnout `OrderCancelled`
  z outboxu) – nový krok ságy tím projde automaticky. Žádný kontrakt „zruš platbu“: `/Pay` u zrušené objednávky nepustí
  zaplatit (`CanPay`), platbu, která přesto projde, vrátí sága.
- **Opuštěný košík (e-shop krok 10):** `CartReminderWorker` (Web, `Carts:ReminderCheckMinutes` 15) → `CartReminderService`
  (Ordering): `Cart.IsAbandoned` (obsah, `Carts:AbandonedAfterHours` 24 beze změny, `ReminderSentAtUtc` null) →
  `MarkReminderSent` + `CartAbandonedV1` (jen ID účtu + položky) v jedné transakci; vyprázdnění košíku značku maže
  (jedna připomínka na nákup), `CheckoutId` se nemění. Bez doménové události. Notifications `CartReminderHandler` ověří
  **až při odeslání** přes `ICustomerContacts` (implementace `Web/Accounts/IdentityCustomerContacts` nad schématem `accounts`)
  potvrzený e-mail a `ApplicationUser.CartRemindersConsent` (výchozí false, stránka `/Account/EmailPreferences`).
  Šablona `CartEmailTemplates`. Testy: `InMemoryCustomerContacts`, `RunCartReminderWorkerAsync`.
- **Přehled tržeb (e-shop krok 11, CQRS):** modul Reporting (`…Reporting` ve všech vrstvách, hostuje Web, schéma `reporting` s inboxem
  + outboxem, fronta `minishop.reporting`) = čtecí model `SalesOrder` z existujících událostí (`OrderPlacedV2`, `OrderConfirmedV1`,
  `OrderRejectedV1`, `OrderCancelledV1`, `OrderCompletedV1`, `PaymentRefundedV1`) – Ordering o něm neví. Zprávy mimo pořadí: upsert
  (`SalesOrder.Create`) + **stav jen dopředu** (`ApplyStatus`). Bez osobních údajů. Přestavění (`SalesReportService.RebuildAsync`,
  tlačítko na `/Admin/Sales`, automaticky nad prázdnou DB – `SalesReportOnStartupWorker`): smazat + `RebuildSalesReportV1` →
  Ordering `RebuildSalesReportHandler` → `OrderSnapshotV1` pro každou objednávku (**ne** `OrderPlacedV2` – rozeslalo by e-maily).
  **Nový údaj v přehledu = projekce + `RebuildSalesReportHandler`/snímek, pak Přestavět.** Součty v paměti, dny v `Europe/Prague`.
- **Vrácení zboží (e-shop krok 12):** druhá sága `ReturnSaga` (tabulka `ReturnSagas`, klíč = `OrderId` → jedna vratka na objednávku)
  + `ReturnSagaOrchestrator`. `Order.RequestReturn` jen z `Completed`, do `Returns:WindowDays` (14) od `DeliveredAtUtc` (první Completed
  v historii) a jen jednou → `OrderReturnRequested` → sága `AwaitingGoods` (termín `Returns:SendWindowDays`) + `ReturnRequestedV1`.
  Admin `/Admin/Returns` volá orchestrátor přímo (krok člověka): Přijato → `RestockReturnedGoodsV1` (sklad podle **vydaných** rezervací,
  `StockReservation.MarkReturned`) → `ReturnedGoodsRestockedV1` → kartou `RefundPaymentV1` (Payments beze změny) → `PaymentRefundedV1`
  (bez ságy vratky ignoruje) → `Order.CompleteReturn` → `ReturnCompletedV1`; dobírka bez refundu (peníze ručně, `RefundedByCard=false`).
  Zamítnutí/timeout (`OrderSagaTimeoutWorker`) → `Order.RejectReturn` → zpět `Completed` + `ReturnRejectedV1`. Reporting: `Returned` se
  nepočítá do tržby (`SalesOrder` z `Completed` jen na `Returned`). Testy: `DeliveredOrderAsync`, `Returns(…)`.
- **Slevové kódy (e-shop krok 13):** agregát `Coupon` v Orderingu (tabulka `Coupons`, unikátní `Code` velkými písmeny, `Version` =
  concurrency token). Pokladna: `ApplyCouponAsync` jen ověří a uloží `Cart.CouponCode` (nová verze košíku); **použití se počítá až v
  `PlaceOrderAsync` ve stejné transakci** jako objednávka (`Order.ApplyCoupon` → `Coupon.Redeem`, sleva **rozpočítaná do
  `OrderItem.Discount`** – kontrakty beze změny; doprava zdarma z ceny po slevě). Souběh o poslední použití = `ConcurrencyConflictException`
  → hláška „kód mezitím použil někdo jiný“. Jeden zákazník jednou (`IOrderRepository.HasUsedCouponAsync`, jen platné objednávky).
  Kompenzace `ReleaseCouponHandler` na `OrderRejected`/`OrderCancelled` (vratka ne). Formulář kódu je mimo sledovaný formulář
  pokladny (do `Replay.cshtml` nepatří). Admin `/Admin/Coupons`. Testy: `CreateCouponAsync`, `PlaceWithCouponAsync`.
- **Faktury (e-shop krok 15):** modul Invoicing (`…Invoicing` ve všech vrstvách, hostuje Web, schéma `invoicing` s inboxem + outboxem,
  fronta `minishop.invoicing`; **musí do zálohy** – čísla nejde přestavět). Kopie `BillableOrder` z `OrderPlacedV2`; zaplaceno =
  `PaymentSucceededV1` (karta) nebo `OrderCompletedV1` (dobírka/API, platí první); `OrderRejectedV1`/`OrderCancelledV1` před vystavením =
  nefakturovat. Faktura + číslo z `InvoiceNumberSeries` (řádek na rok, `Version` = concurrency token, **ne sekvence** – díry) +
  `InvoiceIssuedV1` v **jedné transakci**; souběh = konflikt → consumer zopakuje. Faktura se nemění (dobropis = krok 15B), po smazání účtu
  zůstává (10 let). PDF `QuestPdfInvoiceRenderer` (QuestPDF, `LicenseType.Community`, písmo Lato v knihovně) při stažení
  `/Orders/{id}/Invoice` (vlastník/admin, jinak 404) a do přílohy: Notifications `InvoiceEmailHandler` → `IInvoiceDocuments`
  (implementace `Web/Invoicing/InvoiceDocuments`) → `EmailMessage.Attachments`. Admin `/Admin/Invoices`. Prodávající `InvoiceSeller`
  (`Invoicing:Seller:*`, fiktivní, neplátce DPH). Testy: `PaidCardOrderAsync`, `InvoiceAsync`, `NotifierEvent` = `invoice:{id}`.
  **Dobropis (e-shop krok 15B):** faktura existuje + `BillableOrder.CreditReason` (z `OrderCancelledV1`/`OrderRejectedV1`/`ReturnCompletedV1`,
  platí první) → jeden `CreditNote` (kopie **faktury**, unikátní `OrderId`) + číslo z `CreditNoteNumberSeries` (`D2026000001`, **vlastní
  tabulka** – změna klíče řady faktur by rozbila postupné nasazení) + `CreditNoteIssuedV1` v jedné transakci → `InvoiceEmailHandler`
  (PDF `IInvoiceDocuments.GetCreditNoteAsync`). Jen celý dobropis; peníze vrací dál sága/Payments. `/Orders/{id}/CreditNote`.
  Testy: `CreditNoteAsync`, `NotifierEvent` = `credit-note:{id}`.
- **Hodnocení produktů (e-shop krok 14):** modul Reviews (`…Reviews` ve všech vrstvách, hostuje Web, schéma `reviews` s inboxem + outboxem,
  fronta `minishop.reviews`; **musí do zálohy** – nejde přestavět). Kopie nákupů `PurchasedOrder` z `OrderPlacedV2`, `OrderCompletedV1`
  (doručeno), `ReturnCompletedV1` (vráceno), `OrderSnapshotV2`, `CustomerAnonymizedV1` (smazat nákupy, autor „Smazaný zákazník“) – upsert,
  doručeno/vráceno jen zapnout. Hodnotí jen doručeno + nevráceno (`FindReviewableAsync`), jedno na zákazníka a produkt (filtrovaný unikátní
  index), autor „Jan N.“. Každá změna (i skrytí adminem) = `ProductRatingChangedV1` ve stejné transakci → Ordering `CatalogProjectionHandler`
  → `CatalogProduct.ApplyRating` (starší nepřepíše) → výloha hvězdičky + `CatalogSort.BestRated`. Nákupy z doby před modulem:
  `PurchasesSyncOnStartupWorker` → `SyncPurchasesV1` → Ordering `SyncPurchasesHandler` → `OrderSnapshotV2` (se zákazníkem, bez
  anonymizovaných). Stránky `/Shop/{id}` (veřejná, `OnPostReview` jen zákazník), `/Admin/Reviews`. Testy: `Reviews(…)`, `DeliverApiOrderAsync`.
- **Provoz zpráv (EDA krok 13):** každý modul se zprávami má `IMessageStore` (registruje `AddIntegrationModule`) – úklid (`MessageRetentionWorker`,
  `Messaging:Retention:OutboxDays` 7 / `InboxDays` 30; mazat jen zpracované, poison nikdy; **inbox delší než nejdelší opakované doručení**),
  přehled a „Zkusit znovu“. Outbox: `NextAttemptAtUtc` + `OutboxProcessor.RetryDelay`. Consumer (`RabbitMqQueueConsumer`): selhání = kopie
  s hlavičkou `x-minishop-attempt` zpět do fronty + ack originálu, po `RabbitMqDeadLetters.MaxDeliveryAttempts` (5) nebo u nečitelné
  zprávy do `minishop.{modul}.dead` (nové fronty – **existujícím frontám neměnit argumenty**). `/Admin/Messages`, health check `dead-letters`.
- **Průběh objednávky (krok 12):** modul Journeys (`…Journeys` ve všech vrstvách, hostuje ho Web, vlastní schéma `journeys`,
  bez zpráv → `JourneysDbContext` nedědí z `ModuleDbContext`). Proud `JourneyEvents` = jen INSERT, idempotence přes unikátní `EventId`
  z prohlížeče, řazení podle `OffsetMs` (čas od otevření formuláře). `ValidationFailed`/`OrderSubmitted` zapisuje jen server
  (stránka `Checkout/Index` – od e-shop kroku 4B, dřív `Orders/Index`), Ordering o Journeys neví (hlídá `ModuleBoundariesTests`). Průběh = projekce při čtení
  (`OrderJourneyProjection`, fold). Sledování nesmí rozbít objednávku (chyba zápisu jen do logu). Ukládá se skutečně napsaný text.
  Automatické obnovení stránky Objednávky jen atributem `data-reload-when-idle` (`wwwroot/js/orders-reload.js`, nespustí se, když uživatel vyplňuje).
  Přehrání (12B) = druhá projekce `JourneyReplayProjection` (scénář počítá server, `journey-player.js` jen přehrává);
  `Replay.cshtml` má ruční kopii pokladny – **nové pole pokladny přidat i tam** (a kopii původního formuláře pro staré
  proudy; formulář pozná `OrderJourneyDto.Form` z `FormOpened.Target`, staré proudy jméno nemají = `order-form`).
  Sledovaný formulář = `data-journey-id` + `data-journey-form` (ne `data-track` – ten zapisuje kliky). Nové typy událostí jen na konec
  `JourneyEventType` (ukládá se jako text). Prvky, které má přehrávač najít, mají `data-track`.

## Dokumentace v aplikaci (`/Docs`, menu Dokumentace)
- Zdroj: Markdown + Mermaid v `docs/prirucka/` (hlavička `title`/`summary`/`order`), Web je vykreslí za běhu
  (`src/MiniShop.Web/Documentation/`, Markdig; Mermaid lokálně ve `wwwroot/lib/mermaid`). Soubory se kopírují do buildu
  i do image (`COPY docs/prirucka` v Dockerfile Webu, výjimka v `.dockerignore`); v Development se čtou přímo z repozitáře.
- Odkazy: na stránku relativně `[x](../eda/inbox.md)`, do kódu `[x](code:src/…)`, v diagramu `click uzel "/Docs/…"`.
  Automatické bloky `{{auto:contracts|domain-events|journey-event-types|nuget-packages}}` (balíčky z `*.deps.json`).
- **Změna chování/struktury = upravit příslušnou stránku ve stejné změně.** `DocumentationTests` hlídají odkazy na stránky
  i soubory, zmínku každého projektu ve `src/`, kontraktu, doménové události, NuGet balíčku (katalog `treti-strany/`),
  Docker image, GitHub Action, secretu/proměnné workflow a souboru v `deploy/` a `infra/`.
- Push do `main` jen se změnou `docs/prirucka/**` spustí CI i nasazení (`paths` v `ci.yml`); jiné změny jen v `docs/` a `*.md` ne.
- Sekce: přehled, architektura, EDA, toky (sekvenční diagramy), nasazení, GitHub a CI/CD, třetí strany, slovníček, historie.

## Stav EDA (viz `docs/EDA-roadmap.md`)
| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Doménové události (`Entity`, `IDomainEvent`, `OrderPlaced/Completed/Cancelled`) | hotovo | — |
| 2. In-process dispatcher (`DomainEventDispatcher`, dispatch po `SaveChangesAsync`) | hotovo | `docs/kroky/krok-02-dispatcher.md` |
| 3. Handlery místo přímých volání v `OrderService` | hotovo | `docs/kroky/krok-03-handlery.md` |
| 4. Outbox pattern (`OutboxMessages` v téže transakci jako data) | hotovo | `docs/kroky/krok-04-outbox.md` |
| 5. Background worker (`OutboxWorker` → `OutboxProcessor`, jediná cesta doručení, retry max. 5×) | hotovo | `docs/kroky/krok-05-worker.md` |
| 6. Idempotentní handlery (`EventId`, inbox `ProcessedMessages`, dekorátor `IdempotentDomainEventHandler`) | hotovo | `docs/kroky/krok-06-inbox.md` |
| 7. Integrační události a kontrakty (`MiniShop.Contracts`, překladový handler, `Kind` v outboxu, `IIntegrationBus`) | hotovo | `docs/kroky/krok-07-integracni-udalosti.md` |
| 8. Message broker (RabbitMQ.Client, exchange fanout na kontrakt, publisher confirms, ukázkový consumer `minishop.audit`) | hotovo | `docs/kroky/krok-08-rabbitmq.md` |
| 9. Sága objednávky – orchestrace (`OrderSaga` + `OrderSagaOrchestrator`, příkazy `ReserveStockV1`/`ReleaseStockV1`, modul Inventory, timeout, kompenzace) | hotovo | `docs/kroky/krok-09-saga.md` |
| 10A. Modulární monolit (2 DB, outbox/inbox na modul, kopie katalogu `CatalogProduct`, test architektury) | hotovo | `docs/kroky/krok-10a-modularni-monolit.md` |
| 10B. Vyjmutí Inventory do samostatné služby (`MiniShop.Inventory.Api`, .NET Aspire AppHost, ServiceDefaults, `InventoryApiClient`, resilience) | hotovo | `docs/kroky/krok-10b-samostatna-sluzba.md` |
| 11. Observabilita (OpenTelemetry, `TraceParent` v outboxu + hlavička `traceparent` v RabbitMQ, metriky, health check outboxu) | hotovo | `docs/kroky/krok-11-observabilita.md` |
| 12A. Průběh objednávky – proud událostí z formuláře (`JourneyEvents`, modul Journeys), projekce časová osa + souhrn (`/Orders/Journey/{id}`) | hotovo | `docs/kroky/krok-12a-prubeh-objednavky.md` |
| 12B. Přehrání průběhu objednávky (`/Orders/Replay/{id}`, `JourneyReplayProjection`, `journey-player.js`, událost `FieldInvalid`) | hotovo | `docs/kroky/krok-12b-prehrani-objednavky.md` |
| 13. Úklid a odolnost zpráv (`MessageRetentionWorker` – outbox 7 / inbox 30 dní, `OutboxMessage.NextAttemptAtUtc` backoff, consumer: počet pokusů v hlavičce + `minishop.{modul}.dead`, `/Admin/Messages`, health check `dead-letters`) | hotovo (nasazeno 4. 11. 2026, stránka Zprávy a dead-letter fronty ověřeny v produkci) | `docs/kroky/krok-13-uklid-a-odolnost-zprav.md` |

Známé omezení po kroku 11: vnější akce (notifikace) nejsou v transakci – při pádu mezi odesláním a zápisem
do inboxu odejdou znovu. Inbox i outbox uklízí `MessageRetentionWorker` (EDA krok 13). Workery s časovačem běží jen na vedoucí kopii služby (škálování krok 5).
`OrderingDomainEventReader` převádí payload zpět na událost načtením `Order` podle `OrderId` (handlery vidí aktuální stav).
Integrační zprávy jdou do RabbitMQ (`RabbitMqIntegrationBus`), nedostupný broker (`IntegrationBusUnavailableException`)
se nepočítá jako pokus. Od EDA kroku 13: outbox opakuje s backoffem (5/10/20/40 s), consumer nejvýš 5× (2/4/8/16 s) a pak do `minishop.{modul}.dead`.
Každý krok ságy čeká na kolo outbox workeru (2 s) → potvrzení objednávky trvá ~4–8 s, nový produkt je v nabídce po ~2–4 s.
Služby sdílí knihovny Application/Infrastructure (změna jádra = nasadit obě). Bez Dockeru se aplikace nespustí.
Obnova katalogu (`SyncCatalogV1`) produkty nemaže – zmeškané `ProductRemovedV1` neopraví.
Při výpadku skladu trvají první načtení stránek ~4 s, pak circuit breaker (ServiceDefaults) odpovídá hned.
Timeout ságy a obnova katalogu začínají vlastní trasu (nenavazují na původní požadavek). Telemetrie jde jen do Aspire
dashboardu (v paměti, bez vzorkování) – lokálně přes AppHost, v Compose/cloudu do kontejneru `aspire-dashboard`
(cloud krok 11); bez `OTEL_EXPORTER_OTLP_ENDPOINT` se neexportuje, trace ID je jen v logu a v outboxu.
Testy běží bez RabbitMQ (`RecordingIntegrationBus` nad `InProcessIntegrationBus` – celá sága v procesu).
Testy: `ProcessOutboxAsync` jede kola, dokud přibývají zprávy (selhané až po backoffu – `_time.Advance(OutboxProcessor.RetryDelay(n))`), `ProcessOneRoundAsync` = jedno kolo,
čas posouvá `FixedTimeProvider.Advance` (timeout ságy).

## Stav nasazení do cloudu (viz `docs/Cloud-roadmap.md`)
Cíl: Oracle Cloud Always Free (ARM VM) + Docker Compose + Caddy, CI/CD v GitHub Actions, image v GHCR, IaC v OpenTofu.
Rozpočet max. 100 Kč/měsíc. Souhrny kroků do `docs/kroky-cloud/`. Kroky v cloudové konzoli dělá uživatel sám.
- Image: `docker build -f src/MiniShop.Web/Dockerfile -t minishop-web .` (z kořene; totéž `src/MiniShop.Inventory.Api/Dockerfile`).
  Nový projekt, na kterém služba závisí, přidat do `COPY *.csproj` v obou Dockerfile.
- Konfigurace kontejneru jen proměnnými prostředí; výchozí cesty (`/data/keys`, `/data/logs`, sklad `/data/images`) nastavuje
  `ENV` v Dockerfile. RabbitMQ (`ConnectionStrings__messaging`) a adresu skladu (`Services__inventory-api__http__0`) dodá spouštěč.
- Web ukládá Data Protection klíče do `DataProtection:KeysPath` (jen když je nastavené – lokálně ne).
- Git Bash: u `docker exec … /cesta` použít `MSYS_NO_PATHCONV=1`.
- Compose (krok 2): `cd deploy && docker compose up -d --build` → http://localhost:8080, RabbitMQ UI http://localhost:15673
  (údaje z `deploy/.env`, vzor `.env.example`, `.env` je v `.gitignore`). `docker compose down` data nechá, `down -v` smaže volumes.
  Nová proměnná prostředí pro službu = přidat do `compose.yaml` (a tajnou hodnotu do `.env.example` jako vzor;
  pro produkci netajnou do `deploy/production.env`, tajnou viz Secrets níže).
  Healthcheck .NET služeb: bash `/dev/tcp` na `/health` (image nemá curl). `/health` nehlídá RabbitMQ.
- VM (krok 4): `ssh minishop-vm` (zkratka v `~/.ssh/config` uživatele → Tailscale IP `100.126.167.103`, uživatel `ubuntu`,
  klíč s passphrase – neinteraktivně jen přes ssh-agent). Aplikace v `/opt/minishop` (vlastník `deploy`, `.env` 600 –
  číst/měnit přes `sudo -u deploy`). Od kroku 7 image z GHCR: `.env` `IMAGE_PREFIX=ghcr.io/ftichy/`, `IMAGE_TAG=sha-<commit>`.
  Image publikuje CI jen z `main` po zelených testech (`ghcr.io/ftichy/minishop-web`, `…/minishop-inventory-api`, veřejné,
  amd64+arm64). Lokálně `IMAGE_PREFIX` prázdný → `minishop-…:local` z `build:`.
  Z PowerShellu přes `ssh "…"` pozor na `\n` a uvozovky – skript poslat souborem.
- CD (krok 8): po sloučení do `main` CI → `images` → `deploy` (`.github/workflows/deploy.yml`, environment `production`):
  runner přes Tailscale (`tag:ci`, OAuth) → `ssh deploy@100.126.167.103 "<celý SHA>"` → vynucený příkaz
  `/usr/local/bin/minishop-deploy` (= `deploy/deploy.sh`; stáhne `deploy/` z GitHubu pro commit, `pull`, `up --wait`,
  rollback). Ruční nasazení/rollback: *Actions → Deploy → Run workflow* se SHA, nebo na VM
  `sudo -u deploy minishop-deploy <SHA>`. Změna `deploy/deploy.sh` = ručně znovu nainstalovat na VM
  (**před** sloučením, pokud na změně závisí její vlastní nasazení). Změněný `Caddyfile` `deploy.sh` načte přes `caddy reload`.
  Port 22 je z internetu zavřený – SSH jen přes Tailscale. Nouzově: pravidlo 22 v security listu vrátit.
  Secrets/proměnné jen v environmentu `production`: `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET`, `DEPLOY_SSH_KEY`,
  `RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH` (krok 10), `SMTP_PASSWORD` (účty krok 5), `INVENTORY_API_KEY` (účty krok 7), `PAYMENTS_WEBHOOK_SECRET` (e-shop krok 8), `POSTGRES_PASSWORD` (škálování krok 3), `GOOGLE_CLIENT_SECRET` (účty krok 10, volitelný), `DEPLOY_HOST`, `DEPLOY_KNOWN_HOSTS`.
- Doména (krok 13): `https://minishop-tichy.cz` (Wedos: registrace + DNS, záznamy A `@` a `dashboard` → reserved IP, TTL 300,
  žádné AAAA – zadává uživatel ručně v client.wedos.com, pak *Aplikovat změny*). `DOMAIN` + `LEGACY_DOMAIN` v `production.env`;
  stará `minishop-tichy.duckdns.org` (i `dashboard.`) = 301 na novou doménu (Caddyfile), smoke test hlídá obojí (doména
  natvrdo v `deploy.yml`). Nové jméno = nejdřív DNS, pak nasazení (jinak Caddy nezíská certifikát).
- HTTPS (krok 5): dřív `https://minishop-tichy.duckdns.org` (reserved IP `141.144.255.238`), Caddy z `deploy/compose.cloud.yaml`
  + `deploy/Caddyfile`, na VM zapnuté přes `COMPOSE_FILE=compose.yaml:compose.cloud.yaml` v `.env`. Web není publikovaný ven.
  Basic auth (uživatel `minishop`, heslo zná jen uživatel – změna = secret `BASIC_AUTH_HASH`, viz `docs/secrets.md` R2):
  od účtů kroku 9 `BASIC_AUTH=off` v `deploy/production.env` → web veřejný, zámek jen před dashboardem.
  Caddy maže hlavičky `Server` a `Via` (snippet `hide-server`). Security list: 80/443 z celého internetu (22 od kroku 8 zavřený). Volume `caddy-data` (certifikáty) nemazat.
- IaC (krok 9): `infra/` (OpenTofu, provider `oracle/oci`), spouští uživatel na PC: `tofu init "-backend-config=backend.hcl"` (v PowerShellu v uvozovkách – jinak rozdělí argument s tečkou),
  `tofu plan`, `tofu apply` (přihlášení `~/.oci/config` = `minishop-automation`). State v bucketu `minishop-tfstate` (S3 API,
  klíč `~/.oci/s3-credentials`); `backend.hcl`, `terraform.tfvars` a state nejsou v gitu. VM připraví cloud-init
  (`cloud-init.yaml.tftpl` → `bootstrap.sh.tftpl`, log `/var/log/minishop-bootstrap.log`), `deploy/deploy.sh` jde na VM
  přes `filebase64`. VM má `ignore_changes` na obraz a metadata – změna cloud-init/`deploy.sh` = `tofu apply -replace=oci_core_instance.vm`
  (ztráta dat!). Reserved IP má `prevent_destroy`. Bash v šablonách `.tftpl`: jen `$var`, ne `${var}` (to je OpenTofu).
  Nová VM: nový Tailscale auth key, IP v Tailscale adminu vrátit na `100.126.167.103`, aktualizovat `DEPLOY_KNOWN_HOSTS`,
  pak *Run workflow* Deploy (od kroku 10 cloud-init aplikaci nespouští – nemá secrets) a data `restore.sh latest` (krok 12).
- Secrets (krok 10): registr všech secrets a postupy výměny v `docs/secrets.md` (nový secret = nový řádek tam).
  Secrets aplikace jen v GitHub environmentu `production` (`RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH`); Deploy je pošle
  na stdin SSH, `deploy.sh` přijme jen známé klíče a sestaví `.env` na VM pokaždé znovu (`deploy/production.env`
  + `IMAGE_TAG` + secrets) – ruční úpravy `.env` na VM nepřežijí. Netajná konfigurace produkce = `deploy/production.env`
  (změna přes PR). Nový secret aplikace = `deploy.yml` (env + printf), `deploy.sh` (case + kontrola tvaru), registr.
  Změněné heslo RabbitMQ `deploy.sh` vymění i v běžícím RabbitMQ (`rabbitmqctl change_password`). OpenTofu nemá secrets aplikace.
  Secret scanning + push protection a Dependabot (`.github/dependabot.yml`) zapnuté; historie ověřená gitleaks.
- Monitoring (krok 11): služba `aspire-dashboard` v `compose.yaml` (OTLP `aspire-dashboard:18889` přes `OTEL_EXPORTER_OTLP_ENDPOINT`
  + `OTEL_SERVICE_NAME`, telemetrie jen v paměti; lokálně http://localhost:18888, v cloudu https://dashboard.minishop-tichy.cz
  přes Caddy vždy s basic auth). Caddy pouští bez hesla jen `/health` (web) a `/health/inventory` (sklad) – hlídá je UptimeRobot
  (keyword `Healthy`, case sensitive). Smoke test v `deploy.yml` kontroluje obě na `Healthy`. Nová služba s telemetrií =
  `<<: *otel-env` + `OTEL_SERVICE_NAME` v compose. Alarmy VM (CPU, paměť, chybějící metriky) + e-mail v `infra/monitoring.tf`
  (OCI Monitoring + Notifications, `alert_email` v `terraform.tfvars`).
- Zálohy (krok 12): `minishop-backup.timer` (02:30 UTC, uživatel `deploy`) → `/opt/minishop/backup.sh` → `backup.py`
  v kontejneru `ghcr.io/oracle/oci-cli:20261007` (`minishop.pgdump` z `pg_dump` + `/data/keys` + obrázky skladu
  → `minishop-RRRRMMDDTHHMMSSZ.tar.gz` → bucket `minishop-backups`, lifecycle 7 dní). VM se přihlašuje jako instance principal
  (dynamická skupina `minishop-vm`, politika v `infra/backup.tf`: číst + vytvářet, ne mazat). Obnova:
  `sudo -u deploy /opt/minishop/restore.sh latest|<jméno>|list` (zastaví web + inventory-api, původní soubory nechá jako
  `*.before-restore-…`; škálování krok 4: `backup.py fetch` → ověření `minishop.pgdump` (pg_restore --list, schéma `ordering`) →
  `minishop` přejmenuje na `minishop_before_restore_<čas>` + `pg_restore` (chyba = vrácení) → `restore-files`; stará záloha bez dat odmítnuta;
  i z lokálního souboru `restore.sh /cesta/….tar.gz`; zkouška nanečisto: `MINISHOP_APP_DIR`, `COMPOSE_PROJECT_NAME` v .env). Skripty záloh přináší `deploy.sh` z gitu (volitelné soubory – starší commity je nemají),
  jednotky systemd cloud-init. Hlídač zálohy: healthchecks.io (`HEALTHCHECKS_BACKUP_URL`, volitelný secret).
  Od škálování kroku 3A `backup.sh` nejdřív `pg_dump --format=custom` (kontejner postgres) → `minishop.pgdump` v archivu (obnova = krok 4).
  Nová DB = `DATABASES`, nová složka s daty = `DIRECTORIES` v `backup.py` (od e-shop kroku 2B i `/inventory/images`). Před rizikovou akcí ruční záloha `sudo systemctl start minishop-backup`.
- CI (krok 6): workflow „CI“, job „Build a testy“ (= povinný status check v rulesetu, jméno neměnit; testy nad PostgreSQL – Testcontainers). Repozitář `ftichy/MiniShop` je veřejný –
  nic tajného do gitu. `main` jen přes PR se zeleným CI (bez přímého push). SDK určuje `global.json` (10.0.200, `latestFeature`).
  Nový projekt/závislost = ověřit, že CI na Linuxu projde.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Kontejnerizace a produkční konfigurace (Dockerfile multi-stage, uživatel `app`, data v `/data`, Data Protection klíče) | hotovo | `docs/kroky-cloud/krok-01-kontejnery.md` |
| 2. Docker Compose (`deploy/compose.yaml`, healthchecky, volumes, `.env`) | hotovo | `docs/kroky-cloud/krok-02-compose.md` |
| 3. Cloudový účet, rozpočet, IAM (OCI Frankfurt, compartment `minishop`, uživatel `minishop-automation`, OCI CLI z Dockeru) | hotovo (kvóta `minishop-quota`: v `minishop` jen A1 do 2 OCPU / 12 GB, od kroku 9 i `…-regional-count`) | `docs/kroky-cloud/krok-03-ucet-oci.md` |
| 4. Síť a VM ručně, první nasazení (VCN, security list, VM A1 Ubuntu 24.04, Docker, image přes `docker save` + `scp`) | hotovo | `docs/kroky-cloud/krok-04-sit-a-vm.md` |
| 5. Doména, HTTPS a zámek (DuckDNS `minishop-tichy.duckdns.org`, reserved IP, Caddy + Let's Encrypt, basic auth) | hotovo | `docs/kroky-cloud/krok-05-https.md` |
| 6. CI – build a testy (`.github/workflows/ci.yml`, `global.json`, veřejný repozitář, ruleset `main-protection`) | hotovo | `docs/kroky-cloud/krok-06-ci.md` |
| 7. Image do registry (GHCR, job `images`: PR zkušební build, `main` push x86+ARM, tagy `sha-…`/`latest`, veřejné) | hotovo | `docs/kroky-cloud/krok-07-registry.md` |
| 8. CD – automatické nasazení (`deploy.yml`, Tailscale, uživatel `deploy` s vynuceným příkazem, rollback) | hotovo | `docs/kroky-cloud/krok-08-cd.md` |
| 9. Infrastruktura jako kód (OpenTofu `infra/`: síť, VM, cloud-init, import reserved IP, state v OCI bucketu) | hotovo (ruční VM/VCN smazané, vše z `tofu apply`) | `docs/kroky-cloud/krok-09-iac.md` |
| 10. Secrets a konfigurace (secrets jen v GitHub `production` → stdin → `deploy.sh`, `production.env`, rotace RabbitMQ, registr `docs/secrets.md`, secret scanning, Dependabot) | hotovo (heslo RabbitMQ vyměněno pipeline, ověřeno objednávkou) | `docs/kroky-cloud/krok-10-secrets.md` |
| 11. Monitoring a logy v cloudu (Aspire dashboard v Compose, `/health` pro UptimeRobot, alarmy OCI v `infra/monitoring.tf`) | hotovo (UptimeRobot na `/health` a `/health/inventory`, alarmy OCI, zkouška výpadku – potvrzeno 3. 11. 2026) | `docs/kroky-cloud/krok-11-monitoring.md` |
| 12. Zálohy a obnova po havárii (noční online backup SQLite → OCI bucket přes instance principal, `restore.sh`, healthchecks.io, cvičná havárie) | hotovo (cvičná havárie: VM smazaná → tofu apply → Deploy → `restore.sh latest`, objednávky zpět) | `docs/kroky-cloud/krok-12-zalohy.md` |
| 13. Vlastní doména (`minishop-tichy.cz` u Wedosu, DNS A `@` + `dashboard`, `LEGACY_DOMAIN` → 301 ze staré DuckDNS adresy, smoke test) | hotovo (nasazeno 3. 11. 2026, smoke test vč. 301 ze staré adresy, UptimeRobot na nové doméně) | `docs/kroky-cloud/krok-13-vlastni-domena.md` |

## Stav uživatelských účtů (viz `docs/Ucty-roadmap.md`)
Role User (objednává, vidí a ruší jen své) a Admin (sklad, produkty, vyřízení, všechny objednávky a uživatelé, neobjednává).
Modul Accounts (ASP.NET Core Identity, schéma `accounts`). Souhrny kroků do `docs/kroky-ucty/`. Každý krok se nasadí do cloudu
(DB na volume, `backup.py`, secrets). Od kroku 9 je web veřejný (basic auth jen před Aspire dashboardem).
- Fallback policy = přihlášený: **nová veřejná stránka/endpoint potřebuje `[AllowAnonymous]`**, admin/zákaznické
  `[Authorize(Policy = AuthorizationPolicies.RequireAdmin/RequireCustomer)]` (`Web/Accounts`). Handlery stránky
  Objednávky ověřují policy v kódu (`IAuthorizationService`). **Nová stránka/akce/endpoint = řádek
  v `AuthorizationMatrixTests`** (Web v paměti přes `MiniShopWebFactory`, typ `InventoryApiClient` – `Program` je dvojznačný).
- Vlastník objednávky (krok 3): `IOrderService` dostává `OrderAccess` (čtení, zrušení) a `OrderCustomer` (založení)
  parametrem – skládá je Web z cookie (`User.ToOrderAccess()/ToOrderCustomer()` ve `Web/Accounts`). Cizí objednávka
  = `NotFoundException` (404). Jméno a e-mail objednávky jen z účtu, ne z formuláře/JSON.
- E-maily (krok 5): `IEmailSender` (`Application/Abstractions`) → `Infrastructure/Email` (MailKit; bez `Email:Host`
  jen log). Lokálně Mailpit v AppHostu, produkce Gmail `ftichy.gemini@gmail.com` (`EMAIL_*` v `production.env`,
  secret `SMTP_PASSWORD`, volitelný). E-maily účtu hned (`Web/Accounts/AccountEmails`), objednávek za outboxem
  (od e-shop kroku 7 modul Notifications ze zpráv).  Testy Webu: `MiniShopWebFactory.Emails` (`RecordingEmailSender`). Do logu jen předmět e-mailu.
- První admin: produkce `docker compose exec web dotnet MiniShop.Web.dll create-admin --email …` (jen bez admina),
  vývoj `AdminBootstrap` v `appsettings.Development.json` (`admin@minishop.local` / `admin-heslo-pro-vyvoj`, vynucená změna hesla).
- Správa uživatelů (krok 6): `AccountAdministration` (Infrastructure/Accounts) – role výhradní User ↔ Admin, blokace = lockout
  do `DateTimeOffset.MaxValue`, vždy zůstane aktivní admin, ne sám sobě, rozhoduje DB (ne cookie), vše v transakci,
  každá změna = nový security stamp + `AccountAuditEntries`. `SecurityStampValidator` 1 minuta. Stránky `/Admin/Users`
  (složka `/Admin` zamčená i konvencí `AuthorizeFolder`). Útrata z Orderingu `GetCustomerSummariesAsync` (Web spojí přes ID).
- Sklad (krok 7): Inventory.Api pouští celé `/api` jen s hlavičkou `X-Api-Key` (`ApiKeyAuthenticationHandler` + fallback policy;
  `/health`, `/alive` otevřené). Klíč `InventoryApi:ApiKey` (`ServiceApiKey` v ServiceDefaults, min. 32 znaků, jinak se služba
  **nespustí**): Aspire ho generuje (parametr `inventory-api-key`), bez Aspire `appsettings.Development.json` obou služeb,
  Compose `INVENTORY_API_KEY` (povinný – lokální `deploy/.env` ho musí mít), produkce secret. Web ho přidává v `InventoryApiKeyHandler`.
  Testy Webu: klíč v `MiniShopWebFactory`; sklad v paměti `InventoryApiFactory` (stejná kolekce kvůli Serilogu).
- Zpevnění (krok 8): **CSP `script-src 'self'` – žádné inline `<script>` ani `onclick=`**, skripty jen soubory ve `wwwroot/js`
  (data v `<script type="application/json">` smí; hlídá `HardeningTests`). Žádné CDN – knihovny lokálně ve `wwwroot/lib`.
  Hlavičky v `Web/Security/SecurityHeaders.cs` (+ HSTS mimo vývoj), `enduser.id` do trasy. Zámek účtu 5 / 15 min
  (`AccountsInfrastructure`, `lockoutOnFailure: true`); zablokovaný adminem = `LockoutEnd` rok 9999 (`IsBlocked`),
  dočasný zámek admina práva nebere. **Nový formulář účtu = `[EnableRateLimiting(AccountRateLimiting.PolicyName)]`**
  (limit jen POST, podle IP, `RateLimiting:AccountForms`; testy ho mají vysoko). Do logu nikdy e-mail ani heslo, jen ID účtu
  (`HardeningTests` to hlídá přes `MiniShopWebFactory.Logs`). Serilog = statický logger – test logů potřebuje vlastní instanci Webu.
- Otevřený web (krok 9): `RequireCustomer` = role User **+ claim `minishop:email_confirmed`** (testovací uživatelé mají
  `CreateUserAsync(emailConfirmed: true)`). Smazání účtu `/Account/Delete`: heslo → `IOrderService.AnonymizeCustomerAsync`
  (Ordering, nevyřízená objednávka = DomainException) → `AccountDeletion.DeleteAsync` (Accounts; admin ne, audit
  `ForgetUser`) → odhlásit. **Nový osobní údaj = doplnit anonymizaci a stránku `/Privacy`.** Kestrel bez hlavičky `Server`.
- Google (krok 10): `GoogleLogin.AddGoogleLogin` jen s `Authentication:Google:ClientId` + `ClientSecret` (lokálně user secrets Webu,
  Compose `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET`), jinak tlačítko zmizí. Pravidla v `ExternalAccountLinker` (Infrastructure/Accounts):
  **nepotvrzený e-mail se automaticky nepropojí** (pre-account hijacking), nový účet bez hesla s `EmailConfirmed`; přihlášení
  přes `ExternalLoginSignInAsync` (zámek/blokace). Návrat z Googlu s ID účtu ve `state` (`ConfigureExternalAuthenticationProperties(…, userId)`)
  pro propojení a smazání (`prompt=login`). Chyba z Googlu → `OnRemoteFailure` (jinak 500). **Nová akce s přesměrováním na cizí
  web = doplnit CSP `form-action`.** Testy: `FakeGoogleHandler` (identita z hlaviček `X-Fake-Google-*`), `web.Google.LastPrompt`.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Účty, registrace, přihlášení (modul Accounts, Identity, `users.db`) | hotovo | `docs/kroky-ucty/krok-01-ucty.md` |
| 2. Role a první admin (policies, fallback policy, `create-admin` / `AdminBootstrap` ve vývoji, matice oprávnění) | hotovo | `docs/kroky-ucty/krok-02-role.md` |
| 3. Vlastník objednávky (`CustomerId`, `OrderAccess`/`OrderCustomer`, filtr v SQL, cizí = 404, jméno a e-mail z účtu) | hotovo | `docs/kroky-ucty/krok-03-vlastnik-objednavky.md` |
| 4. Zrušení objednávky i ve stavu Pending (nový přechod ságy, kompenzace) | přesunuto – hotovo jako e-shop krok 9 | `docs/kroky-eshop/krok-09-zruseni.md` |
| 5. Skutečné e-maily (MailKit, Mailpit lokálně, Gmail SMTP v cloudu, potvrzení e-mailu, reset hesla) | hotovo | `docs/kroky-ucty/krok-05-emaily.md` |
| 6. Administrace uživatelů (přehled, povýšení, blokace, poslední admin, audit, EF migrace `users.db`) | hotovo | `docs/kroky-ucty/krok-06-sprava-uzivatelu.md` |
| 7. Zabezpečení skladu (servisní klíč `X-Api-Key` pro celé `/api` Inventory.Api, secret `INVENTORY_API_KEY`, fail fast; JWT odloženo) | hotovo | `docs/kroky-ucty/krok-07-zabezpeceni-skladu.md` |
| 8. Zpevnění (zámek 5 pokusů / 15 min, rate limiting formulářů účtu 10/min/IP → 429, CSP a bezpečnostní hlavičky, Bootstrap lokálně, `enduser.id`, logy bez e-mailů) | hotovo | `docs/kroky-ucty/krok-08-zpevneni.md` |
| 9. Otevření webu (`BASIC_AUTH=off`, objednávka jen s potvrzeným e-mailem, smazání účtu + anonymizace objednávek, `/Privacy`, bez hlaviček `Server`/`Via`; CAPTCHA odložena) | hotovo | `docs/kroky-ucty/krok-09-otevreni-webu.md` |
| 10. Přihlášení přes Google (OpenID Connect, `ExternalAccountLinker`: propojení jen s potvrzeným e-mailem, účet bez hesla + `SetPassword`, smazání po `prompt=login`, `/Account/ExternalLogins`, CSP `form-action` + Google, `GOOGLE_CLIENT_ID` v `production.env` + secret `GOOGLE_CLIENT_SECRET`) | hotovo (nasazeno 6. 11. 2026; OAuth klient v Google Cloud projektu MiniShop, aplikace publikovaná; v produkci ověřen nový účet přes Google, účet bez hesla, smazání po novém přihlášení u Googlu) | `docs/kroky-ucty/krok-10-google.md` |

## Stav e-shopu (viz `docs/Eshop-roadmap.md`)
Výloha → košík → pokladna → platba → expedice → doručení. Košík je agregát v modulu Ordering (checkout = jedna transakce),
nové moduly Notifications (odběratel událostí) a Payments (účastník ságy). Souhrny kroků do `docs/kroky-eshop/`.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Migrace databází (`ordering/inventory/journeys.db`, sdílený `DatabaseMigrator`, baseline produkční DB) | hotovo (nasazeno 30. 10. 2026, produkční DB prošly baseline, data zůstala) | `docs/kroky-eshop/krok-01-migrace.md` |
| 2A. Výloha (`/Shop`, kategorie, hledání `SearchText`, `ProductUpdatedV2` expand → contract, `StockAvailabilityChangedV1` při změně pásma) | hotovo (nasazeno 30. 10. 2026, produkční kopie katalogu se po startu sama doplnila) | `docs/kroky-eshop/krok-02a-vyloha.md` |
| 2B. Obrázky produktů (upload do skladu, typ podle bajtů, `/media/products/…` přes Web s immutable cache, `backup.py` `DIRECTORIES`) | hotovo (nasazeno 1. 11. 2026, obrázky ve výloze, noční záloha obsahuje `images/`) | `docs/kroky-eshop/krok-02b-obrazky.md` |
| 3. Košík (agregát `Cart` v Orderingu s invarianty, `/Cart`, odznak v hlavičce, ceny z kopie katalogu, bez zpráv) | hotovo (nasazeno 1. 11. 2026; oprava: `asp-append-version` – starý CSS z cache) | `docs/kroky-eshop/krok-03-kosik.md` |
| 4A. Pokladna (`/Checkout`, `CheckoutService`, `CheckoutId` = verze košíku + concurrency token, žádné tiché zdražení, `DeliveryAddress`, `IShippingPriceCalculator`, `checkoutId` v API) | hotovo (nasazeno 2. 11. 2026, ověřeno objednávkou v produkci) | `docs/kroky-eshop/krok-04a-pokladna.md` |
| 4B. Journeys na pokladnu (sledování a přehrání pokladny, `OrderJourneyDto.Form`, staré proudy beze změny, odstranění starého formuláře objednávky a „Objednat“ ve výloze) | hotovo (nasazeno 3. 11. 2026, přehrání pokladny ověřeno v produkci) | `docs/kroky-eshop/krok-04b-journeys-pokladna.md` |
| 5. Detail objednávky a historie stavů (`/Orders/{id}`, `OrderStatusChange` v agregátu, migrace doplní historii starým objednávkám) | hotovo (nasazeno 3. 11. 2026, detail a historie ověřeny v produkci) | `docs/kroky-eshop/krok-05-detail-objednavky.md` |
| 6. Expedice (Packing → Shipped → Completed, `/Admin/Fulfilment`, `OrderShippedV1` → sklad vydá rezervaci, `Order.Version` proti souběhu, e-mail s číslem zásilky) | hotovo (nasazeno 3. 11. 2026, celá expedice ověřena v produkci) | `docs/kroky-eshop/krok-06-expedice.md` |
| 7. Notifikace jako samostatný odběratel (modul Notifications, `notifications.db`, `OrderPlacedV2` + `OrderConfirmedV1`/`OrderRejectedV1`, `CustomerAnonymizedV1`, šablony s odkazem; Ordering bez e-mailů) | hotovo (nasazeno 3. 11. 2026, e-maily ověřeny v produkci) | `docs/kroky-eshop/krok-07-notifikace.md` |
| 8. Platba – simulovaná brána (modul Payments, `payments.db`, `RequestPaymentV1`/`RefundPaymentV1`, webhook s HMAC, `AwaitingPayment` + timeout 30 min, vrácení pozdní platby, dobírka) | hotovo (nasazeno 3. 11. 2026, platba kartou, zamítnutí na bráně i dobírka ověřeny v produkci) | `docs/kroky-eshop/krok-08-platba.md` |
| 9. Zrušení kdykoli před odesláním (i `Pending`/`AwaitingPayment`, stav ságy `Cancelled`, orchestrátor dožene zrušení, zrušená zaplacená = `ReleaseStockAndRefundPayment`, `/Pay` u zrušené nepustí platit) | hotovo (nasazeno 3. 11. 2026, zrušení před platbou, po platbě i dobírky ověřeno v produkci) | `docs/kroky-eshop/krok-09-zruseni.md` |
| 10. Opuštěný košík (`CartReminderWorker`, `CartAbandonedV1`, `Cart.ReminderSentAtUtc`, souhlas `CartRemindersConsent` ověřený až při odeslání přes `ICustomerContacts`, `/Account/EmailPreferences`) | hotovo (nasazeno 3. 11. 2026, nastavení souhlasu ověřeno v produkci; e-mail po 24 h) | `docs/kroky-eshop/krok-10-opusteny-kosik.md` |
| 11. Přehled tržeb pro admina – CQRS (modul Reporting, `reporting.db`, projekce se stavem jen dopředu, `/Admin/Sales`, přestavění `RebuildSalesReportV1` → `OrderSnapshotV1`) | hotovo (nasazeno 3. 11. 2026, přehled se po startu postavil ze snímků, přestavění ověřeno v produkci) | `docs/kroky-eshop/krok-11-prehled-trzeb.md` |
| 12. Vrácení zboží (druhá sága `ReturnSaga`, `/Admin/Returns`, `RestockReturnedGoodsV1` → `ReturnedGoodsRestockedV1`, `RefundPaymentV1`, `ReturnRequested/Rejected/CompletedV1`, dobírka ručně) | hotovo (nasazeno 3. 11. 2026, vratka kartou, zamítnutí i dobírka ověřeny v produkci) | `docs/kroky-eshop/krok-12-vraceni-zbozi.md` |
| 13. Slevové kódy (agregát `Coupon`, `/Admin/Coupons`, použití ve stejné transakci jako objednávka, `Version` proti souběhu o poslední použití, sleva v položkách, `ReleaseCouponHandler`) | hotovo (nasazeno 4. 11. 2026, uplatnění, limit použití a vrácení po zrušení ověřeny v produkci) | `docs/kroky-eshop/krok-13-slevove-kody.md` |
| 14. Hodnocení produktů (modul Reviews, `reviews.db`, kopie nákupů z událostí, `ProductRatingChangedV1` do kopie katalogu, `/Shop/{id}`, `/Admin/Reviews`, `SyncPurchasesV1` → `OrderSnapshotV2`) | hotovo (nasazeno 4. 11. 2026, hodnocení, hvězdičky ve výloze, starší objednávky ze snímků i moderace ověřeny v produkci) | `docs/kroky-eshop/krok-14-hodnoceni-produktu.md` |
| 15. Faktury (modul Invoicing, schéma `invoicing`, kopie objednávek z událostí, faktura po zaplacení/doručení, číselná řada bez děr, PDF QuestPDF, `InvoiceIssuedV1` → e-mail s PDF v příloze přes `IInvoiceDocuments`, `/Admin/Invoices`) | hotovo (nasazeno 6. 11. 2026; faktura po platbě kartou, e-mail s PDF v příloze, stažení i přehled admina ověřeny v produkci na ARM VM) | `docs/kroky-eshop/krok-15-faktury.md` |
| 15B. Dobropisy (zrušení/zamítnutí/vrácení zaplacené objednávky s fakturou → `CreditNote` z kopie faktury, řada `D…` v tabulce `CreditNoteNumberSeries`, `CreditNoteIssuedV1` → e-mail s PDF, `/Orders/{id}/CreditNote`, sloupec v `/Admin/Invoices`) | hotovo (nasazeno 9. 11. 2026; dobropis po zrušení zaplacené objednávky, e-mail s PDF a sloupec v přehledu faktur ověřeny v produkci) | `docs/kroky-eshop/krok-15b-dobropisy.md` |
| 16. Skutečná platební brána (Stripe Checkout v testovacím režimu jako `IPaymentGateway`, `/api/payments/stripe-webhook` se `Stripe-Signature`, idempotency key, skutečný refund, `Payments:Gateway` Fake/Stripe, secrets `STRIPE_SECRET_KEY`/`STRIPE_WEBHOOK_SECRET`) | hotovo (nasazeno 9. 11. 2026; platba kartou přes Stripe ověřena lokálně i v produkci, refund + dobropis lokálně; webhook ve Stripu musí mít Payload style Snapshot) | `docs/kroky-eshop/krok-16-stripe.md` |
| 17. Event sourcing košíku (`CartEvents` jako zdroj pravdy agregátu `Cart`, tabulky košíku = projekce, přirozený snapshot od objednání/vyprázdnění, souběh přes verzi proudu, převzetí starých košíků snapshotem, historie `/Cart`, `/Admin/CartInsights`) | hotovo lokálně (čeká na nasazení) | `docs/kroky-eshop/krok-17-event-sourcing-kosiku.md` |

## Stav vzhledu (moderní vzhled a mobil, příručka `architektura/vzhled.md`)
Bootstrap 5.3 lokálně (CSS + JS bundle, SRI ověřen) + design systém v `wwwroot/css/site.css` (proměnné `--ms-*` přepisují `--bs-*`,
indigo `#4f46e5`, systémové písmo, dotyková plocha 44 px). **Nová barva/rozměr = proměnná v `:root`**, ikony jen `Ui/Icons.cs`
(inline SVG), karta produktu jen partial `_ProductCard` (`ProductCardModel`). Hlavička: `navbar-expand-lg` + `collapse`, košík mimo
collapse (vidět na mobilu), admin odkazy v nabídce *Správa* (`data-track` zůstaly), účet v nabídce. Výloha: filtry `offcanvas-lg`,
GET formulář beze změny. Rozbalování jen atributy Bootstrapu (`data-bs-toggle`) – žádný inline skript. **`data-track` a ID polí neměnit**
(Journeys). Kontrola snímkem: headless Edge (min. šířka ~500 px; z PowerShellu `Start-Process -Wait`), stránky za přihlášením stáhnout s cookie a doplnit `<base href>`. Stránky účtu = `_AccountLayout` (`Account/_ViewStart.cshtml`). Souhrny do `docs/kroky-vzhled/`.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| A. Kostra a výloha (design systém, hlavička + mobilní menu, Bootstrap JS, úvod pro zákazníka, `_ProductCard`, filtry v panelu, detail s nákupním boxem, ikony) | hotovo (nasazeno 11. 11. 2026, ověřeno na telefonu) | `docs/kroky-vzhled/krok-a-kostra-a-vyloha.md` |
| B. Nákupní cesta (košík s kartami a spodní lištou, pokladna se souhrnem a `.choice-card` – Journeys beze změny, `Replay.cshtml` stejně, objednávky jako karty + časová osa, účet v `_AccountLayout`) | hotovo lokálně (čeká na nasazení) | `docs/kroky-vzhled/krok-b-nakupni-cesta.md` |
| C. Administrace (responzivní tabulky, sjednocené hlavičky stránek) | plán | — |

## Stav škálování (viz `docs/Skalovani-roadmap.md`)
Cíl: víc instancí služeb (a později víc strojů). Nejdřív PostgreSQL (kontejner na Always Free VM, 0 Kč), pak víc instancí.
Rozhodnutí: jeden PostgreSQL se **schématem pro každý modul**, testy proti skutečnému PostgreSQL (Testcontainers),
převod produkčních dat s krátkou odstávkou, expand → contract. Souhrny do `docs/kroky-skalovani/`.
- **Databáze (od kroku 3C jen PostgreSQL):** jedna databáze `ConnectionStrings:minishop`, schéma = `XxxDbContext.Schema`.
  Vše v `Persistence/ModuleDatabase.cs` (`ModuleDatabase.FromConfiguration`, `UseModuleDatabase` – historie migrací ve schématu,
  `UseModuleSchema` v `OnModelCreating`, `UseUtcDateTimes` v `ConfigureConventions` – **nový DbContext je musí volat**;
  časy jsou vždy UTC, per-property převodníky `SpecifyKind` už nejsou potřeba). Unikátní index = SQLSTATE 23505 →
  `ConcurrencyConflictException`. Npgsql `GssEncryptionMode=Disable` (jinak hláška o libgssapi_krb5). `/health`: `database-ordering`/`-inventory`.
  AppHost: persistentní kontejner `postgres` (volume `minishop-postgres-data`). Bez Aspire: kontejner `minishop-postgres`
  (`appsettings.Development.json`, heslo `minishop-vyvoj`). Compose: `postgres:18-alpine` (volume `postgres-data` na
  `/var/lib/postgresql`, `127.0.0.1:5432`, `mem_limit 1g`), `POSTGRES_PASSWORD` povinný (i lokální `deploy/.env`);
  `deploy.sh` mění heslo v běžící DB (`ALTER USER` přes socket).
- **Testy:** databáze jen přes `TestDatabases` (čerstvá kopie šablony s migracemi pro každý test, `PostgresTestServer` =
  Testcontainers, **lokálně potřeba Docker**; factory `_databases.Configure(builder)`). **Žádné ruční SQL v testech**
  (schéma, uvozovky) – `ExecuteUpdateAsync`. CI jen job „Build a testy“ (nad PostgreSQL).
- **Historie přechodu:** krok 1–2 oba provideři (migrace SQLite + PostgreSQL), 3A/3B převod `migrate-to-postgres` (odstranění
  v 3C, v historii gitu) 4. 11. 2026, 4 obnova (`restore.sh` s `pg_restore`), 3C odebrání SQLite (migrace PostgreSQL přesunuty
  zpět do `MiniShop.Infrastructure`, jména migrací zachována).
- **Více instancí (krok 5):** Web v `WEB_REPLICAS` kopiích (produkce 2, `production.env`), Caddy `web-upstream` (`dynamic a web 8080`,
  round robin, nedostupnou kopii obejde). Vedoucí kopie = `LeaderElection` (Infrastructure/Hosting; advisory lock
  `minishop.web` / `minishop.inventory-api` na vlastním spojení bez poolu, `AddLeaderElection` před workery).
  **Nový worker s časovačem = `AddLeaderOnlyHostedService<T>()`, ne `AddHostedService`** (hlídá `WorkerRegistrationTests`);
  ve všech kopiích jen `RabbitMqConsumersWorker`. Worker musí snést zastavení a nové spuštění (nová instance na každé vedení)
  a krátký překryv se starým vedoucím (idempotence). Sdílené `web-data` = společné klíče Data Protection. V paměti kopie
  (rate limiting) = limit na kopii. Testy: workery odebírá `HostedServices.RemoveMiniShopWorkers` (i registrované továrnou).
  Vedoucí zjistit `docker compose logs web | grep Vedoucí`.
- **Postupné nasazení (krok 5B):** `deploy.sh` → infrastruktura `up --no-deps` (jinak `depends_on` caddy → web vymění Web naráz),
  změněný Caddyfile `caddy reload` hned potom (ne až po výměně kopií – první nasazení tak dostalo 1× 502),
  pak `deploy/rolling-update.sh inventory-api 1` a `… web $WEB_REPLICAS` (`--scale` staré+nové `--no-recreate`, `--wait`,
  nezdravé nové `docker rm -f` = rollback bez výpadku, 15 s pauza, staré `docker stop --time 30` + `rm`). Caddy `web-upstream`:
  `fail_duration 5s` (nová kopie je v DNS dřív, než otevře port), `dial_timeout 1s` (odstraněná kopie nemá IP), `lb_try_duration 10s`.
  Jména kontejnerů se po nasazení mění (`minishop-web-<N>`). Lokální zkouška: `WEB_PORT` rozsah aspoň 2× kopií (`8080-8083`).

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. PostgreSQL vedle SQLite (`Database:Provider`, `ModuleDatabase`, schéma na modul, migrace v `MiniShop.Infrastructure.PostgreSql`, Aspire + Compose profil `postgres`; produkce dál SQLite) | hotovo (lokálně ověřeno – obě služby nad PostgreSQL, 8 schémat, katalog přes RabbitMQ; AppHost s PostgreSQL ověřen uživatelem 4. 11. 2026) | `docs/kroky-skalovani/krok-01-postgresql.md` |
| 2. Testy proti PostgreSQL (`MINISHOP_TEST_DATABASE=Postgres`, `TestDatabases` + `PostgresTestServer` – Testcontainers, šablona + kopie DB na test, CI job „Testy PostgreSQL“; oprava: kategorie „Nezařazeno“ v migraci PostgreSQL) | hotovo (lokálně obě varianty zelené) | `docs/kroky-skalovani/krok-02-testy-postgresql.md` |
| 3A. Příprava převodu (`migrate-to-postgres` + `SqliteToPostgresCopier`, značka `__MiniShopDataMigration` / `--replace`, PostgreSQL 18 v produkčním Compose, secret `POSTGRES_PASSWORD`, `pg_dump` v `backup.sh`, `DatabaseHealthCheck`) | hotovo (generálka v Compose; nasazeno 4. 11. 2026 – prázdný PostgreSQL 18 na VM healthy, noční záloha obsahuje `minishop.pgdump`) | `docs/kroky-skalovani/krok-03a-priprava-prevodu.md` |
| 3B. Přepnutí produkce (odstávka, `migrate-to-postgres` na VM, `DATABASE_PROVIDER=Postgres` v `production.env`) | hotovo (4. 11. 2026, odstávka ~25 min, 1 702 řádků ve 41 tabulkách, počty souhlasí; produkce na PostgreSQL ověřena v prohlížeči i novou objednávkou; SQLite soubory zůstávají jako cesta zpět do 3C) | `docs/kroky-skalovani/krok-03b-prepnuti-produkce.md` |
| 3C. Odebrání SQLite – contract (provider, migrace SQLite, baseline, `migrate-to-postgres`, SQLite v zálohách; migrace PostgreSQL zpět do `MiniShop.Infrastructure`, testy jen PostgreSQL, CI jeden job) | hotovo (nasazeno 5. 11. 2026; oprava souběhu „poslední admin“ a dvojkliku v pokladně; záloha bez SQLite, staré `.db` na VM smazány) | `docs/kroky-skalovani/krok-03c-odebrani-sqlite.md` |
| 4. Obnova ze zálohy (`restore.sh` s `pg_restore`, přejmenování původní DB, vrácení při chybě, odmítnutí staré zálohy; `pg_dump` už od 3A) | hotovo (generálka v Compose; cvičná obnova v produkci 5. 11. 2026: záloha 165410Z, obnova 18 s, počty shodné, záložní DB smazána) | `docs/kroky-skalovani/krok-04-obnova.md` |
| 5. Více instancí (`LeaderElection` přes advisory lock, `LeaderOnlyHostedService` pro workery s časovačem, `web` ve dvou kopiích za Caddy – `WEB_REPLICAS`, `dynamic a`, log pro každou kopii, TCP keepalive PostgreSQL) | hotovo (nasazeno 5. 11. 2026; zkouška výpadku v produkci: `docker stop` vedoucí kopie → 60/60 požadavků 200, převzetí za 3 s, vrácená kopie zůstala záložní, objednávka přes novou vedoucí potvrzena) | `docs/kroky-skalovani/krok-05-vice-instanci.md` |
| 5B. Postupné nasazení (`deploy/rolling-update.sh` – nové kopie vedle starých, pak staré pryč, nezdravé nové zahodit; infrastruktura zvlášť; Caddy `fail_duration 5s`, `dial_timeout 1s`; migrace kompatibilní se starší verzí) | hotovo (nasazeno 5. 11. 2026; první nasazení 1× 502 – Caddyfile se načítal až po výměně, opraveno; druhé nasazení 968× 200 bez chyby; generálka 522× 200, nezdravá verze zahozena 153× 200) | `docs/kroky-skalovani/krok-05b-postupne-nasazeni.md` |
| 6. Druhá VM (volitelně) | plán | — |

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
