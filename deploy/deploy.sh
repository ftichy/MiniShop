#!/bin/bash
# Cloud krok 8: nasazení MiniShopu na VM. Na VM leží jako /usr/local/bin/minishop-deploy (vlastník root).
#
# SSH klíč GitHub Actions (uživatel "deploy") smí spustit JEN tento skript – vynucený příkaz v authorized_keys:
#   restrict,command="/usr/local/bin/minishop-deploy" ssh-ed25519 AAAA… github-actions
# Text, který klient pošle jako příkaz (ssh deploy@vm "<sha>"), dostane skript v SSH_ORIGINAL_COMMAND.
# Ručně (jako ubuntu): sudo -u deploy /usr/local/bin/minishop-deploy <celý sha commitu>
#
# Krok 10: tajné hodnoty posílá GitHub Actions na standardní vstup (ne v příkazu – ten je vidět v logu
# a v seznamu procesů), každou na řádek KLÍČ=hodnota:
#   RABBITMQ_PASSWORD=…
#   BASIC_AUTH_HASH=$2a$14$…
#   HEALTHCHECKS_BACKUP_URL=https://hc-ping.com/…   (krok 12, volitelný)
#   SMTP_PASSWORD=abcdefghijklmnop                   (účty krok 5, volitelný – heslo aplikace Gmailu)
#   INVENTORY_API_KEY=<64 hex znaků>                  (účty krok 7, povinný – servisní klíč skladu)
#   PAYMENTS_WEBHOOK_SECRET=<64 hex znaků>            (e-shop krok 8, povinný – podpis webhooku platební brány)
#   POSTGRES_PASSWORD=<24–128 písmen/číslic>          (škálování krok 3, povinný – databáze PostgreSQL)
# Co na vstupu chybí (ruční spuštění z terminálu), vezme se z dosavadního .env. Netajná konfigurace je v gitu
# (deploy/production.env). Soubor .env na VM skript pokaždé sestaví znovu – ruční změny v něm nepřežijí.
# Změněné heslo RabbitMQ nejdřív vymění v běžícím RabbitMQ (RABBITMQ_DEFAULT_PASS platí jen při prvním startu).
# Stejně heslo PostgreSQL (POSTGRES_PASSWORD platí jen při založení prázdného volume) – škálování krok 3.
#
# Postup: ověří vstup → stáhne deploy/ z GitHubu přesně pro daný commit → sestaví .env (IMAGE_TAG=sha-<7 znaků>)
#         → docker compose pull → up --wait (čeká na healthchecky) → při chybě vrátí předchozí verzi.
set -euo pipefail

APP_DIR=/opt/minishop
REPO=ftichy/MiniShop
FILES=(compose.yaml compose.cloud.yaml Caddyfile production.env)
# Krok 12: skripty záloh. Volitelné – starší commity (ruční rollback přes Deploy) je ještě nemají.
OPTIONAL_FILES=(backup.py backup.sh restore.sh)

sha="${SSH_ORIGINAL_COMMAND:-${1:-}}"
# Jen celý SHA commitu (40 malých hexa znaků). Nic jiného – žádné mezery, středníky, cesty.
if [[ ! "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Chybný vstup: čekám celý SHA commitu (40 hexa znaků)." >&2
    exit 2
fi
tag="sha-${sha:0:7}"

# Tajné hodnoty ze vstupu – jen známé klíče a jen hodnoty v očekávaném tvaru (do .env se nesmí dostat nic jiného).
# Z terminálu (ruční spuštění) se nečte nic.
rabbitmq_password=""
basic_auth_hash=""
healthchecks_backup_url=""
smtp_password=""
inventory_api_key=""
payments_webhook_secret=""
postgres_password=""
if [ ! -t 0 ]; then
    while IFS= read -r line || [ -n "$line" ]; do
        [ -z "$line" ] && continue
        key="${line%%=*}"
        value="${line#*=}"
        case "$key" in
            RABBITMQ_PASSWORD)
                # Jen písmena a číslice: heslo je součástí adresy amqp://uzivatel:heslo@rabbitmq.
                [[ "$value" =~ ^[A-Za-z0-9]{24,128}$ ]] || { echo "Chybný RABBITMQ_PASSWORD (24–128 písmen/číslic)." >&2; exit 2; }
                rabbitmq_password="$value" ;;
            BASIC_AUTH_HASH)
                [[ "$value" =~ ^\$2[aby]\$[0-9]{2}\$[./A-Za-z0-9]{53}$ ]] || { echo "Chybný BASIC_AUTH_HASH (čekám bcrypt otisk)." >&2; exit 2; }
                basic_auth_hash="$value" ;;
            HEALTHCHECKS_BACKUP_URL)
                # Krok 12: adresa, kam backup.sh hlásí průběh zálohy (prázdná = nehlásit).
                [[ -z "$value" || "$value" =~ ^https://hc-ping\.com/[A-Za-z0-9-]{8,64}$ ]] || { echo "Chybný HEALTHCHECKS_BACKUP_URL (čekám https://hc-ping.com/<uuid>)." >&2; exit 2; }
                healthchecks_backup_url="$value" ;;
            SMTP_PASSWORD)
                # Účty krok 5: heslo aplikace Gmailu = 16 malých písmen (bez mezer). Prázdné = e-maily jen do logu.
                [[ -z "$value" || "$value" =~ ^[a-z]{16}$ ]] || { echo "Chybný SMTP_PASSWORD (čekám 16 malých písmen bez mezer)." >&2; exit 2; }
                smtp_password="$value" ;;
            INVENTORY_API_KEY)
                # Účty krok 7: servisní klíč mezi Webem a skladem = 64 hex znaků (openssl rand -hex 32).
                [[ "$value" =~ ^[0-9a-f]{64}$ ]] || { echo "Chybný INVENTORY_API_KEY (čekám 64 malých hex znaků)." >&2; exit 2; }
                inventory_api_key="$value" ;;
            PAYMENTS_WEBHOOK_SECRET)
                # E-shop krok 8: tajemství pro podpis webhooku platební brány (HMAC) = 64 hex znaků (openssl rand -hex 32).
                [[ "$value" =~ ^[0-9a-f]{64}$ ]] || { echo "Chybný PAYMENTS_WEBHOOK_SECRET (čekám 64 malých hex znaků)." >&2; exit 2; }
                payments_webhook_secret="$value" ;;
            POSTGRES_PASSWORD)
                # Škálování krok 3: jen písmena a číslice – heslo je součástí connection stringu (znaky ; = by ho rozbily).
                [[ "$value" =~ ^[A-Za-z0-9]{24,128}$ ]] || { echo "Chybný POSTGRES_PASSWORD (24–128 písmen/číslic)." >&2; exit 2; }
                postgres_password="$value" ;;
            *)
                echo "Neznámý klíč na vstupu: $key" >&2
                exit 2 ;;
        esac
    done
