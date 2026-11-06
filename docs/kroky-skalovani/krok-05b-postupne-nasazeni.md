# Škálování krok 5B: postupné nasazení (rolling deploy)

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Po kroku 5 běží Web ve dvou kopiích, ale `deploy.sh` volal
`docker compose up -d --wait` – Compose při novém image **nejdřív zastaví všechny staré kopie** a pak spustí nové.
Každé nasazení tak znamenalo desítky sekund výpadku. Teď se kopie vymění tak, aby vždy aspoň jedna obsluhovala.

## Jak to funguje

`deploy/rolling-update.sh <služba> <počet kopií> [timeout]` – „výměna směny u přepážek“:

1. `docker compose up -d --no-deps --no-recreate --scale web=<staré + nové> web` – staré kopie zůstanou beze změny
   (`--no-recreate`), nové vzniknou s novým image a konfigurací.
2. `--wait` počká na healthcheck. **Nezdravé nové kopie skript odstraní** a skončí chybou – staré běží dál
   (rollback bez výpadku).
3. 15 s pauza: Caddy obnovuje seznam kopií z DNS po 5 s a kopii, která při startu odmítla spojení, vyřadí na 5 s.
4. Staré kopie `docker stop --time 30` (rozpracované požadavky se dokončí) a `docker rm`.

`deploy.sh` (krok 5): nejdřív infrastruktura `up -d --no-deps --remove-orphans --wait` (postgres, rabbitmq,
aspire-dashboard, caddy – `--no-deps`, jinak by Compose kvůli `depends_on` caddy → web vyměnil Web naráz), pak
`rolling-update.sh inventory-api 1` a `rolling-update.sh web $WEB_REPLICAS`. Commit bez `rolling-update.sh`
(ruční rollback na starší verzi) = dosavadní `up --wait`.

Vedoucí kopie (krok 5) se vymění sama – stará při zastavení pustí zámek, nová ho do 5 s převezme.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `deploy/rolling-update.sh` | postupná výměna kopií jedné služby |

## Změněné soubory

- `deploy/deploy.sh` – infrastruktura zvlášť, sklad a Web přes `rolling-update.sh` (volitelný soubor z gitu);
  změněný Caddyfile se načte před výměnou kopií (oprava po prvním nasazení).
- `deploy/Caddyfile` – `fail_duration 5s` (dřív 30 s), `lb_try_duration 10s`, `transport http { dial_timeout 1s }`.
- Příručka: CD (sekce Postupné nasazení + diagram), databáze (pravidlo pro migrace), více instancí, monitoring, historie.

## Generálka (lokální Compose, 2 kopie Webu + Caddy se stejným `web-upstream`)

Verze = stejný image pod štítky `v1`/`v2`, nezdravá `v3` (ENTRYPOINT `sleep infinity`). Smyčka `GET /Shop` přes Caddy
každých 0,2 s.

| Pokus | Výsledek | Co z toho plynulo |
| --- | --- | --- |
| 1. lokální `WEB_PORT=8080-8081` | 4 kopie se do rozsahu nevešly → nové odstraněny, 80× 200 | cesta selhání funguje; lokálně rozsah aspoň 2× kopií |
| 2. původní Caddy (`fail_duration 30s`, bez `dial_timeout`) | 59× 200, **1× 502, 1× 503** | viz níže |
| 3. `dial_timeout 1s`, `lb_try_duration 10s` | vše 200, ale 1 požadavek ~9 s | debug log Caddy: nové kopie vyřazené |
| 4. `fail_duration 5s`, pauza 15 s – tři nasazení po sobě | **522× 200**, nejdelší 1,3 s | ✔ |
| 5. nezdravá `v3` | po ~50 s „unhealthy“ → nové odstraněny, **153× 200** | ✔ rollback bez výpadku |
| infrastruktura `--no-deps` s jiným image Webu v konfiguraci | Web ani sklad se nezměnily | ✔ |

