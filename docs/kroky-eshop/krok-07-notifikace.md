# E-shop krok 7: Notifikace jako samostatný odběratel

Do tohoto kroku psal zákazníkovi přímo Ordering: doménová událost → `Send…Handler` → `EmailOrderNotifier`.
Teď Ordering jen **oznamuje fakta** (integrační události) a nový modul **Notifications** si sám rozhodne, komu co
napsat. Ordering neobsahuje žádný kód o e-mailech. Přibyl e-mail „Objednávku jsme přijali“ a každý e-mail má
odkaz na detail objednávky a HTML verzi.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 6](krok-06-expedice.md) (odeslání s číslem zásilky).

## Jak to funguje

Přirovnání: obchod vyvěsí na nástěnku „objednávka č. 5 odeslána“. Kdo chce, ať si to přečte – sklad, e-mailová
kancelář, jednou třeba SMS služba. Obchod se kvůli novému čtenáři nemění. E-mailová kancelář si při první zprávě
o objednávce opíše adresu zákazníka; když zákazník zruší účet, její kopii skartuje.

```
 Ordering (Web)                                        Notifications (Web, notifications.db)
   OrderPlaced ──► překladač ──► OrderPlacedV1 + OrderPlacedV2 ──► uložit příjemce (jméno, e-mail, souhrn) + „přijali jsme“
   OrderConfirmed ─────────────► OrderConfirmedV1  ──────────────► najít příjemce → „potvrzená“
   OrderRejected ──────────────► OrderRejectedV1 (důvod) ────────► „nelze vyřídit“
   OrderShipped ───────────────► OrderShippedV1 ─────────────────► „na cestě, číslo zásilky …“ (+ sklad vydá rezervaci)
   OrderCompleted / Cancelled ─► OrderCompletedV1 / CancelledV1 ─► „doručená“ / „zrušená“
   smazání účtu ───────────────► CustomerAnonymizedV1 ───────────► smazat řádky zákazníka
                      (outbox ordering.db → RabbitMQ, fronta minishop.notifications → inbox notifications.db)
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Pub/sub** | vydavatel oznámí fakt, odběratelé si ho zpracují sami | Ordering → integrační události → Notifications (i Inventory) |
| **Event-carried state transfer** | zpráva nese data, odběratel si drží vlastní kopii | `OrderPlacedV2` → `OrderRecipient` (jako kopie katalogu v Orderingu) |
| **Expand → contract** | nová verze kontraktu vedle staré, stará zmizí, až ji nikdo nečte | `OrderPlacedV1` + `OrderPlacedV2` |
| **Zprávy mimo pořadí** | potvrzení může dorazit dřív než vznik | handler vyhodí výjimku → zpráva znovu za 2 s (nejvýš 5 minut) |
| **Právo být zapomenut** | kdo si uložil osobní údaje, musí je po smazání účtu smazat | `CustomerAnonymizedV1` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Orders/OrderPlacedV2.cs` | celý snímek objednávky (jméno, položky s názvy, doprava, adresa) |
| `src/MiniShop.Contracts/Orders/OrderConfirmedV1.cs`, `OrderRejectedV1.cs` | dřív jen interní doménové události |
| `src/MiniShop.Contracts/Customers/CustomerAnonymizedV1.cs` | zákazník smazal účet – zapomenout ho |
| `src/MiniShop.Domain/Notifications/OrderRecipient.cs` | příjemce + položky do souhrnu |
| `src/MiniShop.Application/Notifications/*` | `OrderNotificationsHandler`, `OrderEmailTemplates`, `NotificationSettings`, rozhraní |
| `src/MiniShop.Infrastructure/Notifications/*` | `NotificationsDbContext` (inbox), repozitář, inicializace, migrace `InitialNotifications` |
| `docs/prirucka/architektura/moduly/notifications.md`, tento souhrn | dokumentace |
| `tests/MiniShop.Tests/Application/OrderEmailTemplatesTests.cs` | texty, HTML kódování, odkaz, starší objednávka |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Application/Ordering/IntegrationEvents/PublishOrderIntegrationEventsHandler.cs` | navíc `OrderPlacedV2`, `OrderConfirmedV1`, `OrderRejectedV1` |
| `Application/Ordering/OrderService.cs` | `AnonymizeCustomerAsync` publikuje `CustomerAnonymizedV1` (ve stejné transakci) |
| `Application/Ordering/IOrderNotifier.cs`, `EventHandlers/*`, `Infrastructure/Ordering/EmailOrderNotifier.cs` | **smazáno** |
| `Application/IntegrationModules.cs` | modul `notifications` (fronta `minishop.notifications`) |
| `Web/Program.cs`, `Extensions/ApplicationServiceCollectionExtensions.cs` | `notifications.db`, `AddNotificationsModule` |
| `Web/appsettings*.json`, `Web/Dockerfile`, `deploy/compose.yaml`, `deploy/production.env` | connection string, `Notifications:PublicBaseUrl` (`PUBLIC_BASE_URL`) |
| `deploy/backup.py` | `notifications.db` do záloh |
| `Web/Pages/Privacy.cshtml` | nový řádek: kopie pro oznámení |
| testy | `OrderServiceTests` s modulem Notifications (celá cesta, duplicita, mimo pořadí, zapomenutí, výpadek SMTP), `ModuleBoundariesTests` (Notifications, „Ordering neposílá e-maily“), kontrakty |

## Rozhodnutí v tomto kroku

- **Notifications si příjemce ukládá** (z `OrderPlacedV2`) místo toho, aby každá událost nesla jméno, adresu a položky.
  Osobní údaje jsou tak v jedné zprávě, ne v deseti – a modul musí poslouchat `CustomerAnonymizedV1`.
- **`OrderPlacedV2` vedle V1** (expand → contract) – V1 se zatím nemaže, mohou ho číst jiní odběratelé (audit).
- **`OrderPackingStartedV1` nevzniklo** – e-mail „balíme“ by byl spam a kontrakt bez odběratele je zbytečný.
- **Zpráva mimo pořadí = výjimka a opakování**, nejvýš 5 minut (`MaxWaitForPlacedOrder`) – pak jde o objednávku
  z doby před modulem a čekání by nikdy neskončilo.
- **Starší objednávky** (bez příjemce): odeslání, doručení a zrušení nesou e-mail ve zprávě → kratší e-mail bez souhrnu.
- **Odkaz v e-mailu z konfigurace** (`Notifications:PublicBaseUrl`) – handler nemá HTTP požadavek; bez nastavení
  e-mail bez odkazu (raději žádný než špatný).
- **Šablony v jedné třídě** (`OrderEmailTemplates`) místo rozhraní na každou událost – šest krátkých metod, nic
  složitého; rozdělit jde, až přibude jiný kanál.
- **Notifications nemá outbox** (dědí ho z `ModuleDbContext`, ale nepoužívá) – nic neposílá.

## Známá omezení

- At-least-once: pád mezi odesláním e-mailu a zápisem do inboxu = e-mail podruhé (jako dřív).
- Výpadek SMTP: RabbitMQ zprávu vrací každé 2 s bez limitu (consumery nemají dead-letter frontu).
- Objednávky starší než krok 7 nedostanou e-mail o potvrzení/zamítnutí (už proběhlo) a další e-maily jsou bez souhrnu.
- Zálohy `notifications.db` obsahují osobní údaje ještě 7 dní po smazání účtu (lifecycle bucketu).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu `Applying migration '…_InitialNotifications'`; v RabbitMQ UI fronta `minishop.notifications`.
2. Objednej z košíku → v Mailpitu hned „Objednávku č. … jsme přijali“ s položkami a odkazem, za pár sekund „potvrzená“.
3. Expedice → Odeslat → „je na cestě“ s číslem zásilky; Doručeno → „doručená“. HTML verze v Mailpitu má odkaz.
4. Zastav Mailpit, zruš objednávku → e-mail nepřijde; spusť Mailpit → do pár sekund přijde právě jednou.
5. Smaž účet (bez rozpracovaných objednávek) → v `notifications.db` nezůstane žádný řádek zákazníka.

### Produkce

1. Před sloučením ruční záloha (`sudo systemctl start minishop-backup`).
2. Sloučit PR → CD nasadí (nová `notifications.db`, `PUBLIC_BASE_URL` z `production.env`).
3. Objednat → e-mail „přijali jsme“ s odkazem na https://minishop-tichy.cz/Orders/….

## Vyzkoušejte sami

- Přidej odběratele `OrderShippedV1` „SMS“ (handler, který jen zapíše do logu) – kolik souborů v Orderingu změníš?
- Co by se stalo, kdyby `OrderNotificationsHandler` pro `OrderConfirmedV1` bez příjemce nevyhazoval výjimku, ale
  e-mail tiše přeskočil?
- Proč `CustomerAnonymizedV1` posílá Ordering, a ne Accounts? (Nápověda: kdo ví, že anonymizace proběhla.)
- Najdi test, který hlídá, že Ordering nepoužívá `IEmailSender`. Zkus ho rozbít.