fi

cd "$APP_DIR"
exec 9>"$APP_DIR/.deploy.lock"
flock -n 9 || { echo "Jiné nasazení právě běží." >&2; exit 3; }

log() { echo "[$(date -u +%FT%TZ)] $*" | tee -a "$APP_DIR/deploy.log"; }

# Hodnota z dosavadního .env (bez případných jednoduchých uvozovek); prázdná, když soubor nebo řádek chybí.
current() { [ -f .env ] && grep "^$1=" .env | tail -n 1 | cut -d= -f2- | tr -d "'" || true; }

previous_tag=$(current IMAGE_TAG)
previous_rabbitmq_password=$(current RABBITMQ_PASSWORD)
rabbitmq_password="${rabbitmq_password:-$previous_rabbitmq_password}"
basic_auth_hash="${basic_auth_hash:-$(current BASIC_AUTH_HASH)}"
healthchecks_backup_url="${healthchecks_backup_url:-$(current HEALTHCHECKS_BACKUP_URL)}"
smtp_password="${smtp_password:-$(current SMTP_PASSWORD)}"
inventory_api_key="${inventory_api_key:-$(current INVENTORY_API_KEY)}"
payments_webhook_secret="${payments_webhook_secret:-$(current PAYMENTS_WEBHOOK_SECRET)}"
[ -n "$rabbitmq_password" ] || { echo "Chybí RABBITMQ_PASSWORD (na vstupu ani v .env)." >&2; exit 2; }
[ -n "$basic_auth_hash" ] || { echo "Chybí BASIC_AUTH_HASH (na vstupu ani v .env)." >&2; exit 2; }
[ -n "$inventory_api_key" ] || { echo "Chybí INVENTORY_API_KEY (na vstupu ani v .env)." >&2; exit 2; }
[ -n "$payments_webhook_secret" ] || { echo "Chybí PAYMENTS_WEBHOOK_SECRET (na vstupu ani v .env)." >&2; exit 2; }
previous_postgres_password=$(current POSTGRES_PASSWORD)
postgres_password="${postgres_password:-$previous_postgres_password}"
[ -n "$postgres_password" ] || { echo "Chybí POSTGRES_PASSWORD (na vstupu ani v .env)." >&2; exit 2; }

log "Nasazuji $tag (commit $sha), předchozí ${previous_tag:-žádná}"

# 1. Konfigurace pro daný commit – stáhne se do dočasné složky; když cokoli chybí, nic se nezmění.
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for f in "${FILES[@]}"; do
    curl -fsSL --retry 3 "https://raw.githubusercontent.com/$REPO/$sha/deploy/$f" -o "$work/$f"
done
for f in "${OPTIONAL_FILES[@]}"; do
    curl -fsSL --retry 3 "https://raw.githubusercontent.com/$REPO/$sha/deploy/$f" -o "$work/$f" 2>/dev/null \
        || { rm -f "$work/$f"; log "  $f v commitu není – přeskočeno"; }
