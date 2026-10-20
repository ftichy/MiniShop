#!/bin/bash
# Cloud krok 8: nasazení MiniShopu na VM. Na VM leží jako /usr/local/bin/minishop-deploy (vlastník root).
#
# SSH klíč GitHub Actions (uživatel "deploy") smí spustit JEN tento skript – vynucený příkaz v authorized_keys:
#   restrict,command="/usr/local/bin/minishop-deploy" ssh-ed25519 AAAA… github-actions
# Text, který klient pošle jako příkaz (ssh deploy@vm "<sha>"), dostane skript v SSH_ORIGINAL_COMMAND.
# Ručně (jako ubuntu): sudo -u deploy /usr/local/bin/minishop-deploy <celý sha commitu>
#
# Postup: ověří vstup → stáhne deploy/ z GitHubu přesně pro daný commit → IMAGE_TAG=sha-<7 znaků>
#         → docker compose pull → up --wait (čeká na healthchecky) → při chybě vrátí předchozí verzi.
set -euo pipefail

APP_DIR=/opt/minishop
REPO=ftichy/MiniShop
FILES=(compose.yaml compose.cloud.yaml Caddyfile)

sha="${SSH_ORIGINAL_COMMAND:-${1:-}}"
# Jen celý SHA commitu (40 malých hexa znaků). Nic jiného – žádné mezery, středníky, cesty.
if [[ ! "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Chybný vstup: čekám celý SHA commitu (40 hexa znaků)." >&2
    exit 2
fi
tag="sha-${sha:0:7}"

cd "$APP_DIR"
exec 9>"$APP_DIR/.deploy.lock"
flock -n 9 || { echo "Jiné nasazení právě běží." >&2; exit 3; }

log() { echo "[$(date -u +%FT%TZ)] $*" | tee -a "$APP_DIR/deploy.log"; }

previous_tag=$(grep '^IMAGE_TAG=' .env | cut -d= -f2 || true)
log "Nasazuji $tag (commit $sha), předchozí $previous_tag"

# 1. Konfigurace pro daný commit – stáhne se do dočasné složky; když cokoli chybí, nic se nezmění.
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for f in "${FILES[@]}"; do
    curl -fsSL --retry 3 "https://raw.githubusercontent.com/$REPO/$sha/deploy/$f" -o "$work/$f"
done

# Záloha současného stavu pro rollback.
mkdir -p previous
cp .env previous/.env
for f in "${FILES[@]}"; do [ -f "$f" ] && cp "$f" "previous/$f"; done

rollback() {
    log "CHYBA – vracím $previous_tag"
    cp previous/.env .env
    for f in "${FILES[@]}"; do [ -f "previous/$f" ] && cp "previous/$f" "$f"; done
    docker compose up -d --wait --wait-timeout 180 || log "Rollback se také nepovedl – zásah ručně!"
    exit 1
}

# 2. Nová konfigurace a tag.
cp "$work"/* .
sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$tag/" .env

# 3. Image a spuštění. --wait = počkat, až budou kontejnery healthy (smoke test na VM).
docker compose pull --quiet || rollback
docker compose up -d --remove-orphans --wait --wait-timeout 180 || rollback

log "Hotovo: $tag"
docker compose ps --format '{{.Service}}\t{{.Image}}\t{{.Status}}'
