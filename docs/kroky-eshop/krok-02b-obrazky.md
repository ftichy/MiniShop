# E-shop krok 2B: Obrázky produktů

Admin nahraje k produktu obrázek na stránce Úprava produktu, výloha ho ukáže na kartě. Obrázek patří skladu
(soubor na jeho volume), do zpráv jde jen jméno souboru a veřejně ho vydává Web s cache na rok.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 2A](krok-02a-vyloha.md).

## Jak to funguje

Přirovnání: v katalogu zásilkové služby je u zboží jen číslo fotky. Fotky leží v archivu a tiskárna si je vyzvedne,
až je potřebuje – nikdo je nerozesílá poštou s každou změnou ceny.

```
 admin: Úprava produktu ── soubor ──► Web (InventoryApiClient, klíč) ── PUT /api/products/7/image ──► Inventory.Api
   ProductImageService:
     1. typ podle prvních bajtů (JPEG/PNG/WebP), max. 1 MB          – jinak DomainException (400)
     2. jméno = "7-" + SHA-256(obsah)[0..12] + přípona              – 7-3fa9c1d2e8b4.png
     3. soubor → FileSystemProductImageStore (/data/images)
     4. product.SetImage → ProductChanged → COMMIT + Outbox[ProductUpdatedV1, ProductUpdatedV2(ImageFileName)]
     5. smazat starý soubor
                                                     │ RabbitMQ
 Ordering: CatalogProduct.ImageFileName ◄────────────┘
 prohlížeč: /Shop ── <img src="/media/products/7-3fa9c1d2e8b4.png"> ──► Web ── GET /api/products/images/… ──► sklad
            ◄── 200 image/png, Cache-Control: public, max-age=31536000, immutable
```

- **Obrázek není zpráva.** Megabajty v RabbitMQ a v outboxu by zdržely ostatní zprávy a nafoukly databáze.
  Zpráva nese odkaz (jméno), data leží u vlastníka. `ProductUpdatedV2.ImageFileName` existovalo už od kroku 2A,
  takže se nezměnil kontrakt ani `ordering.db`.
- **Neměnné jméno.** Jméno je odvozené z obsahu – nový obrázek má nové jméno, takže pod jednou adresou je vždy
  stejný soubor. Prohlížeč si ho proto smí uložit na rok bez ptaní (`immutable`) a na sklad se sahá jen poprvé.
- **Pořadí kroků** (soubor → databáze → smazat starý): když Ordering dostane zprávu s novým jménem, soubor už existuje.
  Spadne-li databáze, zůstane jen nepoužitý soubor; kdyby se starý mazal dřív, výloha by chvíli ukazovala chybějící obrázek.
- **Bezpečnost uploadu:** typ podle „magic bytes“, ne podle přípony nebo `Content-Type` (ty si útočník napíše sám);
  SVG zakázané (může obsahovat JavaScript); limit 1 MB ve Webu i ve skladu (větší požadavek Kestrel ani nedočte);
  jméno souboru generuje server a čte se jen jméno podle přesného vzoru – `../appsettings.json` neprojde (path traversal).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Application/Inventory/ProductImageFormat.cs` | Rozpoznání typu podle bajtů, limit 1 MB, jméno souboru z otisku, kontrola jména, Content-Type |
| `src/MiniShop.Application/Inventory/ProductImageService.cs`, `IProductImageService.cs`, `IProductImageStore.cs` | Nahrání / odebrání / čtení obrázku; rozhraní úložiště |
| `src/MiniShop.Infrastructure/Inventory/FileSystemProductImageStore.cs` | Soubory ve složce (zápis přes dočasný soubor a přejmenování) |
| `src/MiniShop.Infrastructure/Inventory/Migrations/…_ProductImage.cs` | Sloupec `Products.ImageFileName` |
| `src/MiniShop.Web/Catalog/ProductImageEndpoint.cs` | Veřejné `GET /media/products/{soubor}` s cache na rok, při výpadku skladu 503 bez cache |
| `tests/…/FileSystemProductImageStoreTests.cs`, `Web/ProductImageEndpointTests.cs`, `TestDoubles/InMemoryProductImageStore.cs` | Úložiště, path traversal, formáty; endpoint a hlavičky |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Inventory/Product.cs` | `ImageFileName`, `SetImage`, `RemoveImage` (změna obrázku = změna produktu) |
| `Application/Inventory/ProductService.cs`, `ProductContracts.cs`, `ProductMapping.cs`, `InventoryContractMapping.cs` | Smazání produktu uklidí obrázek; `ProductDto.ImageFileName`; V2 nese skutečné jméno |
| `Infrastructure/Inventory/InventoryInfrastructure.cs`, `Configurations/ProductConfiguration.cs` | `AddFileSystemProductImages(path)`; sloupec |
| `Inventory.Api/Program.cs`, `Controllers/ProductsController.cs`, `Dockerfile` | Úložiště z `ProductImages:Path` (od content root); `PUT/DELETE /{id}/image`, `GET /images/{soubor}`; `ENV ProductImages__Path=/data/images` |
| `Application/Ordering/OrderContracts.cs`, `OrderMapping.cs` | `CatalogItemDto.ImageFileName` |
| `Web/Inventory/InventoryApiClient.cs`, `Extensions/…`, `Program.cs` | Klient i pro obrázky (`IProductImageService`), mapování endpointu |
| `Web/Pages/Products/Edit.cshtml(.cs)` | Náhled, nahrání, odebrání obrázku (limit požadavku, `ModelState.Clear` – stejná past jako u kategorií) |
| `Web/Pages/Shop/Index.cshtml`, `wwwroot/css/site.css` | Obrázek na kartě (`loading="lazy"`, poměr 4 : 3), zástupný blok bez obrázku |
| `deploy/backup.py` | `KEYS_DIR` → seznam `DIRECTORIES` (klíče Webu + `/inventory/images`), záloha i obnova |
| `.gitignore` | `src/MiniShop.Inventory.Api/images/` |
| testy | `ProductTests`, `OrderServiceTests` (V2 s jménem, výměna, stejný obrázek, SVG/GIF/text, velikost, úklid, neplatná jména), `ProductAdministrationTests` (nahrání, náhled), `AuthorizationMatrixTests` |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | Inventory, kopie katalogu, Ordering, zálohy, kontejnery, slovníček, historie |

