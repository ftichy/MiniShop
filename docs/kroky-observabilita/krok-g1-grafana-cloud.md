# Observabilita krok G1: Alloy a export do Grafana Cloud

Plán: [Observabilita-roadmap.md](../Observabilita-roadmap.md). Cíl: telemetrie (trasy, logy, metriky) s historií 14 dní –
Aspire dashboard drží data jen v paměti a po restartu je pryč.

## Jak to funguje

Přirovnání: Aspire dashboard je tachometr (ukazuje „teď“), Grafana Cloud černá skříňka (zaznamenává a jde se do ní podívat zpětně).
Alloy je rozbočovač, který signál pošle do obou.

```
web ×2, inventory-api ──OTLP gRPC :4317──► alloy ──► aspire-dashboard   (vše, jako dřív)
                                              └──► Grafana Cloud (EU)  (bez osobních údajů)
```

1. Služby dál exportují OpenTelemetry (kód beze změny), jen `OTEL_EXPORTER_OTLP_ENDPOINT` ukazuje na `alloy` místo dashboardu
   (`compose.grafana.yaml` přepíše hodnotu z `compose.yaml`).
2. Alloy (`config.alloy`) pošle vše do Aspire dashboardu a kopii přes `otelcol.processor.attributes "privacy"` (smaže
   `client.address`, `client.port`, `network.peer.address`, `http.client_ip`, `user_agent.original`, `url.query`) a dávkování
   do Grafana Cloud (`otlphttp`, Basic auth: Instance ID + token).
3. Grafana Cloud ukládá trasy do Tempa, logy do Loki a metriky do Mimiru (Prometheus) – 14 dní.

## Nové soubory

- `deploy/compose.grafana.yaml` – služba `alloy` (`grafana/alloy:v1.21.3`, 256 MB), přesměrování OTLP služeb
- `deploy/config.alloy` – příjem, filtr osobních údajů, export do dashboardu a Grafana Cloud
- `docs/Observabilita-roadmap.md`, tento souhrn

## Změněné soubory

- `deploy/production.env` – `COMPOSE_FILE` + `compose.grafana.yaml`, `GRAFANA_CLOUD_OTLP_ENDPOINT`, `GRAFANA_CLOUD_INSTANCE_ID`
- `deploy/deploy.sh` – secret `GRAFANA_CLOUD_OTLP_TOKEN` (kontrola tvaru `glc_…`), volitelné soubory `compose.grafana.yaml`
  a `config.alloy`, restart Alloye při změně `config.alloy`
- `.github/workflows/deploy.yml` – předání secretu přes stdin
- `docs/secrets.md` (řádek + postup R15), příručka `nasazeni/monitoring.md`, `github/secrets.md`, `eda/observabilita.md`,
  `treti-strany/beh.md` (Alloy), `treti-strany/sluzby.md` (Grafana Cloud), `historie.md`, CLAUDE.md

## Rozhodnutí

- **Alloy mezi službami a dashboardem, ne druhý exportér v aplikaci:** .NET `UseOtlpExporter` umí jeden cíl; filtr osobních
  údajů a přihlášení ke cloudu patří do provozu, ne do kódu. Rollback = jeden řádek v `production.env`.
- **Volitelné soubory v `deploy.sh`:** starší commity je nemají – ruční rollback přes Deploy dál funguje.
- **Token povinný jen se zapnutým doplňkem:** `compose.grafana.yaml` ho vyžaduje (`${…:?}`), `deploy.sh` ho bere jako volitelný.
- **Do cloudu jen zápis:** token nemá `read` – únik nedovolí číst data.
- **Filtr osobních údajů jako druhá vrstva:** aplikace e-maily ani IP do logů a tras nepíše (účty krok 8); Alloy maže atributy,
  které by je mohly nést, kdyby instrumentace v nové verzi přidala víc.
- **Bez filtru metrik zatím:** počet řad se změří po nasazení (*Usage*), filtr až podle skutečných čísel (G2).

## Známá omezení

- Výpadek Alloye = díra v telemetrii i v Aspire dashboardu (e-shop běží dál).
- Každá nová kopie služby má nové `service.instance.id` → po nasazení se metriky počítají jako nové řady (staré do pár hodin vypadnou z „aktivních“).
- Změna `config.alloy` = restart Alloye (pár sekund bez telemetrie).
- Alarmy zatím žádné – krok G3.

## Jak ověřit

- Lokálně generálka: token do `deploy/.env`, `docker compose -f compose.yaml -f compose.grafana.yaml up -d --wait`, objednávka →
  trasa v Aspire i v Grafaně (*Explore* → `…-traces`).
- Produkce po nasazení: `docker compose ps alloy` běží, Aspire dashboard dál ukazuje data, v Grafaně trasy, logy (`service_name = web`)
  a metriky (`minishop_outbox_delivered_total`).
- Kontrola osobních údajů: v Grafaně u trasy HTTP požadavku nesmí být `client.address` ani `user_agent.original`.

## Vyzkoušejte sami

1. Udělejte objednávku a v Grafaně najděte její trasu podle `minishop.order.id`.
2. Z řádku logu v Loki přejděte na trasu (*Trace ID*).
3. Restartujte web (`docker compose restart web`) – v Aspire historie zmizí, v Grafaně zůstane.
