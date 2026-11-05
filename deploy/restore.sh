#!/bin/bash
# Cloud krok 12: obnova MiniShopu ze zálohy v OCI Object Storage. Na VM v /opt/minishop (přinese ho deploy.sh).
#   sudo -u deploy /opt/minishop/restore.sh latest                          # nejnovější záloha
#   sudo -u deploy /opt/minishop/restore.sh minishop-20261024T023000Z.tar.gz
#   sudo -u deploy /opt/minishop/restore.sh /cesta/minishop-….tar.gz        # škálování krok 4: záloha z lokálního souboru
#   sudo -u deploy /opt/minishop/restore.sh list                            # co je v bucketu
#
# Postup (škálování krok 4 – nejdřív všechno zkontrolovat, teprve pak měnit):
#   1. zastaví web a inventory-api (nikdo nezapisuje),
#   2. backup.py fetch – stáhne zálohu a rozbalí ji,
#   3. ověří snímek minishop.pgdump (pg_restore --list, musí obsahovat data modulů – záloha z doby před přepnutím
#      na PostgreSQL je prázdná a obnova by data smazala),
#      dosavadní databázi PŘEJMENUJE na minishop_before_restore_<čas>, založí novou a nahraje do ní zálohu;
#      když pg_restore selže, novou smaže a původní vrátí,
#   4. backup.py restore-files – klíče a obrázky (původní nechá jako *.before-restore-…),
#   5. služby znovu spustí (i při chybě – s původními daty).
# RabbitMQ běží dál – zprávy jsou dočasné, pravda je v outboxech obnovených databází.
set -euo pipefail

# Škálování krok 4: jiná složka/projekt jen pro zkoušku nanečisto (lokální Compose); na VM výchozí hodnoty.
APP_DIR="${MINISHOP_APP_DIR:-/opt/minishop}"
IMAGE=ghcr.io/oracle/oci-cli:20261007
cd "$APP_DIR"

what="${1:?Použití: restore.sh latest | <jméno zálohy> | <cesta k souboru zálohy> | list}"

env_value() { grep "^$1=" .env 2>/dev/null | tail -n 1 | cut -d= -f2- | tr -d "'" || true; }
project="$(env_value COMPOSE_PROJECT_NAME)"
project="${project:-minishop}"

work=$(mktemp -d)
extra=()
source_name="$what"
if [ "$what" != "list" ] && [ -f "$what" ]; then
    extra=(-v "$(realpath "$what"):/restore-in/backup.tar.gz:ro")
    source_name=/restore-in/backup.tar.gz
fi

run_backup_py() {
    docker run --rm --user 0 \
        -v "${project}_web-data:/web" \
        -v "${project}_inventory-data:/inventory" \
        -v "$work:/restore" \
        "${extra[@]}" \
        -v "$APP_DIR/backup.py:/backup.py:ro" \
        --entrypoint python3 "$IMAGE" /backup.py "$@"
}

# Rozbalenou zálohu zapsal kontejner jako root – smazat ji musí také root.
cleanup() {
    docker run --rm --user 0 -v "$work:/restore" --entrypoint sh "$IMAGE" -c 'rm -rf /restore/* /restore/.[!.]*' || true
    rmdir "$work" 2>/dev/null || true
}

if [ "$what" = "list" ]; then
    trap cleanup EXIT
    run_backup_py list
    exit 0
fi

psql_admin() {
    docker compose exec -T postgres psql --quiet -v ON_ERROR_STOP=1 -U minishop -d postgres -c "$1"
}

restore_postgres() {
    local dump="$work/content/minishop.pgdump"
    local old
    old="minishop_before_restore_$(date -u +%Y%m%dt%H%M%Sz)"   # malými písmeny – PostgreSQL jména bez uvozovek stejně zmenší

    echo "PostgreSQL: dosavadní databáze → $old, nahrávám zálohu…"
    psql_admin "ALTER DATABASE minishop RENAME TO $old"
    # Od přejmenování musí jakákoli chyba (založení i nahrání) vrátit původní databázi – jinak by služby žádnou neměly.
    if ! psql_admin "CREATE DATABASE minishop OWNER minishop" \
        || ! docker compose exec -T postgres pg_restore --exit-on-error --no-owner -U minishop -d minishop < "$dump"; then
        echo "Obnova PostgreSQL selhala – vracím původní databázi." >&2
        psql_admin "DROP DATABASE IF EXISTS minishop" || true
        psql_admin "ALTER DATABASE $old RENAME TO minishop" \
            || echo "POZOR: původní databázi se nepodařilo vrátit – je jako $old, přejmenujte ji ručně na minishop!" >&2
        return 1
    fi

    echo "PostgreSQL obnoven. Původní databáze zůstala jako $old – po ověření webu ji smažte:"
    echo "  cd $APP_DIR && docker compose exec -T postgres psql -U minishop -d postgres -c 'DROP DATABASE $old'"
}

start=$(date +%s)
echo "Zastavuji web a inventory-api…"
docker compose stop web inventory-api
# Služby spustit znovu i při chybě obnovy (s původními daty – zůstala na místě nebo se vrátila).
trap 'echo "Spouštím služby…"; docker compose up -d --wait --wait-timeout 180; cleanup' EXIT

run_backup_py fetch "$source_name" /restore

dump="$work/content/minishop.pgdump"
[ -s "$dump" ] || { echo "Záloha neobsahuje snímek PostgreSQL (minishop.pgdump) – obnova se nespustí." >&2; exit 1; }
toc=$(docker compose exec -T postgres pg_restore --list < "$dump") \
    || { echo "Snímek PostgreSQL je poškozený (pg_restore --list selhal) – obnova se nespustí." >&2; exit 1; }
grep -q "SCHEMA - ordering " <<< "$toc" \
    || { echo "Snímek PostgreSQL neobsahuje data modulů – záloha je z doby před přepnutím na PostgreSQL (4. 11. 2026)." >&2
         echo "Obnovit ji by data smazalo. Vyberte novější zálohu (restore.sh list)." >&2; exit 1; }
restore_postgres

run_backup_py restore-files /restore

trap cleanup EXIT
echo "Spouštím služby…"
docker compose up -d --wait --wait-timeout 180
echo "Hotovo za $(( $(date +%s) - start )) s. Ověřte web a objednávky."