## Rozhodnutí v tomto kroku

- **Vlastník je sklad** (spravuje katalog), **Web jen vydává** – sklad z internetu dostupný není a mít nemá.
- **Proxy přes Web místo veřejné cesty v Caddy ke skladu** – sklad zůstává celý za servisním klíčem; cache v prohlížeči
  zátěž srovná (každý obrázek jde ze skladu na prohlížeč jednou).
- **Bez zmenšování a převodu** – ImageSharp má licenční podmínky, SkiaSharp nativní knihovny pro ARM VM. Limit 1 MB
  a poměr 4 : 3 v CSS (`object-fit: contain` – nic se neořízne) stačí.
- **Jméno z otisku obsahu** (12 hex znaků SHA-256) místo náhodného – stejný obrázek znovu nic nezmění (žádná zpráva).
- **Úklid starého souboru až po uložení a „potichu“** – chyba úklidu nechá jen soubor navíc, data jsou v pořádku.
- **Formuláře na stránce s `[BindProperty]` = `ModelState.Clear` a vlastní kontroly** (poučení z opravy kroku 2A).

## Známá omezení

- Jeden obrázek na produkt, bez miniatur – výloha stahuje plnou velikost (max. 1 MB).
- Při výpadku skladu se nenačtou obrázky, které prohlížeč ještě nemá (503, stránka běží).
- Soubor bez záznamu (spadlé uložení, ruční zásah) zůstane ležet – žádný úklid „sirotků“.
- Obnova zálohy vrátí obrázky ze stejného okamžiku jako `inventory.db` – starší záloha může mít jiné obrázky než výloha
  před havárií (výloha se srovná obnovou katalogu).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu skladu `Applying migration '…_ProductImage'`.
2. Admin → **Produkty** → **Upravit** → nahraj JPEG/PNG/WebP do 1 MB → „Obrázek byl nahrán“, náhled vpravo.
   Soubor je v `src/MiniShop.Inventory.Api/images/` (jméno `ID-otisk.přípona`).
3. **Obchod** – za pár sekund má karta obrázek. V nástrojích prohlížeče (Network) má `/media/products/…`
   hlavičku `Cache-Control: public, max-age=31536000, immutable`; po obnovení stránky jde z cache.
4. Zkus nahrát SVG nebo soubor přejmenovaný na `.png` – hláška „Povolené jsou jen obrázky JPEG, PNG a WebP.“
5. Nahraj jiný obrázek – jméno se změní, starý soubor zmizí. **Odebrat obrázek** – karta ukáže „bez obrázku“.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí obě služby (migrace přidá sloupec, `deploy.sh` přinese nový `backup.py`).
3. Nahrát obrázek k produktu, zkontrolovat výlohu https://minishop-tichy.duckdns.org/Shop.
4. Ruční záloha znovu a v logu (`sudo journalctl -u minishop-backup -n 20`) řádek `images/: N souborů`.

## Vyzkoušejte sami

- Nahraj stejný obrázek dvakrát – v outboxu skladu přibude zpráva jen poprvé. Proč?
- Přejmenuj `.txt` soubor na `.png` a nahraj ho – kde přesně se zastaví?
- Otevři `/media/products/..%2F..%2Fappsettings.json` – co vrátí Web a dostane se požadavek vůbec do skladu?
- V `ProductImageService` prohoď pořadí „smazat starý soubor“ a „uložit do databáze“. Co by uviděl zákazník, kdyby
  uložení selhalo?
