# Krok 13: Úklid a odolnost zpráv

Zprávy se doručovaly spolehlivě, ale provoz měl tři slabiny: tabulky outboxu a inboxu jen rostly, outbox zkoušel
selhanou zprávu pětkrát za deset sekund a pak ji vzdal, a consumer RabbitMQ vracel vadnou zprávu do fronty donekonečna
(nečitelnou zahodil). Teď se zpracované záznamy **uklízejí**, opakuje se s **exponenciálním backoffem** a vadné zprávy
skončí v **dead-letter frontě**, odkud je admin vrátí. Plán: [docs/EDA-roadmap.md](../EDA-roadmap.md).

## Jak to funguje

Přirovnání: pošta archivuje obálky jen pár týdnů, doručovatel při nezastižení přijde zítra a pozítří a nedoručitelný
dopis skončí na oddělení nedoručitelných zásilek, ne v koši.

```
 outbox:  selhání ─► NextAttemptAtUtc = teď + 5 / 10 / 20 / 40 s ─► … 5. selhání ─► poison (Zprávy → Zkusit znovu)
 fronta:  selhání ─► kopie s x-minishop-attempt + pauza 2 / 4 / 8 / 16 s ─► 5. selhání ─► minishop.{modul}.dead
          nečitelná zpráva ─► rovnou .dead                                  (Zprávy → Vrátit do fronty)
 úklid:   MessageRetentionWorker každou hodinu: zpracovaný outbox > 7 dní, inbox > 30 dní ─► smazat
 hlídání: health check dead-letters (odložená zpráva = Degraded) + outbox (stará nezpracovaná = Degraded) ─► UptimeRobot
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Retence** | jak dlouho držet už nepotřebná data | `MessageRetentionWorker`, `Messaging:Retention` |
| **Exponenciální backoff** | pauza mezi pokusy roste – chvilkový výpadek přežije, vadná zpráva neblokuje | `OutboxProcessor.RetryDelay`, `RabbitMqDeadLetters.RetryDelay` |
| **Poison message** | zpráva, která nejde zpracovat ani po opakování | outbox: `Attempts` = 5 |
| **Dead-letter fronta** | odkladiště vadných zpráv – neztratí se, neběží v kruhu | `minishop.{modul}.dead` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Infrastructure/Outbox/MessageStore.cs` | úklid, přehled a opakování zpráv jednoho modulu (`IMessageStore`) |
| `src/MiniShop.Infrastructure/Hosting/MessageRetentionWorker.cs` | úklid každou hodinu (+ `MessageRetentionSettings`) |
| `src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqDeadLetters.cs` | počet pokusů v hlavičce, backoff, dead-letter fronta |
| `src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqDeadLetterService.cs` | počty a vrácení z dead-letter front, `DeadLetterHealthCheck` |
| `src/MiniShop.Web/Pages/Admin/Messages/Index.cshtml(.cs)` | stránka Zprávy (jen admin) |
| migrace `OutboxBackoffAndRetention` (6 databází s outboxem) | sloupec `NextAttemptAtUtc`, index `ProcessedMessages.ProcessedAtUtc` |
| `docs/prirucka/eda/provoz-zprav.md`, tento souhrn | dokumentace |
| `tests/…/Infrastructure/MessageMaintenanceTests.cs` | úklid, poison, backoff, hlavičky |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/Outbox/OutboxMessage.cs`, `OutboxProcessor.cs`, `Persistence/Configurations/*` | `NextAttemptAtUtc`, backoff, indexy |
| `Infrastructure/Integration/RabbitMq/RabbitMqQueueConsumer.cs` | omezené opakování kopií s hlavičkou, dead-letter místo zahození a nekonečného requeue |
| `Infrastructure/DependencyInjection.cs`, `Hosting/MessagingServiceCollectionExtensions.cs` | registrace `IMessageStore`, worker, dead-letter služba a health check |
| `Web/…/appsettings.json`, `Inventory.Api/appsettings.json` | sekce `Messaging:Retention` |
| `Web/Pages/Shared/_Layout.cshtml` | menu **Zprávy** |
| příručka (`eda/outbox`, `inbox`, `broker`, `index`, `nasazeni/monitoring`, `historie`), `docs/EDA-roadmap.md` | dokumentace |
| `tests/…/OutboxTests.cs`, `OrderServiceTests.cs`, `AuthorizationMatrixTests.cs` | testy počítají s backoffem (posouvají čas), oprávnění |

## Rozhodnutí v tomto kroku

- **Retence inboxu (30 dní) delší než outboxu (7 dní)** – smazaný záznam inboxu by nepoznal duplicitu; opakování
  trvá minuty, lhůta dny.
- **Poison a nezpracované zprávy se nemažou nikdy** – čekají na člověka.
- **Opakování ve frontě kopií, ne „nack + requeue“** – jen tak jde počítat pokusy; duplicitu při pádu zachytí inbox.
- **Existující fronty beze změny argumentů** (žádné `x-dead-letter-exchange`) – RabbitMQ by změnu u existující fronty
  odmítl a nasazení by spadlo. Dead-letter fronty jsou nové obyčejné fronty.
- **Nedostupný broker není Degraded** v health checku dead-letter – `/health` RabbitMQ záměrně nehlídá; dlouhé
  čekání zpráv nahlásí health check outboxu.
- **Pauza čekáním v consumeru** (blokuje jedno místo z prefetch 10) – jednoduché, bez plánovače zpráv.

## Známá omezení

- Stránka Zprávy ukazuje moduly služby Web; sklad (Inventory.Api) svůj stav hlásí jen přes `/health/inventory` a log.
- Vrácení z dead-letter fronty je po dávkách 500 zpráv a bez výběru jednotlivých zpráv (obsah ukáže RabbitMQ Management).
- Republikace kopie při opakování jde bez publisher confirms – při pádu brokeru v tu chvíli by se kopie mohla ztratit
  (originál je ale už potvrzený jen po úspěšném publikování na kanálu).
- Worker úklidu i outboxu dál počítá s jednou instancí služby.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu migrace `…_OutboxBackoffAndRetention` (Web i Inventory.Api); v RabbitMQ UI nové fronty `minishop.*.dead`.
2. Admin → **Zprávy**: všude 0, dead-letter fronty s nulou.
3. Zastav Mailpit (kontejner) a zruš objednávku → v logu „selhala (1/5), zkusí se znovu“ s rostoucími pauzami;
   spusť Mailpit do minuty → e-mail dorazí. Necháš-li ho vypnutý déle → zpráva v Zprávy jako poison → **Zkusit znovu**.
4. V RabbitMQ UI publikuj do `minishop.ordering` zprávu s typem `Nesmysl` → skončí v `minishop.ordering.dead`,
   `/health` hlásí Degraded → **Vrátit do fronty** (a znovu skončí v dead-letter – zpráva je vadná).

### Produkce

Nic nového k nastavení – migrace a nové fronty vzniknou samy. Úklid poběží hned po startu (smaže zpracované zprávy
starší než 7 dní) – zálohy se zmenší.

## Vyzkoušejte sami

- Co by se stalo, kdyby retence inboxu byla 1 minuta a RabbitMQ doručil zprávu po 2 minutách znovu?
- Proč se nedostupnost brokeru nepočítá jako pokus outboxu, ale chyba handleru ano?
- Přidej do backoffu náhodný rozptyl (*jitter*) – proč pomáhá, když selže hodně zpráv naráz?
- Jak bys stránku Zprávy rozšířil o sklad, aniž by Web sahal do `inventory.db`?