**Proč 502/503 a 9 s:** (a) odstraněná kopie nemá IP – spojení na ni neskončí odmítnutím, ale čeká na timeout
(proto `dial_timeout 1s`); (b) nová kopie je v DNS hned po startu, ale port otevře o pár sekund později – Caddy ji
po prvním odmítnutí vyřadil na celých 30 s, a když staré kopie odešly, neměl kam posílat (proto `fail_duration 5s`
a pauza 15 s). Odhalil to až debug log Caddy (`refreshing A upstreams`, `selected upstream`).

## Produkce – první nasazení (5. 11. 2026)

Nový `deploy.sh` nainstalovaný na VM před sloučením PR #66, během nasazení smyčka `GET /Shop` každých 0,3 s z VM.

- Sklad 1 + 1 kopie, Web 2 + 2 kopie (`web-3`, `web-4`), staré pryč – celá výměna ~50 s, smoke test zelený.
- **1223× 200, 1× 502** (20:53:35, po 3 s). Log Caddy: `dial tcp 172.18.0.6:8080: i/o timeout` – požadavek na odstraněnou
  kopii. Příčina: `deploy.sh` načítal změněný Caddyfile (`caddy reload`) **až po** výměně kopií, takže celé nasazení běželo
  se starým nastavením Caddy (výchozí dial timeout 3 s, `fail_duration 30s`) – stejný případ jako pokus 2 generálky.
- Oprava: `reload_caddy_if_changed` hned po kroku s infrastrukturou, před `rolling-update.sh`.

**Druhé nasazení (PR #67, s opravou):** sklad 1 + 1, Web 2 + 2 (`web-5`, `web-6`), výměna ~50 s – **968× 200**,
žádná chyba ani odpověď nad 2 s (171 požadavků během výměny). ✔

## Rozhodnutí

- **Vlastní skript místo Swarmu / Kubernetes / pluginu docker-rollout:** pár řádků bashe, nic dalšího na VM, jde
  vyzkoušet lokálně.
- **Nejdřív sklad, pak Web:** Web ho volá jen kvůli administraci; zprávy mezitím počkají ve frontě.
- **Infrastruktura dál naráz:** mění se zřídka, restart PostgreSQL/RabbitMQ je pár sekund a Caddy se načítá `reload`.
- **Krátké vyřazení (5 s) místo aktivních health checků Caddy:** stačí na start i odchod kopie, nic navíc.

## Známá omezení

- **Migrace musí být kompatibilní se starší verzí** – stará a nová verze běží chvíli souběžně nad stejnou databází
  (přejmenování/smazání = dvě nasazení, expand → contract).
- Požadavek, který trefí odcházející kopii, se o ~1 s zpozdí (timeout spojení, pak jiná kopie).
- Během nasazení na chvíli 4 kopie Webu (+~1 GB RAM).
- Kontejnery se po nasazení jmenují jinak (`minishop-web-3`, `-4`…).
- Změnu `deploy.sh` je třeba na VM nainstalovat ručně před sloučením.

## Jak ověřit

Na VM během nasazení:

```bash
for i in $(seq 1 300); do curl -s -o /dev/null -m 15 -w "%{http_code}\n" https://minishop-tichy.cz/Shop; sleep 0.3; done | sort | uniq -c
```

(Spustit těsně před sloučením PR – nasazení se rozběhne po CI.) Čekáme jen `200`. V logu nasazení
`/opt/minishop/deploy.log` a ve výpisu Actions řádky `[web] Staré kopie: 2 – přidávám 2 nových` … `Hotovo`.

## Vyzkoušejte sami

1. Lokálně v `deploy/.env`: `WEB_REPLICAS=2`, `WEB_PORT=8080-8083` (rozsah pro staré i nové kopie), `docker compose up -d`.
2. `docker tag minishop-web:local minishop-web:v2`, pak `IMAGE_TAG=v2 ./rolling-update.sh web 2` a sledujte
   `docker compose ps web` – čtyři kopie, pak dvě nové.
3. Rozbitá verze: image s `ENTRYPOINT ["sleep", "infinity"]` jako `v3` → skript nové kopie zahodí, staré zůstanou.
