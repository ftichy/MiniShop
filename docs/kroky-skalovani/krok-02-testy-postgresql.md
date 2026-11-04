# Škálování krok 2: testy proti PostgreSQL

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Cíl kroku: testovat proti databázi, která poběží v produkci.
SQLite se v detailech chová jinak (velikost písmen, přesnost času, pořadí řádků, souběh) – chyby se mají najít v testech.

## Jak to funguje

- **Přepínač:** proměnná prostředí `MINISHOP_TEST_DATABASE` = `Sqlite` (výchozí) nebo `Postgres`. Stejná sada testů
  (OrderService, sága, pokladna, kupony, vratky, hodnocení, outbox, inbox, Web i API skladu v paměti) poběží nad obojím.
- **`TestDatabases`** nahradila v testech `InMemoryDatabase`: test si vezme databázi modulu (`_databases.Ordering` …),
  factory Webu/skladu volá `_databases.Configure(builder, …)` – nastaví stejné klíče jako produkce
  (`ConnectionStrings:<Modul>`, nebo `Database:Provider=Postgres` + `ConnectionStrings:minishop`).
  - SQLite: každý modul vlastní databázi v paměti (jako dřív).
  - PostgreSQL: všechny moduly **jedna** databáze, oddělené schématy – jako v produkci.
- **`PostgresTestServer`:** jednou za běh spustí přes Testcontainers `postgres:17-alpine` (bez `fsync` – data po testu nikdo
  nepotřebuje), založí šablonu `minishop_template` a provede do ní migrace všech 8 modulů. Každý test pak dostane kopii
  `CREATE DATABASE test_… TEMPLATE minishop_template` (desítky ms) a v `Dispose` ji smaže (`DROP DATABASE … WITH (FORCE)`).
  Kontejner po skončení procesu uklidí Testcontainers (Ryuk).
- **CI:** nový job „Testy PostgreSQL“ (souběžně s „Build a testy“, jehož jméno = povinný check se nemění). Image se
  sestaví až po obou testovacích jobech.

## Co testy nad PostgreSQL našly

| Nález | Příčina | Oprava |
| --- | --- | --- |
| 103 testů: „Kategorie s ID 1 neexistuje“ | kategorii „Nezařazeno“ zakládá v SQLite **ručně dopsané SQL** v migraci e-shop kroku 2; `InitialPostgres` vznikla z modelu a data v ní nejsou | nová migrace jen pro PostgreSQL `UncategorizedCategory` – vloží kategorii, jen když chybí (ID přidělí databáze). Nová migrace, ne úprava `InitialPostgres`: lokální databáze z AppHostu ji už mají zapsanou jako provedenou |
| výloha: hledání „audio“ vrátilo 3 produkty | stejná příčina – kategorie „Audio“ dostala ID 1 a test do ní dal všechny produkty | (viz výše) |
| `NeuplnaKopieKatalogu…`: `relation "catalogproducts" does not exist` | test měnil data ručním SQL psaným pro SQLite (bez schématu a uvozovek) | `ExecuteUpdateAsync` přes EF – stejný příkaz pro obě databáze |

Kód aplikace žádnou chybu neměl – časy, desetinná čísla, unikátní indexy, concurrency tokeny i řazení se chovají stejně.

## Nové soubory

| Soubor | Co |
| --- | --- |
| `tests/MiniShop.Tests/TestDoubles/TestDatabases.cs` | databáze modulů pro test podle `MINISHOP_TEST_DATABASE` |
| `tests/MiniShop.Tests/TestDoubles/PostgresTestServer.cs` | kontejner PostgreSQL (Testcontainers), šablona s migracemi, kopie pro každý test |
| `src/MiniShop.Infrastructure.PostgreSql/Inventory/Migrations/…_UncategorizedCategory.cs` | kategorie „Nezařazeno“ v PostgreSQL |

## Změněné soubory

- `MiniShop.Tests.csproj` – balíček `Testcontainers.PostgreSql`.
- Testové třídy s databází (`OrderServiceTests`, `JourneyServiceTests`, `OutboxTests`, `IdempotentHandlerTests`,
  `MessageMaintenanceTests`, `AccountsTests`, `AccountAdministrationTests`) a factory `MiniShopWebFactory`,
  `InventoryApiFactory` – `TestDatabases` místo `InMemoryDatabase`.
- `OrderServiceTests`: `Moduly_MajiOddeleneDatabaze` porovnává dvojici (databáze, schéma); nový test
  `Kupon_PosledniPouzitiNaraz_ProjdeJenJednaObjednavka` (dvě objednávky s posledním použitím kuponu opravdu naráz).
- `DependencyInjection.AddInfrastructure` – parametry `ModuleDatabase` (testy).
- `.github/workflows/ci.yml` – job `test-postgres`, `images` čeká na oba testovací joby.
- Příručka: Databáze (testy nad oběma databázemi), CI, knihovny, vrstvy, historie.

## Rozhodnutí

- **Stejná sada testů nad oběma databázemi** (přepínač), ne zvláštní testy pro PostgreSQL – rozdíl databází se může
  projevit kdekoli. SQLite zůstává výchozí lokálně: rychlé a bez Dockeru.
- **Šablona + kopie databáze pro každý test** místo migrací v každém testu (8 schémat × stovky testů by trvalo minuty)
  i místo sdílené databáze (testy běží paralelně a nesmí se ovlivnit).
- **Testy migrací SQLite** (`DatabaseMigrationTests` – baseline, `sqlite_master`) zůstávají jen nad SQLite; běží v obou
  variantách, protože si databázi v paměti zakládají samy.
- **„Testy PostgreSQL“ je povinný check** v rulesetu `main-protection` (přidán po prvním zeleném běhu v CI, 4. 11. 2026) –
  PR, kterému padají testy nad PostgreSQL, nejde sloučit.

## Známá omezení

- Varianta PostgreSQL trvá déle (~1,5 min lokálně oproti ~50 s) a potřebuje Docker.
- Produkce dál běží na SQLite – převod dat je krok 3.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
$env:MINISHOP_TEST_DATABASE="Postgres"; dotnet test MiniShop.slnx; Remove-Item Env:MINISHOP_TEST_DATABASE
```

## Vyzkoušejte sami

1. Spusťte testy nad PostgreSQL (výše) a během běhu se podívejte do `docker ps` – kontejner `postgres:17-alpine`
   a pomocný `testcontainers-ryuk`. Po skončení oba zmizí.
2. Zkuste v `PostgresTestServer` vynechat migraci `UncategorizedCategory` (nebo v testu napsat ruční SQL) – varianta
   SQLite projde, PostgreSQL spadne. Přesně proto se testuje nad oběma.
3. V PR se podívejte na dva testovací checky: „Build a testy“ (SQLite) a „Testy PostgreSQL“.
