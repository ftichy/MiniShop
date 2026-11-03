# E-shop krok 10: Opuštěný košík

Zákazník, který si zapnul připomínky, dostane jeden e-mail, když zboží zůstane v košíku 24 hodin bez změny.
Technicky je to **reakce na „ticho“**: nereagujeme na událost, ale na to, že žádná nepřišla. Takovou situaci musí
někdo hledat – worker, stejně jako timeout ságy. Ordering oznámí fakt `CartAbandonedV1`, Notifications rozhodne,
jestli smí psát. Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 9](krok-09-zruseni.md).

## Jak to funguje

Přirovnání: prodavač si všimne vozíku, u kterého už dlouho nikdo nestojí, a zapíše si to. Jestli zákazníkovi smí
poslat SMS, zjistí z věrnostní kartičky **až v tu chvíli** – souhlas mohl mezitím odvolat.

```
 CartReminderWorker (15 min) ─► CartReminderService: košík s obsahem, 24 h beze změny, bez připomínky?
                                 └─► COMMIT: Cart.ReminderSentAtUtc + Outbox[CartAbandonedV1 (ID účtu + položky)]
 OutboxWorker ─► RabbitMQ ─► Notifications: CartReminderHandler ─► ICustomerContacts (účty, TEĎ)
                                 souhlas + potvrzený e-mail ─► e-mail s odkazem na košík a na vypnutí
                                 jinak ─► nic (inbox zprávu odškrtne)
 košík se vyprázdní (objednávka) ─► ReminderSentAtUtc = null ─► příští nákup smí dostat připomínku
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Reakce na ticho (timeout)** | událost, která vzniká tím, že něco nepřišlo – musí ji hledat plánovač | `CartReminderWorker`, `Cart.IsAbandoned` |
| **Fakt bez osobních údajů** | zpráva nese jen ID, citlivé údaje si odběratel zjistí sám | `CartAbandonedV1` (ID účtu, položky) |
| **Kontrola v okamžiku použití** | souhlas se ověřuje až při odeslání, ne při vzniku zprávy | `ICustomerContacts` → `IdentityCustomerContacts` |
| **Nejvýš jednou** | značka a zpráva v jedné transakci, značka se maže až s koncem nákupu | `Cart.ReminderSentAtUtc` + outbox |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Carts/CartAbandonedV1.cs` | integrační událost (+ `CartItemV1`) |
| `src/MiniShop.Application/Ordering/Carts/CartReminderService.cs` | hledání opuštěných košíků, značka + zpráva v jedné transakci, `CartReminderSettings` |
| `src/MiniShop.Application/Notifications/CartReminderHandler.cs`, `CartEmailTemplates.cs`, `ICustomerContacts.cs` | připomínka, šablona, kontakt a souhlas |
| `src/MiniShop.Web/BackgroundServices/CartReminderWorker.cs` | worker (vlastní scope a span pro každý košík) |
| `src/MiniShop.Web/Accounts/IdentityCustomerContacts.cs` | `ICustomerContacts` nad účty (composition root) |
| `src/MiniShop.Web/Pages/Account/EmailPreferences.cshtml(.cs)` | souhlas s připomínkami (výchozí vypnuto) |
| migrace `CartReminders` (ordering.db), `CartRemindersConsent` (users.db) | nové sloupce, nic se nemaže |
| `docs/prirucka/toky/opusteny-kosik.md`, tento souhrn | dokumentace |
| `tests/…/Web/EmailPreferencesTests.cs`, `TestDoubles/InMemoryCustomerContacts.cs` | stránka souhlasu, kontakty v testech |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Carts/Cart.cs` | `ReminderSentAtUtc`, `IsAbandoned`, `MarkReminderSent`; vyprázdnění značku maže |
| `Application/Ordering/Carts/ICartRepository.cs`, `Infrastructure/Ordering/CartRepository.cs`, `CartConfiguration.cs` | `GetByIdAsync`, `GetAbandonedIdsAsync`, sloupec |
| `Application/Notifications/NotificationSettings.cs` | odkazy na košík a na nastavení e-mailů |
| `Application/Notifications/OrderEmailTemplates.cs` | text „platbu vracíme“ pokrývá i zrušení zaplacené objednávky (krok 9) |
| `Infrastructure/Accounts/ApplicationUser.cs` | `CartRemindersConsent` |
| `Web/Extensions/…`, `Program.cs`, `appsettings.json` | registrace, worker, sekce `Carts` (`AbandonedAfterHours`, `ReminderCheckMinutes`) |
| `Web/Pages/Account/ChangePassword.cshtml`, `Pages/Privacy.cshtml` | odkaz na nastavení e-mailů, nový řádek „co ukládáme“ |
| příručka (`moduly/ordering`, `notifications`, `accounts`, `toky/index`, `historie`) | dokumentace |
| `tests/…/CartTests.cs`, `OrderServiceTests.cs`, `AuthorizationMatrixTests.cs` | pravidla košíku, celé cesty, matice oprávnění |

## Rozhodnutí v tomto kroku

- **Souhlas se čte až při odeslání** (`ICustomerContacts`), ne kopií ve zprávě jako u objednávek. Marketing smí
  odejít jen s platným souhlasem; zpráva může ve frontě čekat i hodiny.
- **Výchozí bez souhlasu** a jen na potvrzený e-mail – žádný nevyžádaný e-mail.
- **Bez doménové události** – Ordering na opuštění nereaguje, `CartAbandonedV1` jde rovnou přes publisher
  (jako `CustomerAnonymizedV1`).
- **Fakt se oznámí vždy**, i bez souhlasu – rozhoduje odběratel. Jiný odběratel (statistiky opuštěných košíků)
  by souhlas nepotřeboval.
- **Jedna připomínka na nákup**, ne na změnu – kdo po připomínce přidá zboží a odejde, druhou nedostane.
- **Připomínka nemění `CheckoutId`** – otevřená pokladna dál platí; souběh se zákazníkem zachytí právě tenhle token.
- **Ceny v e-mailu aktuální** z kopie katalogu, s poznámkou, že zboží není rezervované.

## Známá omezení

- Košíky, které v produkci leží déle než 24 h, dostanou značku hned po nasazení – zákazník bez souhlasu (všichni
  zatím) tak za ně připomínku nedostane ani po zapnutí souhlasu. Další nákup už ano.
- Odkaz „vypnout“ vyžaduje přihlášení (žádný odhlašovací token v odkazu).
- Worker počítá s jednou instancí Webu (jako ostatní workery).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

Lokálně termín zkrať: v `appsettings.Development.json` Webu `"Carts": { "AbandonedAfterHours": 0.02, "ReminderCheckMinutes": 0.5 }`
(~1 minuta ticha, kontrola každých 30 s).

1. Přihlas se jako zákazník → jméno → **Nastavení e-mailů** → zaškrtni připomínky → Uložit.
2. Dej něco do košíku a nech být → do ~2 minut v Mailpitu „Ve vašem košíku zůstalo zboží“ s odkazem na košík.
3. Počkej další minutu → žádný druhý e-mail.
4. Objednej → nový košík → po minutě znovu jedna připomínka.
5. Vypni souhlas → nový nákup → v logu `CartAbandonedV1`, ale žádný e-mail.

### Produkce

Nic nového k nastavení – migrace proběhnou samy při startu (`ordering.db`, `users.db`). Termín 24 h.

## Vyzkoušejte sami

- Co by se stalo, kdyby `CartAbandonedV1` nesla e-mail a souhlas (jako `OrderPlacedV2`) a zákazník souhlas vypnul,
  zatímco zpráva čekala ve frontě?
- Proč `MarkReminderSent` nemění `CheckoutId`? Zkus to změnit a pusť testy pokladny.
- Přidej druhého odběratele `CartAbandonedV1` (např. počítadlo opuštěných košíků pro admina) – co musíš změnit
  v Orderingu? (Nic.)
- Jak bys udělal odhlášení jedním klikem bez přihlášení? (Nápověda: podepsaný token v odkazu, jako webhook.)
