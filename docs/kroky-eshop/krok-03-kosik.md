# E-shop krok 3: Košík

Zákazník si ve výloze přidává produkty do košíku, na stránce `/Cart` mění množství a odebírá řádky. Košík je
uložený v databázi, takže přežije odhlášení. V hlavičce je vidět počet kusů. Objednat celý košík půjde až
v kroku 4 (pokladna).
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 2B](krok-02b-obrazky.md).

## Jak to funguje

Přirovnání: nákupní vozík v obchodě. Do vozíku se vejde jen tolik, kolik se vejde (pravidla hlídá vozík sám),
zboží v něm není zaplacené ani blokované. Když ho necháte stát u regálu, ostatní si klidně vezmou poslední kus.
Na ceny ve vozíku se nikdo nespoléhá, platí cena u pokladny.

```
 výloha /Shop ── [Do košíku] POST /Cart?handler=Add (productId, returnUrl) ──► Cart/Index (RequireCustomer)
   CartService.AddItemAsync(ID zákazníka, produkt, 1):
     1. produkt z kopie katalogu (CatalogProduct)               – neexistuje → DomainException → hláška
     2. košík zákazníka z ordering.db, nebo nový                 – unikátní index CustomerId
     3. cart.AddItem(produkt, 1, čas)                            – INVARIANTY: max 20 řádků, 1–100 ks,
                                                                   produkt jen jednou, ne vyprodaný/stažený
     4. SaveChanges (Carts + CartLines)                          – žádná zpráva, nic do outboxu
   ◄── 302 zpět na /Shop?… (jen lokální adresa) + hláška přes TempData

 /Cart ── CartService.GetAsync: řádky (ID produktu, množství, PriceWhenAdded)
                               + AKTUÁLNÍ název, obrázek, cena, dostupnost z kopie katalogu
 hlavička ── CartBadgeViewComponent ── SUM(Quantity) v SQL
```

- **Agregát a invarianty.** `Cart` je jeden celek: řádky (`CartLine`) mění jen jeho metody `AddItem`,
  `ChangeQuantity`, `RemoveItem`, `Clear` a ty hlídají pravidla. Metody `CartLine` jsou `internal`, takže pravidla
  nejde obejít ani z aplikační vrstvy. Služba je nezná, jen načte košík, zavolá metodu a uloží. Unikátní indexy
  (`CustomerId`, `CartId + ProductId`) pravidla jistí i v databázi.
- **Košík neblokuje zboží a nic neposílá.** Rezervace vzniká až objednávkou (sága). Kdyby ji dělal košík,
  plné košíky by vyprodaly sklad bez jediného nákupu a sklad by navíc potřeboval timeout pro zapomenuté košíky.
- **Cena je orientační.** Košík si ukládá jen ID produktu a množství. Název, cenu a dostupnost bere při každém
  zobrazení z kopie katalogu, takže je vždy aktuální. `PriceWhenAdded` slouží jen k upozornění „cena se změnila“.
  Závazná cena vznikne až v objednávce (snímek v `OrderItem`).
- **Vyprodaný produkt** v košíku zůstane (zboží se může vrátit), ale nepočítá se do součtu a znovu přidat nejde.
  **Stažený produkt** (`ProductRemovedV1`) se ukáže jako „už není v nabídce“ a jde jen odebrat.
