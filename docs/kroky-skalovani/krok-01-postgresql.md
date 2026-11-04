# Škálování krok 1: PostgreSQL vedle SQLite

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Cíl kroku: aplikace umí **obě** databáze. Produkce dál běží
na SQLite beze změny (expand → contract), přepnutí přijde s převodem dat v kroku 3.

## Jak to funguje

- **Provider podle konfigurace:** `Database:Provider` = `Sqlite` (výchozí) nebo `Postgres`.
  `ModuleDatabase.FromConfiguration(configuration, "Ordering")` vrátí provider a connection string:
  SQLite bere `ConnectionStrings:Ordering` (soubor pro každý modul), PostgreSQL `ConnectionStrings:minishop`
  (jedna databáze pro všechny).
- **Schéma pro každý modul:** `UseModuleDatabase(database, OrderingDbContext.Schema)` nastaví u PostgreSQL
  tabulku historie migrací do schématu modulu a `UseModuleSchema` (v `OnModelCreating`) tam dá i tabulky.
  Moduly zůstávají oddělené („schéma = vlastní databáze“), Web a Inventory.Api migrují nezávisle.
  SQLite schémata nezná – jeho model, migrace i produkční soubory se nezměnily.
- **Dvě sady migrací:** SQLite v `MiniShop.Infrastructure` (beze změny), PostgreSQL v novém projektu
  `MiniShop.Infrastructure.PostgreSql` (`InitialPostgres` pro 8 kontextů). Který projekt EF použije, určuje
  `MigrationsAssembly` v `UseModuleDatabase`. Projekt referencují Web, Inventory.Api a testy, aby DLL ležela vedle nich.
- **Rozdíly databází:**
  - časy – PostgreSQL odmítne `DateTime` bez `Kind = Utc`; `UseUtcDateTimes` je při zápisu označí jako UTC
    (všechny časy v MiniShopu jsou UTC, SQLite Kind neukládá),
  - souběh – unikátní index hlásí PostgreSQL jako SQLSTATE `23505`, `ModuleDbContext` ho stejně jako chybu SQLite 2067
    převede na `ConcurrencyConflictException`,
  - baseline (`DatabaseMigrator`, `sqlite_master`) jen pro SQLite – PostgreSQL databáze vznikají rovnou migracemi.
- **Lokálně:** AppHost přidá persistentní kontejner `postgres` (volume `minishop-postgres-data`, heslo v user secrets)
  s databází `minishop` a obě služby přepne na PostgreSQL. SQLite přes `-- --Database:Provider=Sqlite`.
  Compose má službu `postgres` jen v profilu `postgres` – bez profilu (produkce) nevznikne.

## Nové soubory

| Soubor | Co |
| --- | --- |
| `src/MiniShop.Infrastructure/Persistence/ModuleDatabase.cs` | `DatabaseProvider`, `ModuleDatabase`, `UseModuleDatabase`, `UseModuleSchema`, `UseUtcDateTimes` |
| `src/MiniShop.Infrastructure.PostgreSql/` | projekt s migracemi PostgreSQL (`*/Migrations/…_InitialPostgres`) a `PostgresDesignTimeFactories.cs` |
| `tests/MiniShop.Tests/Infrastructure/PostgresMigrationTests.cs` | zapomenutá migrace PostgreSQL, tabulky jen ve schématu modulu, SQLite beze schématu, konfigurace |
| `docs/prirucka/architektura/databaze.md` | stránka příručky Databáze |

## Změněné soubory

