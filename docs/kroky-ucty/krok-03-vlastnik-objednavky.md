# Účty krok 3: Vlastník objednávky

Po kroku 2 věděl MiniShop, **kdo** smí objednávat, ale ne **čí** je která objednávka – každý zákazník viděl
a mohl zrušit všechny. Teď objednávka patří zákazníkovi: vidí a ruší jen své, cizí se tváří jako neexistující (404).
Jméno a e-mail se už nepíšou do formuláře – bere je server z přihlášeného účtu. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: šatna. Za kabát dostaneš lístek s číslem (`CustomerId`). Šatnářka vydá jen kabát k tvému lístku;
na „dejte mi kabát 43“ řekne „takový tu nemáme“ – ne „ten je cizí“ (to by prozradilo, že existuje).

```
 prohlížeč ── cookie (ID, jméno, e-mail, role) ──► Web
     Web: User.ToOrderAccess()   → admin: OrderAccess.AllOrders | zákazník: OrderAccess.Customer(ID)
          User.ToOrderCustomer() → OrderCustomer(ID, jméno, e-mail)      (OrderAccessExtensions)
     OrderService (Application – o cookie ani účtech neví, dostává to parametrem):
          GetAllAsync(access)        → SQL: WHERE CustomerId = @id   (admin bez filtru)
          GetByIdAsync / CancelAsync → cizí objednávka = NotFoundException (stejná hláška jako neexistující) → 404
          PlaceOrderAsync(request, customer) → new Order(customer.CustomerId, jméno, e-mail, …) – kopie z doby objednání
```

Ordering zná jen ID zákazníka jako neprůhledný řetězec – modul Accounts nereferencuje (hlídá `ModuleBoundariesTests`).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Application/Ordering/OrderAccess.cs` | `OrderAccess` (kdo se ptá: zákazník / všechny) a `OrderCustomer` (kdo objednává) |
| `src/MiniShop.Web/Accounts/OrderAccessExtensions.cs` | Převod přihlášeného uživatele (claims) na `OrderAccess` / `OrderCustomer` |
| `tests/MiniShop.Tests/Web/OrderOwnershipTests.cs` | HTTP: seznam jen vlastních, cizí detail/zrušení = 404 (API i stránka), admin vidí vše |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Domain/Orders/Order.cs` | `CustomerId` (povinné), `BelongsTo` |
| `src/MiniShop.Application/Ordering/IOrderService.cs`, `OrderService.cs` | Parametry `OrderAccess` / `OrderCustomer`; cizí objednávka → `NotFoundException` |
| `src/MiniShop.Application/Ordering/IOrderRepository.cs`, `Infrastructure/Ordering/OrderRepository.cs` | `GetAllAsync(customerId)` – filtr v SQL |
| `src/MiniShop.Application/Ordering/OrderContracts.cs` | `CreateOrderRequest` bez `CustomerName` / `CustomerEmail` |
| `src/MiniShop.Infrastructure/Ordering/Configurations/OrderConfiguration.cs` | Sloupec `CustomerId` + index |
| `src/MiniShop.Web/Pages/Orders/Index.cshtml(.cs)` | Formulář bez jména a e-mailu („Objednává: …“), „Moje / Všechny objednávky“ |
| `src/MiniShop.Web/Pages/Orders/Replay.cshtml` | Kopie formuláře bez jména a e-mailu |
| `src/MiniShop.Web/Controllers/OrdersController.cs`, `MiniShop.Web.http` | `OrderAccess` / `OrderCustomer` z přihlášeného uživatele; ukázky bez jména a e-mailu |
| `tests/…` | `OrderServiceTests` (vlastník, cizí = 404), konstruktor `Order` v doménových testech, `MiniShopWebFactory.CreateOrderForAsync` |
| `docs/prirucka/…` | Ordering, Accounts, tok vytvoření objednávky, slovníček (IDOR, resource-based authorization), historie |

## Rozhodnutí v tomto kroku

- **Kdo se ptá, dostává `OrderService` parametrem** (`OrderAccess`), ne přes „ICurrentUser“ uvnitř aplikace.
  Je to vidět v každém volání, testuje se bez HTTP a aplikační vrstva o přihlášení nic neví.
- **Filtr seznamu v SQL dotazu**, ne v šabloně – cizí objednávky se vůbec nenačtou.
- **Cizí objednávka = 404 se stejnou hláškou jako neexistující** – zákazník nepozná, že číslo existuje (IDOR).
- **Jméno a e-mail z účtu, ne z formuláře ani z JSON** (rozhodnutí 1A) – identita se bere z přihlášení, klient ji
  nemůže podvrhnout. Na objednávku se ukládají jako kopie z doby objednání.
- **`CustomerId` je povinné, stará `ordering.db` se maže** (rozhodnutí 2A) – žádné objednávky „bez zákazníka“.
- **Admin = všechny objednávky** odvozuje Web z role (`IsInRole(Admin)`), stejně jako policy `RequireAdmin`.
- Kontrakty (`OrderPlacedV1` …) beze změny – sklad zákazníka nepotřebuje.

## Známá omezení

- Zrušit jde zatím jen potvrzenou objednávku (Pending přijde v kroku 4).
- Admin vidí všechny objednávky v jednom seznamu, bez filtru podle zákazníka (krok 6).
- Starší záznamy průběhu (journeys) obsahují psaní jména a e-mailu – přehrání tyto kroky přeskočí (pole už neexistují).
- E-mail na objednávce je e-mail účtu; změna e-mailu v účtu (zatím nejde) by staré objednávky nezměnila – záměrně.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
```

**Lokálně – změna schématu:** smažte `src/MiniShop.Web/ordering.db` (i `-wal`, `-shm`), pak
`dotnet run --project src/MiniShop.AppHost`. Katalog se po startu načte ze skladu sám.

1. Zaregistrujte dva zákazníky (např. ve dvou oknech prohlížeče, jedno anonymní). Každý vytvoří objednávku.
2. Každý vidí v „Moje objednávky“ jen svou. Formulář ukazuje „Objednává: …“ místo polí jméno/e-mail.
3. Zákazník A otevře `https://localhost:7180/api/orders/<číslo objednávky B>` → **404**.
4. Admin vidí „Všechny objednávky“ s jmény obou zákazníků.

### Produkce – smazání `ordering.db` (jednou, hned po nasazení)

Do smazání bude stránka Objednávky v produkci hlásit chybu (stará databáze nemá sloupec `CustomerId`).
Před nasazením zkontrolujte, že žádná objednávka není ve stavu „Ověřujeme sklad…“ (odpověď skladu
na objednávku, která po smazání neexistuje, by zůstala viset ve frontě).

Na VM (přes Tailscale, `ssh minishop-vm`):

```text
sudo systemctl start minishop-backup                       # ruční záloha (objednávky zůstanou v záloze)
cd /opt/minishop
sudo -u deploy docker compose stop web
sudo rm /var/lib/docker/volumes/minishop_web-data/_data/ordering.db*
sudo -u deploy docker compose start web
```

Pak na webu: stránka Objednávky funguje, katalog se během pár sekund doplní, nová objednávka zákazníka projde
až do „Potvrzená“.

## Vyzkoušejte sami

- Pošlete `POST /api/orders` s `"customerEmail": "cizi@example.com"` v JSON. Jaký e-mail má vzniklá objednávka?
- V `OrderService.GetOrderAsync` dočasně vraťte objednávku bez kontroly `access.CanSee` – který test spadne?
- Podívejte se v Aspire dashboardu na SQL span seznamu objednávek – je v něm `WHERE … CustomerId`?
