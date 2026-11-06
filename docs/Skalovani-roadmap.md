# MiniShop: škálování na víc instancí

4. 11. 2026 · František

Cíl: aby MiniShop mohl běžet ve **víc instancích** (a později na víc strojích). Dnes tomu brání tři věci:

- **SQLite je soubor na disku jedné VM** – druhý stroj k němu nemá přístup a i dva procesy na jednom stroji se
  přetahují o zápis (SQLite má jediného zapisovatele).
- **Workery počítají s jednou instancí** (pošťák outboxu, timeouty ság a vratek, připomínky košíku, úklid zpráv) –
  ve dvou kopiích by práci dělaly dvakrát (dvojí e-mail, dvojí připomínka).
- **Zálohy a obnova** kopírují SQLite soubory.

Pořadí: nejdřív serverová databáze (PostgreSQL), pak víc instancí. Rozpočet zůstává 0 Kč – PostgreSQL poběží
jako kontejner na stávající Always Free VM (managed PostgreSQL v OCI zdarma není). Souhrny kroků do `docs/kroky-skalovani/`.

## Rozhodnutí

1. **Jeden PostgreSQL, schéma pro každý modul** (`ordering`, `inventory`, `journeys`, `notifications`, `payments`,
   `reporting`, `reviews`, `accounts`), každý modul vlastní tabulku historie migrací. Hranice modulů zůstávají
   („schéma = vlastní databáze“), server i záloha jsou jen jedny.
2. **Testy proti skutečnému PostgreSQL** (Testcontainers lokálně i v CI) – SQLite se chová jinak (časy, řazení,
   souběh, chyby unikátních indexů). Čisté doménové testy beze změny.
3. **Převod produkčních dat s krátkou plánovanou odstávkou** (minuty), bez souběžného provozu obou databází.
4. **Expand → contract**: nejdřív aplikace umí oba providery (produkce dál na SQLite), přepnutí produkce až s
   převodem dat, SQLite se odstraní nakonec.

## Kroky

| Krok | Co | Proč |
| --- | --- | --- |
| 1. PostgreSQL vedle SQLite | Provider podle konfigurace (`Database:Provider` = `Sqlite` / `Postgres`), Npgsql, schéma pro každý modul, nové počáteční migrace pro PostgreSQL v samostatném projektu, překlad chyb (unikátní index → `ConcurrencyConflictException`), PostgreSQL v Aspire a v Compose (profil). Produkce dál na SQLite. | Aplikace umí obě databáze – přepnout jde, až bude převod dat. |
| 2. Testy proti PostgreSQL | Testy s databází přes Testcontainers (lokálně Docker, v CI služba runneru), kontrola zapomenutých migrací pro oba providery. | Testovat proti databázi, která poběží v produkci. |
| 3A. Příprava převodu | Příkaz `migrate-to-postgres` (SQLite → PostgreSQL podle modelu EF, stejná ID, počty řádků, značka proti opakování), PostgreSQL 18 v produkčním Compose (prázdný), secret `POSTGRES_PASSWORD`, `pg_dump` do noční zálohy, health check databáze. Produkce dál na SQLite. | Data (objednávky, účty, hodnocení) se nesmí ztratit – převod se nejdřív ověří nanečisto. |
| 3B. Přepnutí produkce | Plánovaná odstávka: ruční záloha → zastavit služby → `migrate-to-postgres` → `DATABASE_PROVIDER=Postgres` v `production.env` → smoke test. SQLite soubory zůstávají jako cesta zpět. | Krátká odstávka místo souběhu dvou databází. |
| 3C. Odebrání SQLite (contract, hotovo) | Až produkce pár dní běží: pryč SQLite provider, migrace SQLite, baseline, SQLite v `backup.py`; testy výchozí nad PostgreSQL; staré `.db` smazat po další záloze. | Jedna databáze = méně kódu a míň míst k chybě. |
| 4. Obnova ze zálohy (hotovo) | `restore.sh` pro PostgreSQL (`pg_restore` z `minishop.pgdump`), cvičná obnova. (`pg_dump` do zálohy přibyl už v kroku 3A, aby nová data nezůstala nezálohovaná.) | Záloha bez vyzkoušené obnovy není záloha. |
| 5. Více instancí (hotovo) | Leader lease přes PostgreSQL advisory lock (`LeaderElection`, workery s časovačem jen na vedoucí instanci přes `LeaderOnlyHostedService`), `web` ve dvou kopiích za Caddy (`WEB_REPLICAS`, `dynamic a` + round robin), log pro každou kopii, zkouška výpadku jedné kopie. Souhrn [krok-05](kroky-skalovani/krok-05-vice-instanci.md). | Teprve teď má víc instancí smysl; consumery RabbitMQ jsou připravené (competing consumers + inbox). |
| 5B. Postupné nasazení (hotovo) | `deploy/rolling-update.sh`: nové kopie vedle starých (`--scale --no-recreate`), po healthchecku staré pryč, nezdravé nové se zahodí; infrastruktura zvlášť. Caddy krátké vyřazení + `dial_timeout`. Souhrn [krok-05b](kroky-skalovani/krok-05b-postupne-nasazeni.md). | Nasazení bez výpadku – dvě kopie Webu to umožňují. |
| 6. Druhá VM (volitelně) | Aplikace i na druhém stroji, databáze a RabbitMQ na prvním. | Skutečné škálování přes stroje. |
