#!/bin/bash
# Cloud krok 12: obnova MiniShopu ze zálohy v OCI Object Storage. Na VM v /opt/minishop (přinese ho deploy.sh).
#   sudo -u deploy /opt/minishop/restore.sh latest                          # nejnovější záloha
#   sudo -u deploy /opt/minishop/restore.sh minishop-20261024T023000Z.tar.gz
#   sudo -u deploy /opt/minishop/restore.sh list                            # co je v bucketu
#
# Postup: zastaví web a inventory-api (nikdo nezapisuje) → backup.py stáhne zálohu, zkontroluje ji
# (integrity_check) a vymění soubory ve volumes (původní nechá jako *.before-restore-…) → služby znovu spustí.
# RabbitMQ běží dál – zprávy jsou dočasné, pravda je v outboxech obnovených databází.
set -euo pipefail

APP_DIR=/opt/minishop
IMAGE=ghcr.io/oracle/oci-cli:20261007
cd "$APP_DIR"

what="${1:?Použití: restore.sh latest | <jméno zálohy> | list}"

run_backup_py() {
    docker run --rm --user 0 \
        -v minishop_web-data:/web \
        -v minishop_inventory-data:/inventory \
        -v "$APP_DIR/backup.py:/backup.py:ro" \
        --entrypoint python3 "$IMAGE" /backup.py "$@"
}

if [ "$what" = "list" ]; then
    run_backup_py list
    exit 0
fi

start=$(date +%s)
echo "Zastavuji web a inventory-api…"
docker compose stop web inventory-api
# Služby spustit znovu i při chybě obnovy (s původními daty, ta zůstala na místě).
trap 'echo "Spouštím služby…"; docker compose up -d --wait --wait-timeout 180' EXIT

run_backup_py restore "$what"

trap - EXIT
echo "Spouštím služby…"
docker compose up -d --wait --wait-timeout 180
echo "Hotovo za $(( $(date +%s) - start )) s. Ověřte web a objednávky."
