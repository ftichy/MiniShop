---
title: Slovníček pojmů
summary: Krátké vysvětlení odborných pojmů z dokumentace s odkazem tam, kde se o nich dočtete víc.
order: 80
---

> Pojmy jsou seřazené abecedně. Odkaz vede na stránku, kde je pojem vysvětlený na MiniShopu.

## Architektura a EDA

| Pojem | Význam | Víc |
| --- | --- | --- |
| **Agregát / entita** | objekt domény s identitou a pravidly, stav mění jen přes své metody (`Order.Confirm`) | [Vrstvy](architektura/vrstvy.md) |
| **At-least-once** | doručení „alespoň jednou“ – zpráva se neztratí, ale může přijít víckrát | [Outbox](eda/outbox.md) |
| **Broker** | prostředník, který přijímá zprávy a doručuje je do front příjemců (RabbitMQ) | [Broker](eda/broker.md) |
| **C4 model** | kreslení architektury v úrovních Context → Containers → Components → Code | [Architektura](architektura/index.md) |
| **Composition root** | jediné místo, kde se skládají závislosti (DI) – `Program.cs` | [Vrstvy](architektura/vrstvy.md) |
| **Concurrency token** | verze řádku; souběžná změna stejného záznamu selže místo přepsání | [Stavové diagramy](architektura/stavove-diagramy.md) |
| **Consumer** | proces, který čte zprávy z fronty a předává je handlerům | [Broker](eda/broker.md) |
| **Dekorátor** | obal, který přidá chování beze změny obalené třídy (`IdempotentDomainEventHandler`) | [SOLID](architektura/solid.md) |
| **Doménová událost** | fakt v minulém čase uvnitř modulu (`OrderConfirmed`) | [Doménové události](eda/domenove-udalosti.md) |
| **Dual write** | zápis do dvou systémů (DB + broker), který může skončit napůl | [Outbox](eda/outbox.md) |
| **Eventual consistency** | data se sjednotí se zpožděním (sekundy), ne okamžitě | [EDA](eda/index.md) |
| **Event sourcing** | ukládá se seznam událostí, stav se z nich počítá | [Proud událostí](eda/proud-udalosti.md) |
| **Exchange** | „třídírna“ v RabbitMQ – rozesílá zprávy do navázaných front | [Broker](eda/broker.md) |
| **Fanout** | typ exchange, který pošle kopii zprávy do každé navázané fronty | [Broker](eda/broker.md) |
| **Fold** | postupné „složení“ stavu z událostí – start s prázdným stavem a `Apply` pro každou | [Proud událostí](eda/proud-udalosti.md) |
| **Handler** | třída, která reaguje na jednu událost nebo zprávu | [Doménové události](eda/domenove-udalosti.md) |
| **Idempotence** | provést operaci jednou nebo víckrát má stejný výsledek | [Inbox](eda/inbox.md) |
| **Inbox** | tabulka zpracovaných zpráv – ochrana před dvojím zpracováním | [Inbox](eda/inbox.md) |
| **Integrační zpráva / kontrakt** | veřejná, verzovaná zpráva mezi službami (`ReserveStockV1`) | [Integrační zprávy](eda/integracni-zpravy.md) |
| **Kompenzace** | akce, která ruší účinek dřívějšího kroku (vrácení zboží) | [Sága](eda/saga.md) |
| **Modulární monolit** | jedna aplikace rozdělená na moduly s vlastními daty a jasnými hranicemi | [Moduly](architektura/moduly/index.md) |
| **Orchestrace / choreografie** | proces řídí jeden „dirigent“ / každý reaguje sám na události ostatních | [Sága](eda/saga.md) |
| **Outbox** | tabulka odchozích zpráv ukládaná v téže transakci jako data | [Outbox](eda/outbox.md) |
| **Poison message** | zpráva, jejíž zpracování opakovaně selhává; po 5 pokusech se odloží | [Outbox](eda/outbox.md) |
| **Projekce (read model)** | pohled na data poskládaný z událostí (kopie katalogu, časová osa) | [Kopie katalogu](eda/kopie-katalogu.md) |
| **Příkaz (command)** | zpráva „udělej“ s právě jedním příjemcem | [Integrační zprávy](eda/integracni-zpravy.md) |
| **Sága** | dlouhý proces přes více služeb složený z lokálních transakcí s kompenzacemi | [Sága](eda/saga.md) |
| **SOLID** | pět principů objektového návrhu | [SOLID](architektura/solid.md) |
| **Span / trasa (trace)** | jeden krok práce / strom kroků jednoho požadavku přes služby | [Observabilita](eda/observabilita.md) |
| **traceparent** | standardní hlavička W3C nesoucí ID trasy mezi procesy | [Observabilita](eda/observabilita.md) |
| **Wire tap** | „odposlech“ – fronta, která dostává kopii všech zpráv (audit) | [Broker](eda/broker.md) |

