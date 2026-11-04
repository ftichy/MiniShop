# Škálování krok 3A: příprava převodu dat do PostgreSQL

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Krok 3 je rozdělený: **3A** připraví všechno potřebné a nasadí
to (produkce dál běží na SQLite), **3B** je samotné přepnutí s krátkou odstávkou, **3C** odebere SQLite z kódu.

## Jak to funguje

- **Příkaz `migrate-to-postgres`** v obou službách (každá převádí moduly ze svých volumes):
  `docker compose run --rm --no-deps inventory-api migrate-to-postgres` a totéž pro `web`.
  Zdroj = `ConnectionStrings:<Modul>` (SQLite), cíl = `ConnectionStrings:minishop` (PostgreSQL) – nezávisle na
  `Database:Provider`, takže jde spustit, dokud služby ještě jedou na SQLite.
- **`SqliteToPostgresCopier`** pro jeden modul:
  1. migrace obou databází na nejnovější schéma,
  2. pojistky (viz níže),
  3. `TRUNCATE` cílových tabulek (data z migrací – „Nezařazeno“, role – se nahradí originálem ze SQLite),
  4. kopie řádků se **stejnými ID** v pořadí podle cizích klíčů; typy podle cílového sloupce (SQLite má GUID, čas
     a desetinná čísla jako text, `bool` jako 0/1),
  5. `setval` čítačů identity – další objednávka dostane další číslo,
  6. porovnání počtů řádků, značka `__MiniShopDataMigration` a COMMIT. Cokoli selže → ROLLBACK, v PostgreSQL nic nezůstane.
  Řídí se relačním modelem EF – nová tabulka nebo sloupec se převede bez úpravy kódu.
- **Pojistky proti přepsání nových dat:**
  - značka převodu: podruhé jen s `--replace` (vědomě, např. po návratu na SQLite) – zachytí i pouhé úpravy řádků
    (změna hesla, stav objednávky), které by kontrola klíčů nepoznala,
  - řádky s klíčem, který v SQLite není (zapsala je aplikace nad PostgreSQL), převod odmítne vždy, i s `--replace`.
- **PostgreSQL v produkci:** služba `postgres` (`postgres:18-alpine`) v `compose.yaml` běží vždy, volume `postgres-data`,
  port jen `127.0.0.1:5432` na VM, `mem_limit: 1g`. Služby na ni přepne až `DATABASE_PROVIDER=Postgres` (krok 3B).
- **Secret `POSTGRES_PASSWORD`:** GitHub `production` → `deploy.yml` (stdin) → `deploy.sh` (tvar 24–128 písmen/číslic,
  `.env`). Změnu hesla `deploy.sh` nejdřív provede v běžící databázi (`ALTER USER` přes lokální socket) – jako u RabbitMQ.
- **Zálohy:** `backup.sh` nejdřív `pg_dump --format=custom` v kontejneru databáze, `backup.py` ho přibalí jako
  `minishop.pgdump` do noční zálohy. Obnova (`pg_restore`) a cvičná obnova jsou krok 4. Přesunuto sem, aby po přepnutí
  nebyla ani jedna noc bez zálohy nových dat.
- **Health check databáze:** `database-ordering` (Web) a `database-inventory` (sklad) – `CanConnectAsync`, jinak Unhealthy
  → `/health` 503 → UptimeRobot, `up --wait` při nasazení selže a vrátí předchozí verzi.
- **Npgsql bez Kerberu:** `GssEncryptionMode=Disable` – v image chybí `libgssapi_krb5` a Npgsql by při každém připojení
  vypsal chybu (našla to generálka v Compose).
- **PostgreSQL 18 všude:** Compose, testy (Testcontainers) i Aspire (tam už byla 18).

## Nové soubory

| Soubor | Co |
| --- | --- |
| `src/MiniShop.Infrastructure/Persistence/SqliteToPostgresCopier.cs` | převod jednoho modulu, pojistky, `TableCopyResult` |
| `src/MiniShop.Infrastructure/MigrateToPostgresCommand.cs` | příkaz `migrate-to-postgres [--replace]`, `ModuleDataMigration` |
| `src/MiniShop.Infrastructure/Observability/DatabaseHealthCheck.cs` | health check dostupnosti databáze |
| `tests/MiniShop.Tests/Infrastructure/PostgresDataMigrationTests.cs` | převod 9 scénářů + účtů, porovnání obsahu tabulek, pojistky |
| `tests/MiniShop.Tests/TestDoubles/PostgresFactAttribute.cs`, `PostgresTheoryAttribute.cs` | testy jen ve variantě PostgreSQL |

