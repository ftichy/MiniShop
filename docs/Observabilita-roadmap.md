# Roadmapa: trvalá observabilita (Grafana Cloud)

Cíl: historie telemetrie (14 dní), dashboard zpráv a ság a alarmy – Aspire dashboard drží data jen v paměti, nemá alarmy
ani vlastní grafy. Rozpočet 0 Kč (Grafana Cloud Free, bez karty). Souhrny kroků do `docs/kroky-observabilita/`.

Rozhodnutí:
- **Grafana Cloud, ne vlastní stack na VM** – Grafana + Prometheus + Loki + Tempo by na malé VM zabraly víc paměti než
  MiniShop a potřebovaly zálohy a aktualizace.
- **Aplikace se kvůli Grafaně nemění** – OpenTelemetry (EDA krok 11) už exportuje OTLP; mezi služby a dashboard se vloží
  **Grafana Alloy** (OpenTelemetry collector), který data rozdělí do Aspire dashboardu a do Grafana Cloud.
- **Region EU (Germany)**, do cloudu bez osobních údajů (Alloy maže IP, prohlížeč, query string – druhá vrstva).
- **Token jen se zápisem** – únik nedovolí číst data.
- Lokálně (AppHost, Compose) beze změny, Alloy jen v produkci (`compose.grafana.yaml` v `COMPOSE_FILE`).

| Krok | Co | Stav |
| --- | --- | --- |
| G1 | Alloy na VM + export tras, logů a metrik do Grafana Cloud (stack `patientjaguar2071`, EU), filtr osobních údajů, secret `GRAFANA_CLOUD_OTLP_TOKEN` | v práci |
| G2 | Dashboard EDA jako kód: metriky stavu (outbox čekající/poison, ságy podle stavu – jen vedoucí kopie), RabbitMQ Prometheus plugin (délky front, dead-letter), JSON dashboard v gitu | plán |
| G3 | Alarmy a SLO jako kód: dead-letter, outbox se nevyprazdňuje, 5xx, výpadek telemetrie; SLO „99 % objednávek potvrzeno do 30 s“, e-mail | plán |

Limity free tieru: 10 000 aktivních metrických řad, 50 GB logů, 50 GB tras měsíčně, historie 14 dní, 3 uživatelé.
