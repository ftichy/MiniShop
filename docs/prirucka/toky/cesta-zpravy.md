---
title: Cesta jedné zprávy
summary: Detail doručení jedné integrační zprávy – outbox, RabbitMQ, consumer, inbox – včetně toho, co se děje při chybách.
order: 8
---

> **Shrnutí:** Zpráva má dvě „pojistky“: **outbox** u odesílatele (neztratí se) a **inbox** u příjemce
> (nezpracuje se dvakrát). Mezi nimi ji drží RabbitMQ.

## Šťastná cesta

```mermaid
sequenceDiagram
    autonumber
    participant H1 as Handler odesílatele
    participant OB as Outbox (DB odesílatele)
    participant OP as OutboxProcessor
    participant X as Exchange ReserveStockV1
    participant Q as Fronta minishop.inventory
    participant C as RabbitMqModuleConsumer
    participant D as IntegrationMessageDispatcher
    participant IB as Inbox (DB příjemce)
    participant H2 as Handler příjemce

    H1->>OB: INSERT (Kind = Integration, TraceParent)
    OP->>OB: vybrat nezpracované
    OP->>X: BasicPublish (persistent, hlavička traceparent)
    X-->>OP: potvrzení brokeru (publisher confirm)
    OP->>OB: ProcessedAtUtc = teď
    X->>Q: kopie do každé navázané fronty
    Q->>C: doručení
    C->>D: DispatchAsync(zpráva, modul)
    D->>IB: (MessageId, handler) už zpracováno?
    D->>H2: HandleAsync – v transakci s inboxem
    H2->>IB: COMMIT: změny + odškrtnutí
    C->>Q: ack – zpráva z fronty pryč
```

## Co když se něco pokazí

| Kde | Co se stane | Výsledek |
| --- | --- | --- |
| Broker neběží při publish | výjimka `IntegrationBusUnavailableException`, zpráva zůstane v outboxu, **nepočítá se jako pokus** | odejde, až broker naběhne |
| Pád odesílatele po publish, před označením | zpráva odejde znovu | inbox příjemce duplicitu přeskočí |
| Handler příjemce vyhodí výjimku | transakce se vrátí, *nack + requeue* po 2 s | zkouší se znovu (bez limitu) |
| Pád příjemce před ack | RabbitMQ zprávu doručí znovu (`redelivered`) | inbox duplicitu přeskočí |
| Nečitelná zpráva (neznámý typ, vadný JSON) | *nack bez requeue* | zahozena, v logu varování |
| Doménový handler selže 5× | outbox přestane zkoušet (*poison message*) | čeká na člověka, `/health` po 5 min *Degraded* |

## Kód

[OutboxProcessor](code:src/MiniShop.Infrastructure/Outbox/OutboxProcessor.cs) ·
[RabbitMqIntegrationBus](code:src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqIntegrationBus.cs) ·
[RabbitMqQueueConsumer](code:src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqQueueConsumer.cs) ·
[IntegrationMessageDispatcher](code:src/MiniShop.Infrastructure/Integration/IntegrationMessageDispatcher.cs) ·
[InboxGuard](code:src/MiniShop.Infrastructure/Inbox/InboxGuard.cs) ·
[testy outboxu](code:tests/MiniShop.Tests/Infrastructure/OutboxTests.cs)

## Související

[Outbox](../eda/outbox.md) · [Inbox](../eda/inbox.md) · [Broker](../eda/broker.md) · [Observabilita](../eda/observabilita.md)
