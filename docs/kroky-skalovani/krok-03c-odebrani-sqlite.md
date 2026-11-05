# Škálování krok 3C: odebrání SQLite z kódu (contract)

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Fáze *expand* (kroky 1–3B) přidala PostgreSQL vedle SQLite, aby šlo
bezpečně přepnout. Produkce běží na PostgreSQL od 4. 11. 2026 a obnova ze zálohy je ověřená (krok 4) – SQLite už jen
přidávalo práci: každá změna schématu = dvě migrace, testy dvakrát, výhybky podle providera.

## Co se změnilo

- **Provider SQLite pryč:** balíček `Microsoft.EntityFrameworkCore.Sqlite`, `DatabaseProvider` a `Database:Provider`,
  chyba SQLite 2067 v `ModuleDbContext`, baseline v `DatabaseMigrator` (`sqlite_master`).
  `ModuleDatabase` je teď jen connection string `ConnectionStrings:minishop`.
- **Migrace:** 68 souborů migrací SQLite smazáno, migrace PostgreSQL přestěhované z `MiniShop.Infrastructure.PostgreSql`
  zpět do `MiniShop.Infrastructure/<Modul>/Migrations` (namespace `MiniShop.Infrastructure.<Modul>.Migrations`),
  projekt zrušen (řešení, reference, `COPY` v Dockerfile). Design-time továrny v `DesignTimeDbContextFactories.cs`.
  **Jména migrací se nezměnila** – produkce je má v historii.
- **Převodníky `SpecifyKind` pro jednotlivé časy** (34× „SQLite neukládá DateTimeKind“) pryč – UTC zajišťuje jednotně
  `UseUtcDateTimes` (dřív ho per-property převodníky u těchto časů přebíjely).
- **Jednorázový nástroj převodu** `migrate-to-postgres` (`MigrateToPostgresCommand`, `SqliteToPostgresCopier`) a jeho testy
  odstraněny – zůstávají v historii gitu.
- **Konfigurace:** `ConnectionStrings:<Modul>` z `appsettings.json` a 8 proměnných `ConnectionStrings__…="Data Source=/data/….db"`
  z Dockerfile pryč; `appsettings.Development.json` obou služeb mají `ConnectionStrings:minishop` na lokální kontejner
  `minishop-postgres` (spuštění bez Aspire). AppHost bez volby `--Database:Provider=Sqlite`. Compose, `.env.example`
  a `production.env` bez `DATABASE_PROVIDER`.
- **Zálohy:** `backup.py` už nezálohuje SQLite soubory (snímek `pg_dump`, klíče, obrázky); `restore.sh` obnovuje PostgreSQL vždy.
- **Testy:** `InMemoryDatabase`, přepínač `MINISHOP_TEST_DATABASE` a testy baseline SQLite pryč – všechny testy běží nad
  PostgreSQL (`TestDatabases` = kopie šablony pro každý test). Nový test `Migrace_ProdukcniHistorieZustavaPlatna`
  hlídá jména migrací, která má produkce v historii.
- **CI:** jediný job „Build a testy“ (nad PostgreSQL), job „Testy PostgreSQL“ zrušen a odebrán z povinných checků rulesetu
  `main-protection`.
- **Dokumentace:** jména souborů `ordering.db` … nahrazena schématy v celé příručce, stránka Databáze přepsaná na
  současný stav (s krátkou historií přechodu), Lokální vývoj (kontejner `minishop-postgres`), Zálohy, Kontejnery, CI,
  katalog třetích stran; CLAUDE.md.

## Nalezená chyba: poslední admin při souběhu

Při úklidu komentářů „kvůli SQLite“ se ukázalo, že pravidlo **„vždy zůstane aktivní admin“** (`AccountAdministration`)
stálo na tom, že SQLite pustí k zápisu jen jednu transakci. V PostgreSQL běží transakce souběžně: dva admini, kteří
se ve stejnou chvíli navzájem vracejí na usera, oba prošli kontrolou („ten druhý je ještě admin“) a obchod zůstal bez
admina. Nový test `DvaAdminiNaraz_NavzajemSeOdeberou_VzdyZustaneAdmin` chybu před opravou zachytil hned v prvním kole.

Oprava: na začátku transakce `pg_advisory_xact_lock(hashtext('minishop.account-administration'))` – změny správy uživatelů
proběhnou postupně, druhá počká a pak už vidí výsledek první (a odmítne se). Zámek drží transakce, při commitu
se sám uvolní. Produkce chybou ohrožená nebyla (jeden admin), oprava jde s tímto krokem.

Ostatní místa „nejdřív kontrola, pak zápis“ (kupony, pokladna, košík, ságy, hodnocení) chrání concurrency token nebo
unikátní index – souběh zachytí databáze i v PostgreSQL (test `Kupon_PosledniPouzitiNaraz_ProjdeJenJednaObjednavka`).

## Ověření přesunu migrací

Nejrizikovější část: kdyby EF po přesunu migrace „nepoznal“, chtěl by je v produkci spustit znovu. Ověřeno předem:

1. **Starým kódem** (před 3C) založená databáze v kontejneru – stejná historie migrací jako produkce
   (`…_InitialPostgres` ×8 + `…_UncategorizedCategory`).
2. **Novým kódem** `dotnet ef migrations list --connection …` pro všech 8 kontextů → žádná migrace `(Pending)`.
3. Test `Migrace_ProdukcniHistorieZustavaPlatna` hlídá totéž natrvalo.

## Rozhodnutí

- **Migrace zpět do `MiniShop.Infrastructure`** – bez SQLite druhý projekt nemá důvod; nová migrace je zase jeden příkaz.
- **Nástroj převodu smazat**, ne nechat „pro jistotu“ – převod je hotový, kód by stárnul a mátl. Je v historii gitu.
- **Řazení a součty v paměti** (české názvy, přehled tržeb) zůstávají – fungují; přesun do SQL je samostatné vylepšení.
- **Testy jen nad PostgreSQL** – vědomá cena: lokálně je potřeba Docker (pro AppHost byl potřeba už dřív).

## Známá omezení

- Staré SQLite soubory (`/data/*.db`) na VM zůstávají ve volumes, dokud je ručně nesmažeme (po první záloze bez nich).
- Zálohy z doby SQLite (do 4. 11. 2026) mají v bucketu ještě několik dní SQLite soubory – lifecycle je smaže; obnovit
  z nich data nejde (jejich snímek PostgreSQL je prázdný – `restore.sh` je odmítne).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx        # potřebuje Docker
```

## Po nasazení (VM)

1. Ruční záloha `sudo systemctl start minishop-backup` – v logu jen `minishop.pgdump`, `keys/`, `images/` (žádné `.db`).
2. Smazat staré SQLite soubory z volumes `web-data` a `inventory-data` (`*.db`, `*.db-wal`, `*.db-shm`, `*.before-restore-*`).

## Vyzkoušejte sami

1. `dotnet ef migrations add Zkouska --project src/MiniShop.Infrastructure --context OrderingDbContext --output-dir Ordering/Migrations`
   – jedna migrace (dřív dvě). Pak `dotnet ef migrations remove …` se stejnými parametry.
2. Spuštění bez Aspire podle [Lokální vývoj](../prirucka/nasazeni/lokalne.md) – kontejner `minishop-postgres`.
