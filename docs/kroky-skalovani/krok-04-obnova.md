# Škálování krok 4: obnova ze zálohy (PostgreSQL)

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Noční záloha obsahuje od kroku 3A snímek `minishop.pgdump`,
ale `restore.sh` uměl obnovit jen SQLite soubory – produkci na PostgreSQL by „obnovil“ jen nepoužívané soubory.
Záloha bez vyzkoušené obnovy není záloha.

## Jak to funguje

`restore.sh latest | <jméno> | <cesta k souboru>` – **nejdřív všechno zkontrolovat, teprve pak měnit**:

1. zastaví `web` a `inventory-api`,
2. `backup.py fetch` – stáhne zálohu z bucketu (nebo vezme lokální soubor), rozbalí ji, `integrity_check` SQLite.
   Nic v produkci nemění,
3. když `.env` říká `DATABASE_PROVIDER=Postgres`:
   - snímek musí existovat, být čitelný (`pg_restore --list` v kontejneru databáze) a obsahovat data modulů
     (schéma `ordering`) – **záloha z doby před přepnutím** má prázdný snímek a obnova by smazala všechna data, proto ji
     skript odmítne,
   - dosavadní databázi **přejmenuje** na `minishop_before_restore_<čas>` (nic se nemaže), založí novou `minishop`
     a nahraje do ní snímek (`pg_restore --exit-on-error --no-owner`),
   - když selže založení nebo nahrání: novou smaže, původní přejmenuje zpět,
4. `backup.py restore-files` – SQLite soubory, Data Protection klíče, obrázky (původní jako `*.before-restore-…`),
5. spustí služby – vždy, i po chybě (pak s původními daty).

Na konci vypíše příkaz `DROP DATABASE minishop_before_restore_…` na úklid po ověření webu.

`backup.py` má místo `restore` dva příkazy: `fetch` (stáhnout a zkontrolovat) a `restore-files` (vyměnit soubory) –
obnovu PostgreSQL mezi nimi dělá `restore.sh`, protože `pg_restore` je v kontejneru databáze, ne v image s OCI CLI.

## Změněné soubory

- `deploy/restore.sh` – fáze ověření, obnova PostgreSQL s vrácením, lokální soubor zálohy, `MINISHOP_APP_DIR`
  a `COMPOSE_PROJECT_NAME` (jen pro zkoušku nanečisto – na VM výchozí `/opt/minishop` a `minishop`).
- `deploy/backup.py` – `fetch` + `restore-files` místo `restore`, záloha z `/restore-in/`.
- Příručka: Zálohy (obnova s diagramem), Databáze, historie; roadmapa, CLAUDE.md.

## Generálka nanečisto (lokální Compose, projekt `minishop-krok4`)

| Zkouška | Výsledek |
| --- | --- |
| Záloha (`pg_dump`) → změna dat (nová kategorie, smazaná role účtu) → `restore.sh soubor` | data zpět ve stavu zálohy, původní DB jako `minishop_before_restore_…`, web Healthy, 19 s |
| Záloha s prázdným snímkem (jako před přepnutím) | odmítnuto, nic se nezměnilo |
| Useknutý snímek | odmítnuto už při `pg_restore --list`, nic se nezměnilo |
| Selhání po přejmenování (spojení na `template1` → `CREATE DATABASE` selže) | původní databáze vrácena, web Healthy |

Generálka našla dvě věci: PostgreSQL jména bez uvozovek zmenší (jméno záložní DB je teď rovnou malými písmeny)
a chybějící vrácení, když selže už založení nové databáze (opraveno – vrácení kryje založení i nahrání).

## Rozhodnutí

- **Přejmenovat, ne smazat** – obdoba `*.before-restore-…` u souborů; rychlá cesta zpět i po „úspěšné“ obnově
  ze špatné zálohy. Smazání je vědomý krok po ověření.
- **Kontrola obsahu snímku** – technicky platná, ale prázdná záloha je nejnebezpečnější: obnova by „uspěla“ a smazala data.
- **Obnova z lokálního souboru** – pro zkoušku nanečisto i pro nouzi (bucket nedostupný, záloha stažená jinudy).
- **`pg_restore --no-owner`** – vlastníkem objektů je uživatel, který obnovuje (`minishop`), nezávisle na záloze.

## Známá omezení

- Obnovuje se celá databáze (všechny moduly najednou) – obnova jednoho modulu by šla ručně
  (`pg_restore --schema=ordering`), skript ji nenabízí.
- Dokud je v kódu SQLite (do kroku 3C), obnovují se i SQLite soubory – neškodí, produkce je nepoužívá.

## Jak ověřit

```text
bash -n deploy/restore.sh
```

Cvičná obnova v produkci: ruční záloha → počty řádků → `restore.sh latest` → stejné počty, web → `DROP DATABASE …`.

## Vyzkoušejte sami

1. `sudo -u deploy /opt/minishop/restore.sh list` – zálohy v bucketu.
2. Po obnově: `\l` v `psql` (databáze `minishop` a `minishop_before_restore_…`), pak úklid příkazem z výpisu skriptu.