## Nasazení a provoz

| Pojem | Význam | Víc |
| --- | --- | --- |
| **Basic auth** | jednoduché přihlášení jménem a heslem na úrovni HTTP (v Caddy) | [HTTPS a přístup](nasazeni/https-a-pristup.md) |
| **CI / CD** | automatické sestavení a testy / automatické nasazení | [GitHub a CI/CD](github/index.md) |
| **cloud-init** | skript, který připraví novou VM při prvním startu | [Oracle Cloud](nasazeni/cloud-oci.md) |
| **Compartment** | „místnost“ v OCI pro zdroje jednoho projektu, s vlastními právy | [Oracle Cloud](nasazeni/cloud-oci.md) |
| **Container / image** | běžící instance aplikace / neměnný balík aplikace se vším potřebným | [Kontejnery](nasazeni/kontejnery.md) |
| **Environment (GitHub)** | sada secrets a pravidel pro nasazení do jednoho prostředí (`production`) | [CD](github/cd.md) |
| **Health check** | adresa (`/health`), na které služba hlásí svůj stav | [Monitoring](nasazeni/monitoring.md) |
| **IaC** | infrastruktura popsaná kódem (OpenTofu) místo klikání | [Infrastruktura jako kód](nasazeni/infrastruktura-jako-kod.md) |
| **IAM politika** | pravidlo „kdo smí co a kde“ v cloudu | [Oracle Cloud](nasazeni/cloud-oci.md) |
| **Instance principal** | identita VM – přihlásí se ke službám cloudu bez klíče | [Zálohy](nasazeni/zalohy.md) |
| **Multi-stage build** | Dockerfile s fází sestavení a malou výslednou image | [Kontejnery](nasazeni/kontejnery.md) |
| **Registry** | úložiště Docker image (GHCR) | [CI](github/ci.md) |
| **Reserved IP** | pevná veřejná IP adresa, která přežije novou VM | [Oracle Cloud](nasazeni/cloud-oci.md) |
| **Reverse proxy** | server před aplikací: HTTPS, přihlášení, přeposílání (Caddy) | [HTTPS a přístup](nasazeni/https-a-pristup.md) |
| **Rollback** | návrat na předchozí verzi po neúspěšném nasazení | [CD](github/cd.md) |
| **Ruleset** | pravidla GitHubu pro větev (povinný PR, povinné testy) | [Repozitář](github/repozitar.md) |
| **Secret** | tajná hodnota (heslo, klíč), která nesmí do gitu | [Secrets](github/secrets.md) |
| **Security list** | cloudový firewall – které porty jsou zvenku otevřené | [Oracle Cloud](nasazeni/cloud-oci.md) |
| **Smoke test** | rychlá kontrola po nasazení, že aplikace vůbec odpovídá | [CD](github/cd.md) |
| **State (OpenTofu)** | záznam, co OpenTofu v cloudu vytvořil a pod jakým ID | [Infrastruktura jako kód](nasazeni/infrastruktura-jako-kod.md) |
| **Vynucený příkaz (forced command)** | SSH klíč, který smí spustit jen jeden předem daný příkaz | [CD](github/cd.md) |
| **Volume** | trvalý disk kontejneru – data přežijí nový image | [Kontejnery](nasazeni/kontejnery.md) |
