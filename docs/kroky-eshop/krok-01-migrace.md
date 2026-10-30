# E-shop krok 1: Migrace databází

Databáze `ordering.db`, `inventory.db` a `journeys.db` už nevznikají přes `EnsureCreated`, ale přes **EF Core migrace** –
stejně jako `users.db` od účtů kroku 6. Změna schématu (košík, adresa, historie stavů v dalších krocích) tak
nevyžaduje smazat databázi a produkční objednávky zůstanou. Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md).

## Jak to funguje

Přirovnání: `EnsureCreated` je stavba domu podle aktuálního plánu – jde to jen jednou, na prázdném pozemku.
Migrace jsou **stavební deník**: očíslované úpravy „z verze N na N+1“ a v domě visí lístek, které už jsou hotové
(tabulka `__EFMigrationsHistory`). Při každém startu se dodělá jen to, co na lístku chybí.

```
 start služby ─► …DatabaseInitializer.MigrateAsync(dbContext)
                   └─► DatabaseMigrator.MigrateAsync(dbContext, značková tabulka)
                         je __EFMigrationsHistory?  ano ───────────────────────────┐
                         ne: existuje značková tabulka (Orders/Products/…)?        │
                               ano = stará DB z EnsureCreated (produkce)           │
                                     └─► zapiš Initial… jako PROVEDENOU (baseline) │
                               ne  = prázdná DB ──────────────────────────────────┤
                         Database.MigrateAsync() ◄─────────────────────────────────┘
                           spustí jen migrace, které v historii chybí
```

**Baseline** je jediné „kouzlo“: produkční databáze tabulky už má, ale nemá historii. Bez baseline by `Migrate` spustil
`InitialOrdering` znovu a spadl na `CREATE TABLE Orders` (tabulka existuje). Baseline je správně jen tehdy, když
první migrace vytváří **přesně** schéma produkční databáze – to hlídá test a jednorázová kontrola produkce (níže).

