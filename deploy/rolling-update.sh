#!/bin/bash
# Škálování krok 5B: postupné nasazení (rolling update) jedné služby Compose – bez výpadku.
#
#   ./rolling-update.sh <služba> <počet kopií> [timeout v s]       např. ./rolling-update.sh web 2
#
# Spouští se ve složce s compose.yaml a .env (na VM /opt/minishop, volá ho deploy.sh; lokálně deploy/).
# Compose sám umí jen „zastavit staré, spustit nové“ (výpadek). Tady jako výměna směny u přepážek:
#   1. ke starým kopiím přibydou nové (--scale na staré + nové, --no-recreate = staré zůstanou beze změny),
#   2. počká, až budou nové healthy – když ne, nové odstraní a staré obsluhují dál (rollback bez výpadku),
#   3. počká, až je Caddy začne používat (DNS refresh a fail_duration 5 s v Caddyfile), a staré kopie zastaví a odstraní.
#      Požadavek, který ještě míří na zastavenou kopii, Caddy zopakuje na jiné (lb_try_duration).
# Vedoucí kopie (LeaderElection) se vymění sama: stará při zastavení pustí zámek, převezme ho nová.
# Pozor: stará a nová verze chvíli běží souběžně nad stejnou databází – migrace musí být kompatibilní se starší verzí.
set -euo pipefail

service="${1:?Použití: rolling-update.sh <služba> <počet kopií> [timeout]}"
replicas="${2:?Použití: rolling-update.sh <služba> <počet kopií> [timeout]}"
timeout="${3:-180}"
[[ "$replicas" =~ ^[1-9][0-9]?$ ]] || { echo "Chybný počet kopií: $replicas" >&2; exit 2; }

log() { echo "[$(date -u +%FT%TZ)] [$service] $*"; }

old_ids=$(docker compose ps --all --quiet "$service")
if [ -z "$old_ids" ]; then
    # První spuštění (nebo služba neběží) – není co střídat.
    log "Žádná běžící kopie – spouštím $replicas"
    docker compose up -d --no-deps --wait --wait-timeout "$timeout" --scale "$service=$replicas" "$service"
    exit 0
fi

old_count=$(echo "$old_ids" | wc -w)
log "Staré kopie: $old_count – přidávám $replicas nových"

# 1.–2. Nové kopie vedle starých. --wait čeká na healthcheck všech (staré už healthy jsou).
if ! docker compose up -d --no-deps --no-recreate --wait --wait-timeout "$timeout" \
        --scale "$service=$((old_count + replicas))" "$service"; then
    new_ids=$(docker compose ps --all --quiet "$service" | grep -vxF -f <(echo "$old_ids") || true)
    log "Nové kopie nejsou zdravé – odstraňuji je, staré běží dál"
    [ -n "$new_ids" ] && docker rm -f $new_ids > /dev/null
    exit 1
fi

new_ids=$(docker compose ps --all --quiet "$service" | grep -vxF -f <(echo "$old_ids") || true)
[ -n "$new_ids" ] || { log "Nové kopie nevznikly"; exit 1; }
log "Nové kopie healthy: $(docker inspect --format '{{.Name}}' $new_ids | tr -d / | tr '\n' ' ')"

# 3. Caddy obnovuje seznam kopií z DNS po 5 s a kopii, která při startu odmítla spojení, vyřadí na 5 s (fail_duration).
#    Počkat déle než obojí – nové kopie musí být v Caddy použitelné dřív, než staré zmizí.
sleep 15

for id in $old_ids; do
    name=$(docker inspect --format '{{.Name}}' "$id" | tr -d /)
    log "Zastavuji starou kopii $name"
    # 30 s na dokončení rozpracovaných požadavků (ASP.NET po SIGTERM nové nepřijímá, rozpracované dokončí).
    docker stop --time 30 "$id" > /dev/null
    docker rm "$id" > /dev/null
done

log "Hotovo – běží $(docker compose ps --quiet "$service" | wc -l) kopií"
