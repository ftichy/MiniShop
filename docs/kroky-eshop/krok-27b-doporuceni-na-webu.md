# E-shop krok 27B: Zákazníci také koupili – na webu

Druhá polovina směru A. Doporučení, která od kroku 27A počítá Reporting.Api, si Ordering ukládá do kopie katalogu a detail
produktu `/Shop/{id}` je ukáže jako oddíl „Zákazníci také koupili“ (až 4 karty produktů).

## Jak to funguje

```
Reporting.Api (27A): dávka každých 5 min → ProductRecommendationsChangedV1 (jen změny)
        │                                              ▲
        ▼                                              │ SyncProductRecommendationsV1 (po startu vedoucí kopie Webu)
Ordering: CatalogProjectionHandler                     │ → SyncProductRecommendationsHandler → pošle všechna uložená znovu
   → CatalogProduct.ApplyRecommendations (starší výpočet nepřepíše novější)
        ▼
/Shop/{id}: ICatalogQueryService.GetRecommendationsAsync → produkty v nabídce v pořadí od Reportingu → _ProductCard
```

Teorie:

- **Kopie u odběratele.** Jako hvězdičky z hodnocení: výloha se Reporting.Api za běhu neptá. Když služba vypadne, doporučení na
  webu zůstanou, jen se přestanou měnit.
- **Kdo si řekne o doplnění.** Zprávy z 27A nikdo neposlouchal, Ordering je tedy nemá. Kdyby je Reporting poslal sám po svém
  startu, ztratily by se: `deploy.sh` nasazuje službu **dřív** než Web, stará verze Webu je ještě nemá navázané. Proto žádá
  **odběratel**, až po svém startu (stejný vzor jako `SyncCatalogV1` a `SyncPurchasesV1`).
- **Zpráva bez handleru.** Při postupném nasazení chvíli poslouchá frontu `minishop.ordering` i stará kopie Webu. Zprávu, pro
  kterou nemá handler, potvrdí a zahodí. Ordering proto žádá **po každém startu vedoucí kopie**, ne jen když doporučení chybí:
  co se ztratí, doplní příští start. Stojí to desítky zpráv a stejný čas výpočtu nic nezmění.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Reporting/SyncProductRecommendationsV1.cs` | příkaz „pošli mi znovu všechna doporučení“ (Ordering → Reporting) |
| `src/MiniShop.Application/Reporting/SyncProductRecommendationsHandler.cs` | vyřídí příkaz – `ProductRecommendationService.RepublishAllAsync` (s původním časem výpočtu) |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_CatalogRecommendations.cs` | sloupce `RecommendedProductIds` (`integer[]`, výchozí `'{}'`) a `RecommendationsComputedAtUtc` – jen přidání |
| `tests/MiniShop.Tests/Application/OrderServiceTests.Recommendations.cs` | celá cesta: objednávky → přehled → dávka → kopie katalogu; doplnění na žádost Orderingu |
| `tests/MiniShop.Tests/Domain/CatalogRecommendationsTests.cs` | starší výpočet nepřepíše, prázdný seznam smaže, nikdy sám sebe |
| `tests/MiniShop.Tests/Web/ProductRecommendationsPageTests.cs` | oddíl na detailu v pořadí, bez staženého produktu, bez doporučení se neukáže |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Domain/Orders/CatalogProduct.cs` | `RecommendedProductIds`, `RecommendationsComputedAtUtc`, `ApplyRecommendations` |
| `src/MiniShop.Infrastructure/Ordering/Configurations/CatalogProductConfiguration.cs` | pole `integer[]` s výchozí hodnotou v DB (starší verze Webu sloupec nevyplní) |
| `src/MiniShop.Application/Ordering/Catalog/CatalogProjectionHandler.cs` | `HandleAsync(ProductRecommendationsChangedV1)` |
| `src/MiniShop.Application/Ordering/Catalog/ICatalogQueryService.cs`, `CatalogQueryService.cs` | `GetRecommendationsAsync` – jen produkty v nabídce, pořadí od Reportingu |
| `src/MiniShop.Application/Ordering/Catalog/CatalogSyncService.cs`, `Web/BackgroundServices/CatalogSyncOnStartupWorker.cs` | po startu `RequestRecommendationsAsync` |
| `src/MiniShop.Application/Reporting/ProductRecommendationService.cs` | `RepublishAllAsync` |
| `src/MiniShop.Reporting.Api/ReportingModule.cs`, `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | registrace handlerů |
| `src/MiniShop.Web/Pages/Shop/Detail.cshtml(.cs)` | oddíl `data-track="product-recommendations"` s `_ProductCard` |
| příručka: `moduly/reporting.md`, `moduly/ordering.md`, `moduly/index.md`, `historie.md` | zprávy, diagram, popis |

## Rozhodnutí

- **Zobrazit jen na detailu produktu** (rozhodnutí uživatele). Košík ani výloha zatím ne.
- **Vyprodané doporučení zůstane** se štítkem (jako ve výloze), stažené z nabídky se vynechá.
- **Žádost po každém startu** místo „jen když chybí“ – kvůli zprávám, které zahodí stará kopie Webu (viz výše).
- **Bez tlačítka „přestavět“.** Dávka se každých 5 minut sama srovná se skutečností; doplnění řeší start Webu.

## Známá omezení

- Nová prázdná kopie katalogu (nová databáze) dostane doporučení až při dalším startu Webu nebo s další změnou výpočtu.
  Produkty v ní v tu chvíli ještě nejsou a handler neznámý produkt přeskočí.
- Každý start vedoucí kopie Webu (nasazení, převzetí vedení) pošle desítky zpráv navíc.
- Doporučení může ukázat produkt, který mezitím vyprodali (štítek „Vyprodáno“) – kopie katalogu to ví, Reporting ne.

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `Doporuceni_*`, `CatalogRecommendationsTests`, `ProductRecommendationsPageTests`.
2. Po nasazení v logu Webu: `[Katalog] Žádám Reporting o doporučení „Zákazníci také koupili“ (SyncProductRecommendationsV1)`.
3. Detail produktu, který se kupoval s jiným (po zátěžovém testu skoro každý): oddíl „Zákazníci také koupili“ pod nákupním boxem.

## Vyzkoušejte sami

- Otevřete detail produktu, klikněte na doporučený produkt a podívejte se, jestli doporučuje zpátky ten první (dvojice platí
  oběma směry, pořadí se ale může lišit).
- Stáhněte doporučený produkt z nabídky v *Správa → Produkty*: po pár sekundách z oddílu zmizí (kopie katalogu), i když ho
  Reporting dál doporučuje.
