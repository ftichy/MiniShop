#!/bin/bash
# Cloud krok 12: noční záloha MiniShopu do OCI Object Storage. Na VM v /opt/minishop (přinese ho deploy.sh z gitu).
# Spouští ho systemd timer minishop-backup.timer (02:30 UTC) jako uživatel deploy; ručně:
#   sudo -u deploy /opt/minishop/backup.sh
#
# Vlastní práci dělá backup.py v kontejneru s OCI CLI (online backup SQLite → tar.gz → bucket minishop-backups).
# Volitelně hlásí průběh na healthchecks.io (HEALTHCHECKS_BACKUP_URL v .env, secret z GitHubu) – když noční
# záloha neproběhne nebo selže, healthchecks.io pošle e-mail.
set -euo pipefail

APP_DIR=/opt/minishop
IMAGE=ghcr.io/oracle/oci-cli:20261007
cd "$APP_DIR"

ping_url=$(grep '^HEALTHCHECKS_BACKUP_URL=' .env 2>/dev/null | cut -d= -f2- || true)
ping() { [ -n "$ping_url" ] && curl -fsS -m 10 --retry 3 -o /dev/null "$ping_url$1" || true; }

ping /start
trap 'ping /fail' ERR

# --user 0: soubory ve volumes patří uživateli "app" z image služeb; kontejner je jen čte (online backup).
docker run --rm --user 0 \
    -v minishop_web-data:/web \
    -v minishop_inventory-data:/inventory \
    -v "$APP_DIR/backup.py:/backup.py:ro" \
    --entrypoint python3 "$IMAGE" /backup.py backup

ping ""
