# Mikroservisy krok M4: Méně metrik v Grafaně

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Vyhodnocení M3 ukázalo, že největší cena dvou nových služeb
jsou **aktivní metrické řady** v Grafana Cloud: běžně ~4 700, při nasazení s provozem až ~10 200 (limit free tieru 10 000).
Krok vrací rezervu – bez změny aplikace, jen v Alloyi.

## Jak to funguje

```
služby ──OTLP──► alloy ──► aspire-dashboard            (všechny metriky, beze změny)
                   └──► privacy ──► cloud_metrics ──► batch ──► Grafana Cloud
                                    (jen minishop.*, http.server.request.duration, dotnet.*)
```

- Nový procesor `otelcol.processor.filter "cloud_metrics"` v [config.alloy](../../deploy/config.alloy) zahodí každou
  metriku, která neodpovídá seznamu. Podmínka v jazyce OTTL říká, co **zahodit**:
  `not (minishop.* nebo http.server.request.duration nebo dotnet.*)`.
- Filtr je jen ve větvi do Grafana Cloud. Aspire dashboard dostává data přímo z receiveru, takže při živém ladění
  jsou vidět všechny metriky jako dřív. Logy a trasy filtr nemění.
- Přirovnání: do archivu (Grafana) se ukládají jen dokumenty, které někdo čte; na stole (Aspire) leží všechno.

## Co zabíralo řady (Explore, 19. 11. 2026, celkem ~5 460)

| Metrika | Řad | Kdo ji čte | Teď |
| --- | --- | --- | --- |
| `http_server_request_duration_seconds_bucket` | 1 035 | dashboard (p95), alarm 5xx, tutoriál | zůstává |
| `minishop_outbox_delay_seconds_bucket` | 400 | dashboard | zůstává |
| `dns_lookup_duration_seconds_bucket` | 330 | nikdo | zahozeno |
| `http_client_request_duration_seconds_bucket` | 225 | nikdo | zahozeno |
| `aspnetcore_authentication_authenticate_duration_seconds_bucket` | 210 | nikdo | zahozeno |
| `http_client_request_time_in_queue_seconds_bucket` | 195 | nikdo | zahozeno |
| `kestrel_connection_duration_seconds_bucket` | 150 | nikdo | zahozeno |
| `http_client_connection_duration_seconds_bucket` | 120 | nikdo | zahozeno |
| `aspnetcore_identity_sign_in_authenticate_duration_seconds_bucket` | 90 | nikdo | zahozeno |
| `aspnetcore_authorization_attempts_total`, `…_routing_match_attempts_total`, `…_rate_limiting_…`, `…_identity_user_update_…`, `http_client_open_connections` | ~190 | nikdo | zahozeno |
| `asserts:*` | ~380 | Grafana (Asserts / Application Observability) | počítá je Grafana sama, Alloy je neovlivní |

Odhad úspory: **~1 600–1 800 řad** v běžném provozu (z ~5 460 na ~3 700), při nasazení zhruba dvojnásobek.

## Změněné soubory

- `deploy/config.alloy` – procesor `cloud_metrics` mezi `privacy` a `batch` (jen metriky).
- Příručka `nasazeni/monitoring.md` (Alloy, řádek alarmu limitu), `historie.md`; roadmapa; `CLAUDE.md`.

## Rozhodnutí

- **Seznam povolených (allowlist), ne zakázaných** – nová knihovna s vlastními metrikami nezvýší řady potichu.
  Cena: novou cizí metriku pro dashboard je nutné povolit ve filtru (vlastní `minishop.*` projdou samy).
- **`dotnet.*` zůstává** – málo řad (desítky na metriku), a při podezření na únik paměti je historie užitečná
  (Aspire drží data jen v paměti).
- **Přihrádky histogramu `http.server.request.duration` beze změny** – je to největší metrika, ale čtou ji dashboard
  i alarm. Zmenšení přihrádek v ServiceDefaults je další krok, kdyby rezerva nestačila.
- Bez změny aplikace – vypnout = smazat procesor (výstup `privacy` zpět na `batch`).

## Známá omezení

- Zahozené metriky v Grafaně chybí i zpětně od nasazení (Grafana je přestane dostávat; staré hodnoty zůstanou 14 dní).
- `asserts:*` řady vytváří Grafana z dat, která do ní tečou – filtr je nesníží.

## Jak ověřit

1. Lokálně: `docker run --rm -v <repo>/deploy:/cfg:ro grafana/alloy:v1.21.3 fmt /cfg/config.alloy` (syntaxe)
   a `run` s falešnými proměnnými `GRAFANA_CLOUD_*` – v logu `finished node evaluation … cloud_metrics` bez chyby.
2. Po nasazení (deploy.sh Alloy restartuje): dashboard *MiniShop – zprávy a ságy* má data (zprávy, ságy, odezva, 5xx),
   alarmy zůstanou Normal.
3. Za ~30 min Explore → `-prom`: `topk(30, count by (__name__) ({__name__=~".+"}))` – v seznamu už není `dns_…`,
   `http_client_…`, `kestrel_…`, `aspnetcore_…`.
4. Explore → `grafanacloud-usage`: `grafanacloud_instance_active_series` – pokles a při dalším nasazení špička pod 8 000.

## Vyzkoušejte sami

- V Aspire dashboardu (https://dashboard.minishop-tichy.cz) jsou metriky `http.client.*` dál vidět – filtr platí jen pro cloud.