- `MiniShop.Infrastructure.csproj` – balíček `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `*/…Infrastructure.cs` všech 8 modulů – parametr `ModuleDatabase` místo connection stringu (implicitní převod
  ze `string` = SQLite, takže testy se nemusely měnit), `UseModuleDatabase`.
- `*DbContext.cs` – konstanta `Schema`; `ModuleDbContext`, `JourneysDbContext`, `AccountsDbContext` – schéma a UTC časy;
  `ModuleDbContext` – unikátní index v PostgreSQL.
- `Persistence/DatabaseMigrator.cs` – baseline jen pro SQLite.
- `MiniShop.Web/Program.cs`, `MiniShop.Inventory.Api/Program.cs`, `AccountsServiceCollectionExtensions.cs` – `ModuleDatabase.FromConfiguration`.
- `MiniShop.AppHost` – `Aspire.Hosting.PostgreSQL`, `AddPostgres("postgres").AddDatabase("minishop")`, `WithModuleDatabase`.
- `deploy/compose.yaml`, `deploy/.env.example` – služba `postgres` (profil), `DATABASE_PROVIDER`, `POSTGRES_PASSWORD`.
- Oba `Dockerfile` – `COPY` nového `.csproj`.
- `MiniShop.slnx`, reference Web/Inventory.Api/Tests.
- Příručka: vrstvy, sdílené jádro (migrace), knihovny, běhové prostředí, lokální vývoj, historie.

## Rozhodnutí

- **Schéma, ne osm databází** – hranice modulů zůstávají, ale server, heslo a záloha jsou jedny.
- **Druhá sada migrací v samostatném projektu** – migrace jsou specifické pro databázi (typy sloupců); SQLite migrace
  a produkční baseline se tím vůbec nedotkly. Cena: změna schématu = migrace dvakrát (hlídají oba testy).
- **Schéma z historie migrací** – `UseModuleSchema` čte schéma z nastavení `MigrationsHistoryTable`, takže je zapsané
  jen na jednom místě (registrace / design-time továrna).
- **Převod časů na UTC místo `timestamp without time zone`** – Npgsql by u `timestamp` odmítl naopak UTC časy;
  `timestamptz` je správný typ pro okamžik v čase.
- **Aspire výchozí PostgreSQL, Compose výchozí SQLite** – lokální vývoj má jet na budoucí produkční databázi, Compose
  zrcadlí produkci.
- **Produkce beze změny** – žádný nový secret, `deploy.sh` ani záloha se nemění. PostgreSQL v produkci až krok 3.

## Známá omezení

- Testy aplikace běží dál nad SQLite v paměti; nad PostgreSQL se zatím hlídají jen migrace (krok 2: Testcontainers).
- Lokální data ze SQLite se do PostgreSQL nepřenesou (AppHost začne s prázdnou databází a seedem skladu);
  převod dat přijde v kroku 3.
- Workery dál počítají s jednou instancí (krok 5).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
```

Naživo (ověřeno bez Aspire): PostgreSQL v Dockeru, obě služby s `Database__Provider=Postgres` a
`ConnectionStrings__minishop` → `/health` obou služeb Healthy, v databázi 8 schémat s vlastní `__EFMigrationsHistory`,
sklad naseedoval produkty a kopie katalogu v `ordering.CatalogProducts` se naplnila přes RabbitMQ.

## Vyzkoušejte sami

1. `dotnet run --project src/MiniShop.AppHost` – v dashboardu přibude `postgres` a `minishop`. Výloha má produkty ze seedu.
2. Připojte se do databáze (connection string v detailu `minishop` v dashboardu, nebo `docker exec -it <postgres-…> psql -U postgres -d minishop`)
   a podívejte se na schémata: `\dn`, tabulky objednávek `\dt ordering.*`.
3. Udělejte objednávku a najděte ji: `select "Id", "Status" from ordering."Orders";` a zprávy v `ordering."OutboxMessages"`.
4. Zpátky na SQLite: `dotnet run --project src/MiniShop.AppHost -- --Database:Provider=Sqlite` – původní data jsou tam.
5. Compose s PostgreSQL: v `deploy/.env` `DATABASE_PROVIDER=Postgres` + `POSTGRES_PASSWORD`, pak
   `docker compose --profile postgres up -d --build`.
