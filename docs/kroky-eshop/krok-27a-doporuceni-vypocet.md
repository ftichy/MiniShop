# E-shop krok 27A: Zákazníci také koupili – výpočet

Směr A z kandidátů po dopravci. Na detailu produktu bude oddíl „Zákazníci také koupili“ (až 4 produkty kupované spolu).
Krok 27A ho **spočítá a pošle zprávou** (Reporting.Api), krok 27B ho uloží do kopie katalogu v Orderingu a ukáže na `/Shop/{id}`.
Zavádí se od konce (expand → contract): v 27A zprávu zatím nikdo neposlouchá, chování produkce se nemění.

## Jak to funguje

```
přehled tržeb (SalesOrder – už existuje, plní ho události objednávek)
        │  každých 5 minut, vedoucí kopie Reporting.Api
        ▼
ProductRecommendationsWorker → ProductRecommendationService.RecomputeAsync
   1. objednávky za 180 dní, jen CountsAsRevenue (bez zamítnutých, zrušených, vrácených), KeptLines (bez vrácených kusů)
   2. BoughtTogether.Compute: dvojice produktů ve stejné objednávce, aspoň ve 2 objednávkách, nejvýš 4, nejčastější první
   3. porovnat s tabulkou ProductRecommendations (co naposledy odešlo)
   4. jen změněné → ProductRecommendationsChangedV1 do outboxu + uložit tabulku (jedna transakce)
      produkt, který už nic nedoporučuje → zpráva s prázdným seznamem
        ▼
RabbitMQ (exchange + proud minishop.events) – zatím bez odběratele (Ordering od kroku 27B)
```

Teorie:

- **Odvozený čtecí model.** Nevzniká z nových zpráv, ale z jiného čtecího modelu (přehledu tržeb), který už zvládá zprávy
  mimo pořadí, vratky i přestavění. Nová projekce tak nemusí znovu řešit ani jedno z toho.
- **Dávka místo reakce na zprávu.** Doporučení nemusí být aktuální na vteřinu. Přepočet z celých dat je jednodušší než upravovat
  čítače po každé události a sám opraví cokoli, co se mezitím pokazilo (zrušená objednávka, vratka).
- **Posílat jen změny.** Bez tabulky „co jsme poslali“ by každých 5 minut odešla zpráva za každý produkt.
- **Soukromí.** Dvojice se doporučí až od 2 různých objednávek. Z detailu produktu tak nejde vyčíst, co si koupil jeden člověk.
  Zpráva nenese nic o zákaznících.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Reporting/ProductRecommendationsChangedV1.cs` | nová událost: produkt, seznam doporučených ID, čas výpočtu |
| `src/MiniShop.Domain/Reporting/BoughtTogether.cs` | výpočet dvojic (`BoughtTogether`) + naposledy odeslaná doporučení (`ProductRecommendation`) |
| `src/MiniShop.Application/Reporting/ProductRecommendationService.cs` | dávka: výpočet, porovnání, zprávy jen při změně |
| `src/MiniShop.Infrastructure/Reporting/ProductRecommendationRepository.cs` | repozitář tabulky `ProductRecommendations` |
| `src/MiniShop.Infrastructure/Reporting/Configurations/ProductRecommendationConfiguration.cs` | tabulka, seznam ID jako PostgreSQL `integer[]` |
| `src/MiniShop.Infrastructure/Reporting/Migrations/…_ProductRecommendations.cs` | migrace – jen nová tabulka (kompatibilní se starší verzí) |
| `src/MiniShop.Reporting.Api/ProductRecommendationsWorker.cs` | časovač 5 min (`Recommendations:IntervalMinutes`), vlastní span `product recommendations` |
| `tests/MiniShop.Tests/Domain/BoughtTogetherTests.cs` | práh 2 objednávek, pořadí, nejvýš 4, změna seznamu |
| `tests/MiniShop.Tests/Application/ProductRecommendationServiceTests.cs` | nad PostgreSQL: jen změny, bez zrušených a vrácených, zmizelé = prázdný seznam, prázdný přehled nic nemaže |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/Reporting/ReportingDbContext.cs`, `ReportingInfrastructure.cs` | `DbSet` a registrace repozitáře |
| `src/MiniShop.Reporting.Api/Program.cs`, `ReportingModule.cs`, `appsettings.json` | worker jen na vedoucí kopii, služba, interval |
| `tests/MiniShop.Tests/Contracts/IntegrationContractTests.cs` | tvar JSONu nového kontraktu |
| `tests/MiniShop.Tests/Web/WorkerRegistrationTests.cs` | nový worker jen na vedoucí kopii |
| `docs/prirucka/architektura/moduly/reporting.md` | oddíl Zákazníci také koupili, zprávy, součásti |
| `docs/prirucka/eda/observabilita.md` | atribut `minishop.recommendations.changed` |

## Rozhodnutí

- **V Reporting.Api, ne v novém modulu.** Data (objednávky s položkami a stavem) už tam jsou. Vyhodnocení mikroservis M3
  doporučilo nové služby nevytvářet.
- **Výsledek jako událost do kopie katalogu (27B), ne HTTP dotaz z výlohy.** Stejně jako hvězdičky z hodnocení
  (`ProductRatingChangedV1`): veřejná stránka nezávisí na tom, jestli Reporting.Api zrovna běží.
- **Interval 5 minut, okno 180 dní, nejvýš 4 doporučení, práh 2 objednávky.** Konstanty v kódu, interval v konfiguraci.
- **Testovací objednávky generátoru provozu se počítají** (rozhodnutí uživatele). Reporting zákazníky nezná a vylučovat je
  by znamenalo posílat mu údaj o zákazníkovi.
- **Stejná data = stejný výsledek** (při shodě četnosti nižší ID). Jinak by se pořadí náhodně měnilo a zprávy by odcházely zbytečně.

## Známá omezení

- Během přestavění přehledu tržeb (snímky teprve přicházejí) může jedno kolo dávky doporučení změnit nebo smazat. Další kolo to
  po dokončení přestavění srovná. Nad úplně prázdným přehledem dávka nepošle nic.
- Výpočet načte objednávky za 180 dní do paměti. Na tisíce objednávek to stačí, na miliony ne (pak SQL dotaz nad `SalesOrderLines`).
- Zpráva zatím nemá odběratele – doporučení na webu nejsou vidět až do kroku 27B.

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `BoughtTogetherTests`, `ProductRecommendationServiceTests`.
2. Po nasazení: v logu `reporting-api` řádek `[Doporučení] Změnila se doporučení u N produktů` (první kolo hned po startu).
3. Tabulka `reporting."ProductRecommendations"` je naplněná. V proudu zpráv (`/Admin/EventStream`) jsou zprávy
   `ProductRecommendationsChangedV1`.

## Vyzkoušejte sami

- Spusťte generátor provozu na pár kol a po 5 minutách se podívejte do Proudu zpráv: přijdou nové `ProductRecommendationsChangedV1`
  jen pro produkty, kterým se pořadí opravdu změnilo.
- Zrušte objednávku, která jako jediná držela nějakou dvojici nad prahem – v dalším kole dostane produkt prázdný seznam.
