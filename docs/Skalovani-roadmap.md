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
| 3. Převod produkčních dat | Příkaz `migrate-to-postgres` (SQLite → PostgreSQL, ověření počtů), PostgreSQL v produkčním Compose, secret `POSTGRES_PASSWORD`, odstávka, přepnutí, odebrání SQLite providera. | Data (objednávky, účty, hodnocení) se nesmí ztratit. |
| 4. Zálohy a obnova | `pg_dump` do OCI bucketu, `restore.sh` pro PostgreSQL, cvičná obnova. | Dnešní zálohy kopírují SQLite soubory. |
| 5. Více instancí | Leader lease přes PostgreSQL advisory lock (workery jen na vedoucí instanci), `web` ve dvou kopiích za Caddy, zkouška výpadku jedné kopie. | Teprve teď má víc instancí smysl; consumery RabbitMQ jsou připravené (competing consumers + inbox). |
| 6. Druhá VM (volitelně) | Aplikace i na druhém stroji, databáze a RabbitMQ na prvním. | Skutečné škálování přes stroje. |
