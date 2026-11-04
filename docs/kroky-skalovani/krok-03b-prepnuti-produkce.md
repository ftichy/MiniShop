# Škálování krok 3B: přepnutí produkce na PostgreSQL

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Navazuje na [krok 3A](krok-03a-priprava-prevodu.md), který připravil
příkaz `migrate-to-postgres`, prázdný PostgreSQL 18 na VM, secret `POSTGRES_PASSWORD` a `pg_dump` v záloze.

## Jak proběhlo (4. 11. 2026, odstávka ~25 minut)

Předem: PR #61 (`DATABASE_PROVIDER=Postgres` v `deploy/production.env`) se zeleným CI, nesloučený.

| Krok | Příkaz (na VM, `/opt/minishop`, jako `deploy`) | Výsledek |
| --- | --- | --- |
| 1. Zastavit služby | `docker compose stop web inventory-api` | web nedostupný (Caddy 502), `postgres`, `rabbitmq`, `caddy` běží |
| 2. Záloha | `sudo systemctl start minishop-backup` | `minishop-20261104T205128Z.tar.gz` – přesný stav SQLite před převodem |
| 3. Převod | `docker compose run --rm --no-deps inventory-api migrate-to-postgres && docker compose run --rm --no-deps web migrate-to-postgres` | 1 702 řádků ve 41 tabulkách, všechny počty souhlasí |
| 4. Přepnutí | sloučit PR #61 → CI → Deploy (`up --wait`) | služby s `Database__Provider=Postgres`, `/health` Healthy |
| 5. Kontrola | `printenv Database__Provider`, SQL přes `psql`, prohlížeč | 19 objednávek, převod 20:52:43 UTC, objednávky, přehled tržeb i nová objednávka v pořádku |

Výsledek převodu (souvislosti mezi moduly sedí: 19 objednávek = 19 ság = 19 řádků tržeb = 19 kopií nákupů,
35 položek = 35 řádků tržeb = 35 nakoupených produktů):

| Modul | Řádků | Hlavní tabulky |
| --- | ---: | --- |
| Inventory | 191 | 7 produktů, 4 kategorie, 34 rezervací |
| Ordering | 693 | 19 objednávek, 35 položek, 67 změn stavu, 2 vratky, 1 kupon |
| Journeys | 543 | události formulářů |
| Notifications | 43 | 6 příjemců |
| Payments | 22 | 6 plateb |
| Reporting | 122 | 19 objednávek v přehledu tržeb |
| Reviews | 78 | 19 nákupů, 1 hodnocení |
| Accounts | 10 | 2 účty, 2 role, 4 záznamy auditu |

## Změněné soubory

- `deploy/production.env` – `DATABASE_PROVIDER=Postgres`.
- `deploy/backup.py` – velikost `minishop.pgdump` v bajtech (prázdná databáze měla jen pár set bajtů → „0 kB“).
- Příručka (Databáze, historie), CLAUDE.md, roadmapa.

## Rozhodnutí

- **Záloha až po zastavení služeb** – snímek SQLite je přesně stav, ze kterého se převádělo.
- **PR připravený a otestovaný předem**, sloučený až po převodu – během odstávky se jen čeká na CI a nasazení.
- **Sklad převeden dřív než Web a `&&`** – při chybě skladu se Web nespustí; každý modul je navíc v jedné transakci.
- **SQLite soubory zůstávají** ve volumes a v záloze až do kroku 3C.

## Cesta zpět (kdyby bylo potřeba)

PR s `DATABASE_PROVIDER=Sqlite` v `deploy/production.env` → služby pojedou ze SQLite souborů, tedy ze **stavu
z okamžiku převodu** – objednávky a změny od 20:52 UTC 4. 11. 2026 by byly jen v PostgreSQL. Čím déle produkce
běží, tím je tahle cesta méně užitečná; náhradou je obnova z `pg_dump` (krok 4).

## Známá omezení

- Obnova z `minishop.pgdump` ještě není v `restore.sh` (krok 4) – do té doby ručně `pg_restore`.
- SQLite provider, migrace SQLite a SQLite v záloze jsou v kódu do kroku 3C.
- UptimeRobot během odstávky ohlásil výpadek – očekávané.

## Jak ověřit

```text
ssh minishop-vm "cd /opt/minishop && sudo -u deploy docker compose exec -T web printenv Database__Provider"   → Postgres
```

Ráno po první noční záloze: v logu `minishop.pgdump: … B` řádově stovky kB (`sudo journalctl -u minishop-backup -n 15`).

## Vyzkoušejte sami

1. Dotaz přes standardní vstup (PowerShell nemění uvozovky):
   ```powershell
   'select "Id", "Status" from ordering."Orders" order by "Id" desc limit 5;' | ssh minishop-vm "cd /opt/minishop && sudo -u deploy docker compose exec -T postgres psql -U minishop -d minishop"
   ```
2. `'\dn'` místo dotazu – schémata modulů; `'\dt ordering.*'` – tabulky objednávek.