## Změněné soubory

- `MiniShop.Web/Program.cs`, `MiniShop.Inventory.Api/Program.cs` – příkaz se seznamem modulů služby.
- `Persistence/ModuleDatabase.cs` – `GssEncryptionMode=Disable`.
- `OrderingInfrastructure.cs`, `InventoryInfrastructure.cs` – `DatabaseHealthCheck`.
- `OrderServiceTests` – druhý konstruktor s `TestDatabases` (scénáře nad SQLite pro test převodu); `TestDatabases(provider)`.
- `deploy/compose.yaml` (postgres vždy, verze 18, povinné heslo), `deploy/.env.example`, `deploy/deploy.sh` (secret, rotace),
  `.github/workflows/deploy.yml` (secret), `deploy/backup.sh` + `backup.py` (`pg_dump`).
- `PostgresTestServer` – `postgres:18-alpine`.
- `docs/secrets.md` (R11), příručka: Databáze (převod), Zálohy, Kontejnery, GitHub secrets, běhové prostředí, historie;
  `Skalovani-roadmap.md` (3A/3B/3C, krok 4 jen obnova), CLAUDE.md.

## Rozhodnutí

- **Převod podle modelu EF, ne ručně psané SQL na tabulku** – 41 tabulek v 8 modulech, nové přibývají; model zná
  tabulky, sloupce, cílové typy i cizí klíče.
- **Stejná ID** – čísla objednávek zná zákazník (e-maily, odkazy), ID jsou i ve zprávách mezi moduly a v inboxech.
- **Jedna transakce na modul a kontrola počtů** – modul je převedený celý, nebo vůbec.
- **Odstávka místo souběžného zápisu** – služby stojí, nic se nemění pod rukama; jednodušší a bezpečnější než
  synchronizace dvou databází.
- **Generálka nanečisto** v Compose s produkčními image (oddělený projekt): SQLite → převod → opakování odmítnuto →
  přepnutí → Healthy. Našla hlášku o Kerberu.
- **`pg_dump` už teď** (původně krok 4) – jinak by noční záloha po přepnutí kopírovala jen SQLite soubory, které se už nemění.

## Známá omezení

- Obnova z `minishop.pgdump` zatím není v `restore.sh` (krok 4) – do té doby ručně `pg_restore`.
- SQLite i PostgreSQL zatím v kódu i v záloze (contract = krok 3C).
- Lokální `deploy/.env` potřebuje nově `POSTGRES_PASSWORD` (Compose bez něj nespustí).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
$env:MINISHOP_TEST_DATABASE="Postgres"; dotnet test MiniShop.slnx; Remove-Item Env:MINISHOP_TEST_DATABASE
```

## Před sloučením (pořadí je důležité)

1. Přidat secret `POSTGRES_PASSWORD` do GitHub environmentu `production` (jinak Deploy skončí chybou „Chybí secret“).
2. Nainstalovat nový `deploy/deploy.sh` na VM (`/usr/local/bin/minishop-deploy`) – starý by neznámý klíč
   `POSTGRES_PASSWORD` odmítl.
3. Sloučit PR → CI → nasazení. Na VM poběží prázdný PostgreSQL, služby dál na SQLite.
4. Ruční záloha `sudo systemctl start minishop-backup` – v bucketu záloha s `minishop.pgdump`.

## Vyzkoušejte sami

1. Lokálně v `deploy/.env` doplňte `POSTGRES_PASSWORD`, `docker compose up -d --build` (služby na SQLite), pak
   `docker compose stop web inventory-api`, `docker compose run --rm --no-deps web migrate-to-postgres` – výpis tabulek
   a počtů. Spusťte ho podruhé – odmítne (značka). V `.env` `DATABASE_PROVIDER=Postgres`, `docker compose up -d`.
2. `docker compose exec postgres psql -U minishop -d minishop -c '\dn'` – schémata modulů;
   `select * from ordering."__MiniShopDataMigration";` – kdy proběhl převod.
3. `/health` – ve výpisu kontrol (Aspire dashboard) přibyla `database-ordering`.