| Databáze | Služba | Značková tabulka | Migrace |
| --- | --- | --- | --- |
| `ordering.db` | Web | `Orders` | `Infrastructure/Ordering/Migrations` (`InitialOrdering`) |
| `journeys.db` | Web | `JourneyEvents` | `Infrastructure/Journeys/Migrations` (`InitialJourneys`) |
| `inventory.db` | Inventory.Api | `Products` | `Infrastructure/Inventory/Migrations` (`InitialInventory`) |
| `users.db` | Web | `AspNetUsers` | `Infrastructure/Accounts/Migrations` (od účtů kroku 6) |

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Persistence/DatabaseMigrator.cs` | Sdílené jádro: baseline + `Migrate` pro libovolný DbContext (o modulech neví, dostane jméno značkové tabulky) |
| `src/MiniShop.Infrastructure/{Ordering,Inventory,Journeys}/…DesignTimeDbContextFactory.cs` | Jen pro nástroj `dotnet ef` – vytvoří DbContext bez spuštění služby |
| `src/MiniShop.Infrastructure/{Ordering,Inventory,Journeys}/Migrations/*` | Vygenerované migrace `Initial…` + snapshot modelu |
| `tests/MiniShop.Tests/Infrastructure/DatabaseMigrationTests.cs` | Hlídá zapomenutou migraci, shodu schématu migrace × `EnsureCreated`, baseline staré DB bez ztráty dat, opakované spuštění |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Ordering/OrderingDatabaseInitializer.cs`, `Journeys/JourneysDatabaseInitializer.cs`, `Inventory/InventoryDatabaseInitializer.cs` | `MigrateAsync` místo `EnsureCreatedAsync` (seed produktů skladu zůstává) |
| `Accounts/AccountsDatabaseInitializer.cs` | Vlastní kód baseline přesunut do sdíleného `DatabaseMigrator` |
| `tests/…/OrderServiceTests.cs`, `OutboxTests.cs`, `IdempotentHandlerTests.cs`, `JourneyServiceTests.cs` | Testovací DB vznikají migracemi (testy tím ověřují i migrace) |
| `docs/prirucka/…` | Sdílené jádro (komponenta + sekce Migrace databází), lokální spuštění, Accounts, knihovny, historie |
| `CLAUDE.md` | Pravidlo „při změně schématu smazat DB“ → „nová migrace“; tabulka Stav e-shopu |

## Rozhodnutí v tomto kroku

- **Migrace pro všechny databáze, i `journeys.db`** – sledování průběhu objednávek má hodnotu (přehrání) a krok 4 ho mění.
- **Jeden sdílený migrátor** místo kopie kódu z Accounts v každém modulu – sdílené jádro (`Persistence`) dostane jen
  `DbContext` a jméno tabulky, o modulech nic neví (DRY, hranice modulů beze změny).
- **Značková tabulka místo „DB není prázdná“** – prázdná DB po pádu uprostřed vytváření by jinak vypadala jako stará.
- **Migrace při startu služby** (ne samostatným příkazem v pipeline) – jedna instance každé služby, SQLite, jednoduché;
  stejné jako `users.db`. Při více instancích by bylo potřeba migrovat zvlášť (zámek, krok v CD).
- **Test shody schématu porovnává sloupce (typ, NOT NULL, PK) a SQL indexů** – ne celé `CREATE TABLE`, které se mezi
  `EnsureCreated` a migrací liší jen formátem.
- **Kontrola produkce jen nad schématem** – z VM odchází prázdná kopie struktury, žádné osobní údaje.

## Známá omezení

- Baseline předpokládá, že stará DB má schéma **dnešního** modelu. Lokální databáze z dob před účty krokem 3
  (bez `CustomerId`) by migrací „prošla“, ale aplikace by na ní padala – takovou je potřeba smazat (naposledy).
- Migrace se nevracejí automaticky – rollback nasazení na starší image nechá v DB novější schéma. U přidávání
  sloupců/tabulek to nevadí (starý kód je ignoruje); u mazání a přejmenování je potřeba postup ve dvou nasazeních.
- SQLite neumí většinu `ALTER TABLE` – EF to řeší přestavbou tabulky (nová tabulka, kopie dat, přejmenování).
  U velkých tabulek to trvá; pro MiniShop zanedbatelné.
- Starý soubor `src/MiniShop.Web/minishop.db` (z doby před krokem 10A) nikdo nepoužívá – můžeš ho smazat.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. Lokální databáze se **nemažou**. Při prvním startu je v logu Webu i skladu jen ověření historie – žádné
   `Applying migration 'Initial…'` (baseline ji zapsal jako provedenou).
2. Objednávky, produkty a průběhy z dřívějška jsou na místě; nová objednávka projde ságou.
3. V `ordering.db` (např. DB Browser for SQLite) je tabulka `__EFMigrationsHistory` s řádkem `…_InitialOrdering`.
4. Smaž (jednou, na zkoušku) `journeys.db` a spusť znovu – vznikne celá z migrace (`Applying migration '…_InitialJourneys'`).

### Produkce

1. **Před sloučením – kontrola schématu:** z produkčních DB se na VM vytáhne jen schéma (python v kontejneru
   `oci-cli`, volumes `minishop_web-data` a `minishop_inventory-data`), stáhne se na PC a porovná s migrací
   (výsledek „OK – schéma odpovídá migraci, baseline proběhl“ pro všechny tři DB).
2. Ruční záloha: `sudo systemctl start minishop-backup`.
3. Sloučit PR → CD nasadí. V logu Webu/skladu bez chyb, `/health` = Healthy, objednávky na webu zůstaly.
4. Nic dalšího – žádný secret, `deploy.sh` ani `backup.py` se nemění.

## Vyzkoušejte sami

- Přidejte do `CatalogProduct` vlastnost `string? Description` a spusťte testy – `Model_NemaZmenyBezMigrace(Ordering)`
  selže. Vytvořte migraci
  `dotnet ef migrations add CatalogDescription --project src/MiniShop.Infrastructure --context OrderingDbContext --output-dir Ordering/Migrations`,
  prohlédněte si `Up`/`Down` a spusťte Web – sloupec přibude, data zůstanou. Pak vše vraťte
  (`dotnet ef database update InitialOrdering …` a `dotnet ef migrations remove …`).
- Zkuste vlastnost **přejmenovat** a podívejte se, co EF vygeneruje (`RenameColumn`, nebo `DropColumn` + `AddColumn`?).
- Ve `DatabaseMigrator` zakomentujte baseline a spusťte Web nad starou DB – jakou chybu dostanete?
