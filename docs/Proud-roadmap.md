# Proud událostí – roadmapa (směr B)

Cíl: vedle front (zprávu si modul vezme a zmizí) zavést **proud** (log) – zprávy zůstávají, každý čtenář má svou záložku
a může si přečíst historii. RabbitMQ 4 (typ fronty `stream`), přístup přes AMQP (`RabbitMQ.Client`), 0 Kč.
Teorie: příručka [Proud zpráv](prirucka/eda/proud-zprav.md). Souhrny kroků do `docs/kroky-proud/`.

| Krok | Co | Stav |
| --- | --- | --- |
| B1. Proud zpráv | stream `minishop.events` (14 dní / 1 GB, segmenty 50 MB) navázaný na všechny kontrakty, založení Webem (vedoucí kopie); čtečka od času; stránka **Proud zpráv** (`/Admin/EventStream`, Provoz) – pouť objednávky napříč moduly; maskování osobních údajů; testy nad skutečným RabbitMQ (Testcontainers). Souhrn [krok-b1](kroky-proud/krok-b1-proud-zprav.md). | hotovo (21. 11. 2026, ověřeno) |
| B2. Projekce přehráním | ve službě Reporting.Api čtecí model **Rychlost vyřízení** (doba potvrzení, předání dopravci, doručení – p50/p95 po dnech) jen z proudu; záložka (offset) v tabulce modulu ve stejné transakci jako data; „Přestavět z proudu“ = záložka na začátek. Srovnání s přestavěním přes snímky (krok 11). Souhrn [krok-b2](kroky-proud/krok-b2-projekce-prehranim.md). | hotovo (21. 11. 2026, ověřeno; 22. 11. obnovení stránky bez F5, ověřeno) |
| B3. Bez auditní fronty | `minishop.audit` je s proudem zbytečná a logovala celé zprávy i s osobními údaji – posluchač odstraněn, Web frontu smaže s podmínkou *ifUnused* (až ji žádná stará kopie neposlouchá); test, že obsah zpráv nejde do logu. Souhrn [krok-b3](kroky-proud/krok-b3-bez-auditni-fronty.md). | hotovo (22. 11. 2026, ověřeno) |

## Rozhodnutí

- AMQP místo nativního protokolu streamů (bez pluginu, portu 5552 a nové knihovny).
- Retence 14 dní / 1 GB; proud není zdroj pravdy a nepatří do zálohy.
- Osobní údaje ze zpráv se na stránkách ukazují jen zamaskované (`PersonalDataMask`).
