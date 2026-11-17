# E-shop krok 24: Oznámení k práci pro zaměstnance

Navazuje na oznámení push (krok 23) a role (účty krok 12): zaměstnanec dostane na telefon oznámení, když na něj čeká
práce – skladník „Nová objednávka č. 12 k zabalení“ a „Došlo zboží“, podpora „Žádost o vratku“. Přirovnání: zvonek
na skladě – nemusí pořád koukat do Expedice, telefon zazvoní sám.

## Jak to funguje

1. **Volba po typech (opt-in):** stránka *E-maily a oznámení* je od tohoto kroku pro každého přihlášeného (dřív jen
   zákazník; připomínka košíku zůstává jen zákazníkovi). Zaměstnanec v sekci *Oznámení k práci* zaškrtne typy pro svou
   roli – výchozí nic. Uloží se `StaffAlertSubscription` (modul Notifications, tabulka `StaffAlertSubscriptions`, typ
   jako text). Zapnout jde jen typ, ke kterému má roli (podvržený formulář nic nezmění).
2. **Odeslání:** `StaffPushHandler` (vlastní inbox) poslouchá `OrderConfirmedV1` (NewOrder), `ReturnRequestedV1/V2`
   (ReturnRequested) a `StockAvailabilityChangedV1` s Vyprodáno (OutOfStock). Vezme účty se zaškrtnutým typem,
   `IStaffDirectory` z nich nechá jen ty, které roli TEĎ mají a nejsou zablokované, a `PushNotifier` pošle oznámení na
   jejich zařízení.
3. **Mosty přes Web** (Notifications role, účty ani katalog nezná): `IdentityStaffDirectory` nad Identity s přiřazením
   typ → role v `StaffAlertRoles` (NewOrder → Sklad, ReturnRequested → Podpora, OutOfStock → Sklad + Sortiment);
   `CatalogProductNames` nad kopií katalogu (název produktu – zpráva nese jen ID).
4. **Bez osobních údajů zákazníků** – jen číslo objednávky nebo název produktu; klepnutí otevře stránku správy
   (Expedice, Vratky, Produkty).
5. **Smazání účtu** zapomene i volby (`CustomerAnonymizedV1`).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Notifications/StaffAlertSubscription.cs` | volba typu + `StaffAlertKind` |
| `src/MiniShop.Application/Notifications/StaffAlerts.cs` | `IStaffAlertSubscriptionRepository`, `IStaffDirectory`, `IProductNames`, `StaffAlertService` |
| `src/MiniShop.Application/Notifications/StaffPushHandler.cs` | odběratel událostí, oznámení zaměstnancům |
| `src/MiniShop.Infrastructure/Notifications/StaffAlertSubscriptionRepository.cs` + konfigurace, migrace `StaffAlertSubscriptions` | uložení (jen přidává tabulku) |
| `src/MiniShop.Web/Notifications/StaffAlerts.cs` | `StaffAlertRoles`, `IdentityStaffDirectory`, `CatalogProductNames` |
| `tests/MiniShop.Tests/Application/OrderServiceTests.StaffPush.cs`, `TestDoubles/InMemoryStaffDirectory.cs` | testy |

## Změněné soubory

- `PushTemplates` (texty pro zaměstnance), `OrderNotificationsHandler` (smazání účtu), `NotificationsDbContext`,
  `NotificationsInfrastructure`, `ApplicationServiceCollectionExtensions` (`AddStaffPushHandlers`, mosty).
- Stránky: `Account/EmailPreferences` (pro každého přihlášeného, sekce *Oznámení k práci*, košík jen zákazník),
  `_LoginPartial` a `_AccountFooter` (odkaz *E-maily a oznámení* pro všechny), `Privacy`.
- Testy: `AuthorizationMatrixTests` (zaměstnanec smí stránku i push, připomínku košíku ne), `PushNotificationsTests`
  (stránka skladníka, uložení jen typů s rolí, `IdentityStaffDirectory` včetně zablokovaného účtu).
- Příručka: modul Notifications, observabilita (`minishop.push.staff_recipients`), historie; CLAUDE.md.

## Rozhodnutí

- **Opt-in po typech** – nikomu nezačne pípat telefon bez souhlasu; každý si vybere, co ho zajímá.
- **Role se ověřuje při odeslání, ne při uložení** – odebraná role nebo zablokovaný účet = hned žádná oznámení.
- **Vratky jen podpoře** – rozhoduje o nich podpora; sklad zboží převezme, až fyzicky dorazí (Vratky vidí v Expedici).
- **Přiřazení typ → role ve Webu** – aplikační vrstva role nezná (jsou v Infrastructure/Accounts); jedno místo
  `StaffAlertRoles`.
- **Žádné nové zprávy** – stejné události jako e-maily a oznámení zákazníkům; Ordering ani Inventory se nezměnily.

## Známá omezení

- Oznámení jde jen na zařízení, kde má zaměstnanec zapnutý push (sekce výše na stejné stránce).
- Zaměstnanec, který je i zákazník a objedná si sám, dostane oznámení „k zabalení“ i o vlastní objednávce – vyřídit ji
  stejně musí kolega (střet zájmů, účty krok 12C).
- Velký obchod s mnoha skladníky by dostával hodně oznámení – tady žádné slučování ani tiché hodiny.

## Jak ověřit

- `dotnet test` – `OrderServiceTests.StaffPush_*`, `StaffAlerts_*`, `PushNotificationsTests.Zamestnanec_*`, `AuthorizationMatrixTests`.
- Produkce: účet se Skladem → *E-maily a oznámení* → zapnout oznámení na telefonu → zaškrtnout *Nová objednávka k zabalení*
  → jiným účtem objednat (dobírka) → na telefonu „Nová objednávka č. … k zabalení“ → klepnutí otevře Expedici.

## Vyzkoušejte sami

1. Odeberte skladníkovi roli Sklad (správa uživatelů) a objednejte – oznámení nepřijde, i když má typ zaškrtnutý.
2. Přidejte typ „Platba se nepovedla“ pro účetní: hodnota v `StaffAlertKind`, šablona, `HandleAsync(OrderRejectedV1)`
   ve `StaffPushHandler`, řádek v `StaffAlertRoles` – stránka nastavení ho ukáže sama.