done
chmod 755 "$work"/*.sh 2>/dev/null || true

# 2. Nový .env = netajná konfigurace z gitu + štítek image + tajné hodnoty. Práva 600 od vzniku souboru.
(
    umask 077
    {
        echo "# Generuje minishop-deploy při každém nasazení – ruční změny přepíše. Zdroj: deploy/production.env + secrets z GitHubu."
        grep -v -e '^\s*#' -e '^\s*$' "$work/production.env"
        echo "IMAGE_TAG=$tag"
        echo "RABBITMQ_PASSWORD=$rabbitmq_password"
        # Otisk obsahuje znaky $ – v jednoduchých uvozovkách, jinak by je Compose bral jako proměnné.
        echo "BASIC_AUTH_HASH='$basic_auth_hash'"
        [ -n "$healthchecks_backup_url" ] && echo "HEALTHCHECKS_BACKUP_URL=$healthchecks_backup_url"
        [ -n "$smtp_password" ] && echo "SMTP_PASSWORD=$smtp_password"
        echo "INVENTORY_API_KEY=$inventory_api_key"
        echo "PAYMENTS_WEBHOOK_SECRET=$payments_webhook_secret"
        echo "POSTGRES_PASSWORD=$postgres_password"
        true
    } > "$work/.env"
)
rabbitmq_user=$(grep '^RABBITMQ_USER=' "$work/.env" | cut -d= -f2-)

# Záloha současného stavu pro rollback.
mkdir -p previous
[ -f .env ] && cp -p .env previous/.env
for f in "${FILES[@]}"; do [ -f "$f" ] && cp "$f" "previous/$f"; done

rabbitmq_rotated=false
rotate_rabbitmq_password() {
    # Heslo v příkazu je na VM krátce vidět v seznamu procesů – rabbitmqctl ho jinak převzít neumí.
    docker compose exec -T rabbitmq rabbitmqctl --quiet change_password "$rabbitmq_user" "$1" > /dev/null
}

postgres_rotated=false
rotate_postgres_password() {
    # Škálování krok 3: uvnitř kontejneru přes lokální socket (bez hesla). Heslo jde na stdin psql, ne do příkazové řádky.
    printf "ALTER USER minishop PASSWORD '%s';\n" "$1" \
        | docker compose exec -T postgres psql --quiet -v ON_ERROR_STOP=1 -U minishop -d minishop > /dev/null
}

reload_caddy() {
    docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile
}

rollback() {
    log "CHYBA – vracím ${previous_tag:-(nic – první nasazení)}"
    if [ "$rabbitmq_rotated" = true ]; then
        rotate_rabbitmq_password "$previous_rabbitmq_password" || log "Heslo RabbitMQ se nepodařilo vrátit!"
    fi
    if [ "$postgres_rotated" = true ]; then
        rotate_postgres_password "$previous_postgres_password" || log "Heslo PostgreSQL se nepodařilo vrátit!"
    fi
    if [ -f previous/.env ]; then
        cp -p previous/.env .env
        for f in "${FILES[@]}"; do [ -f "previous/$f" ] && cp "previous/$f" "$f"; done
        docker compose up -d --wait --wait-timeout 180 || log "Rollback se také nepovedl – zásah ručně!"
        reload_caddy || log "Caddy se nepodařilo znovu načíst – zásah ručně!"
    fi
    exit 1
}

# 3. Výměna hesla RabbitMQ (rotace): změnit ho v běžícím RabbitMQ dřív, než se služby spustí s novým.
if [ -n "$previous_rabbitmq_password" ] && [ "$rabbitmq_password" != "$previous_rabbitmq_password" ] \
    && [ -n "$(docker compose ps --status running --quiet rabbitmq 2>/dev/null)" ]; then
    log "Měním heslo RabbitMQ (uživatel $rabbitmq_user)"
    rotate_rabbitmq_password "$rabbitmq_password" || rollback
    rabbitmq_rotated=true
fi

# 3b. Škálování krok 3: totéž pro heslo PostgreSQL (POSTGRES_PASSWORD platí jen při založení prázdného volume).
if [ -n "$previous_postgres_password" ] && [ "$postgres_password" != "$previous_postgres_password" ] \
    && [ -n "$(docker compose ps --status running --quiet postgres 2>/dev/null)" ]; then
    log "Měním heslo PostgreSQL (uživatel minishop)"
    rotate_postgres_password "$postgres_password" || rollback
    postgres_rotated=true
fi

# 4. Nová konfigurace a tag.
cp -p "$work"/* .   # -p: zachovat práva (skripty záloh spustitelné)
cp -p "$work/.env" .env

# 5. Image a spuštění. --wait = počkat, až budou kontejnery healthy (smoke test na VM).
docker compose pull --quiet || rollback
docker compose up -d --remove-orphans --wait --wait-timeout 180 || rollback

# 6. Krok 11: Caddyfile je do kontejneru připojený jako soubor – Caddy jeho změnu sám nenačte a kontejner
#    se nerestartuje (image ani proměnné se nezměnily). Proto ho při změně načíst znovu (bez výpadku).
if ! cmp -s Caddyfile previous/Caddyfile; then
    log "Caddyfile se změnil – načítám ho v Caddy znovu"
    reload_caddy || rollback
fi

log "Hotovo: $tag"
docker compose ps --format '{{.Service}}\t{{.Image}}\t{{.Status}}'