- **Proč v Orderingu:** pokladna (krok 4) musí v jedné transakci založit objednávku a vyprázdnit košík.
  Hranice modulu kopíruje hranici konzistence. Vlastní modul s vlastní DB by kvůli tomu potřeboval ságu.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Orders/Carts/Cart.cs`, `CartLine.cs` | Agregát košíku a jeho řádek; invarianty (20 řádků, 1–100 ks, produkt jednou, ne vyprodaný/stažený) |
| `src/MiniShop.Application/Ordering/Carts/ICartService.cs`, `CartService.cs` | Use-casy košíku; DTO s aktuálními údaji z kopie katalogu |
| `src/MiniShop.Application/Ordering/Carts/CartContracts.cs` | `CartDto`, `CartLineDto` (`PriceChanged`, `IsInOffer`, `IsOrderable`) |
| `src/MiniShop.Application/Ordering/Carts/ICartRepository.cs`, `src/MiniShop.Infrastructure/Ordering/CartRepository.cs` | Košík zákazníka, počet kusů (SUM v SQL) |
| `src/MiniShop.Infrastructure/Ordering/Configurations/CartConfiguration.cs` | Tabulky `Carts`, `CartLines`, unikátní indexy, kaskádové mazání řádků |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_Carts.cs` | Migrace – jen dvě nové tabulky |
| `src/MiniShop.Web/Pages/Cart/Index.cshtml(.cs)` | Stránka košíku a handlery `Add`, `ChangeQuantity`, `Remove` (PRG, hlášky přes TempData) |
| `src/MiniShop.Web/Carts/CartBadgeViewComponent.cs`, `Pages/Shared/Components/CartBadge/Default.cshtml` | „Košík (N)“ v hlavičce, jen pro zákazníka |
| `tests/MiniShop.Tests/Domain/CartTests.cs` | Všechny invarianty |
| `tests/MiniShop.Tests/Web/CartPageTests.cs` | Formuláře jako z prohlížeče, návrat na výlohu, open redirect, admin bez košíku |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Application/Ordering/ICatalogProductRepository.cs`, `Infrastructure/Ordering/CatalogProductRepository.cs` | `GetByIdsAsync` – i stažené produkty (název pro „už není v nabídce“) |
| `Application/Ordering/OrderService.cs`, `IOrderService.cs` | `AnonymizeCustomerAsync` smaže i košík (stejná transakce, „všechno, nebo nic“) |
| `Infrastructure/Ordering/OrderingDbContext.cs`, `OrderingInfrastructure.cs` | `DbSet<Cart>`, registrace repozitáře |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | Registrace `ICartService` |
| `Web/Accounts/OrderAccessExtensions.cs` | `ToCustomerId()` – čí je košík |
| `Web/Pages/Shop/Index.cshtml(.cs)` | Tlačítko **Do košíku** (Objednat zůstává do kroku 4), hlášky, `CurrentUrl` pro návrat; nepřihlášený „Přihlásit a nakoupit“ se vrací do výlohy |
| `Web/Pages/Shared/_Layout.cshtml`, `wwwroot/css/site.css` | Odznak košíku v menu; `.cart-thumb`, `.cart-quantity` |
| `Web/Pages/Privacy.cshtml` | Nový osobní údaj: košík (do smazání účtu) |
| testy | `OrderServiceTests` (sekce Košík: sloučení řádků, per zákazník, změna ceny, vyprodáno, stažený produkt, smazání účtu), `AuthorizationMatrixTests`, `MiniShopWebFactory.EnsureCatalogProductAsync` |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | Ordering (proč je tam košík), kopie katalogu, Accounts (smazání účtu), slovníček (invariant, hranice konzistence), historie |

## Rozhodnutí v tomto kroku

- **Košík v modulu Ordering**, ne vlastní modul – checkout bude jedna transakce.
- **Zatím bez doménových událostí košíku** (`CartItemAdded` …). V Orderingu jde každá doménová událost přes outbox.
  Bez posluchače by jen plnila tabulku. Přijdou v kroku 10 (opuštěný košík), až je bude někdo poslouchat.
- **Jen pro přihlášené** (`RequireCustomer` – potvrzený e-mail, admin košík nemá). Anonymní košík v cookie
  se sloučením po přihlášení (3b) je odložený.
- **Jen Razor Pages**, žádné `/api/cart` – dnes by ho nikdo nevolal.
- **Přidání = POST na stránku Košík s `returnUrl`**, ne handler na veřejné výloze: autorizace zůstává deklarativní
  (`[Authorize]` na stránce) a výloha se nemusí ptát v kódu. `returnUrl` jen lokální (`Url.IsLocalUrl` → `LocalRedirect`),
  jinak by šlo poslat zákazníka na cizí web (open redirect).
- **Řádky v pořadí přidání**, ne podle názvu – při změně množství neposkakují.
- **Zákaz měnit množství u staženého produktu jen v UI** – změna nic nepokazí a služba by kvůli tomu musela číst katalog.
- **Nasazeno bez pokladny** – tlačítko „K pokladně“ je zatím neaktivní, objednává se dál přes „Objednat“.

## Známá omezení

- Dvě záložky, které ve stejný okamžik přidají do prázdného košíku (nebo stejný nový produkt), narazí na unikátní
  index – druhý požadavek skončí chybou 500. Opakování pomůže; u košíku to za složitější řešení nestojí.
- Souběžná změna množství téhož řádku: vyhraje poslední (last write wins).
- Slevy (`QuantityDiscountPolicy`) košík neukazuje – spočítá je až pokladna.
- Prázdný košík (řádek v `Carts`) zůstává v databázi, dokud se nesmaže účet.
- Nepřihlášený návštěvník košík nemá (3b).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu `Applying migration '…_Carts'`.
2. Přihlas se jako zákazník (potvrzený e-mail) → **Obchod** → **Do košíku** u dvou produktů, u jednoho dvakrát.
   Zůstaneš na stejné stránce výlohy (i s filtry), hláška „… je v košíku“, v menu **Košík 3**.
3. **Košík** – dva řádky, jeden se 2 kusy. Změň množství na 150 → hláška „Množství musí být od 1 do 100 kusů.“
4. Admin změní cenu produktu → zákazník v košíku vidí novou cenu a poznámku „Cena se změnila“.
5. Odhlas se a přihlas – košík je pořád tam.
6. Admin v menu Košík nemá, `/Cart` mu vrátí „Přístup odepřen“.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí (migrace přidá dvě tabulky, `backup.py` se nemění – `ordering.db` už zálohuje).
3. https://minishop-tichy.duckdns.org/Shop – přidat do košíku, zkontrolovat `/Cart`.

## Oprava po nasazení

V produkci košík ukázal obrázky produktů v plné velikosti. Kód byl v pořádku, server `site.css` s novou třídou
`.cart-thumb` měl. Prohlížeč ale použil **starý `site.css` ze své cache**: adresa `/css/site.css` se mezi verzemi
nemění a server neposílá `Cache-Control`, takže si prohlížeč sám odhadne, jak dlouho soubor platí (heuristická cache).

- `_Layout.cshtml`: `asp-append-version="true"` u `site.css` a Bootstrapu (u Mermaidu na stránce Dokumentace taky).
  Adresa pak nese otisk obsahu (`/css/site.css?v=…`): nová verze souboru = nová adresa = prohlížeč ji stáhne.
  Je to stejný princip jako neměnná jména obrázků z kroku 2B.
- `Cart/Index.cshtml`: náhled má `width="64" height="48"` i přímo v HTML, takže zůstane malý i bez CSS.
- Test `CartPageTests.NahledVKosiku_MalyIBezNovehoCss`.

## Vyzkoušejte sami

- Zkus v `CartService` místo `cart.AddItem(...)` přidat řádek „bokem“ – proč to nejde zkompilovat?
- Přidej do košíku 21. různý produkt. Kde přesně se to zastaví a kdo napíše hlášku?
- Vykup na jiném účtu poslední kusy produktu, který máš v košíku. Jak dlouho trvá, než to košík ukáže, a proč?
- Pošli formulář „Do košíku“ s `returnUrl=https://example.com` (třeba přes nástroje prohlížeče). Kam tě to přesměruje?
- Rozmysli si: co by se muselo stát, kdyby košík zboží rezervoval? Kolik nových zpráv a jaký timeout by bylo potřeba?
