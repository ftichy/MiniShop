# Proud krok B1: Proud zpráv

Plán: [Proud-roadmap](../Proud-roadmap.md). Vedle front, ze kterých si moduly zprávy berou (a zprávy mizí), teď každá
zpráva skončí i v **proudu** `minishop.events` – logu, ze kterého se jen čte od zvolené záložky. Stránka **Proud zpráv**
ukáže celou pouť objednávky napříč moduly. Teorie: příručka [Proud zpráv](../prirucka/eda/proud-zprav.md).

## Jak to funguje

```
exchange každého kontraktu (fanout) ──► fronty modulů (rozdělí, smažou)  +  proud minishop.events (zůstává)
   proud: x-queue-type stream, x-max-age 14D, x-max-length-bytes 1 GB, segmenty 50 MB
   založí Web po startu (EventStreamSetupWorker, vedoucí kopie, idempotentně – nový kontrakt se naváže s nasazením)
/Admin/EventStream (Provoz): od kdy + typ + číslo objednávky
   → RabbitMqEventStreamReader: BasicQos + BasicConsume(x-stream-offset = čas) → čte do „ticha“ 0,8 s → odpojí se
   → filtr podle času faktu, typu, OrderId z JSONu → nejvýš 2 000 zpráv, JSON s maskou osobních údajů
```

## Nové soubory

- `src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqEventStream.cs` – založení proudu, čtečka, `IEventStreamReader`, záznamy.
- `src/MiniShop.Infrastructure/Integration/PersonalDataMask.cs` – maska osobních údajů v JSONu zprávy.
- `src/MiniShop.Infrastructure/Hosting/EventStreamSetupWorker.cs` (+ `AddEventStream`).
- `src/MiniShop.Web/Pages/Admin/EventStream/Index.cshtml(.cs)` – stránka Proud zpráv.
- Testy: `Infrastructure/EventStreamTests.cs` (skutečný RabbitMQ – Testcontainers), `Infrastructure/PersonalDataMaskTests.cs`,
  `Web/EventStreamPageTests.cs`, `TestDoubles/FakeEventStreamReader.cs`.
- Příručka `eda/proud-zprav.md`; `docs/Proud-roadmap.md`.

## Změněné soubory

- `Program.cs` – `AddEventStream()`; `_Layout.cshtml` – menu Provoz → Proud zpráv; detail objednávky – odkaz „Zprávy v proudu“ (Provoz).
- Testy: `AuthorizationMatrixTests`, `StaffAreaTests`, `WorkerRegistrationTests`, `MiniShopWebFactory` (podvržená čtečka); balíček `Testcontainers.RabbitMq`.
- Příručka: `eda/index.md` (oprava seznamu z kroku F3a – chyběl bod 12), `treti-strany/knihovny.md`.

## Rozhodnutí

- **AMQP, ne nativní protokol streamů** – bez pluginu, portu 5552 a knihovny `RabbitMQ.Stream.Client`; „konec proudu“
  se pozná podle ticha (nativní protokol by ho řekl přímo).
- **Retence 14 dní / 1 GB**, segmenty 50 MB; proud není zdroj pravdy, do zálohy nepatří.
- **Čtení od času, limit 2 000 zobrazených / 50 000 prohledaných zpráv.**
- **Maskování podle jména pole** – pojistný test `KazdePoleSOsobnimUdajem_MaskaZna` spadne u nového podezřelého pole.
  Maskuje i `Reason` (důvod vratky = volný text zákazníka) – tím i systémové důvody zamítnutí; přijatelná cena.
- **Bez metriky velikosti proudu** – RabbitMQ u proudu přes AMQP počet zpráv nehlásí (vrací 0, ověřeno testem);
  velikost je v RabbitMQ UI.
- Auditní fronta `minishop.audit` zůstává (případně krok B3).

## Známá omezení

- Historie začíná nasazením B1 (starší zprávy už z front zmizely).
- Čtení široké historie (7 dní) při velkém provozu chvíli trvá – limit prohledaných zpráv ho ukončí.

## Jak ověřit

- `dotnet test` – `EventStreamTests` (Docker), `PersonalDataMaskTests`, `EventStreamPageTests`, matice oprávnění.
- Po nasazení: v logu Webu „Proud zpráv minishop.events připraven“; RabbitMQ UI → Queues → `minishop.events` typ *stream*;
  Správa → Provoz → **Proud zpráv** → číslo objednávky → celá pouť.

## Vyzkoušejte sami

1. Udělejte objednávku a otevřete Proud zpráv s jejím číslem – zprávy od `OrderPlacedV2` po `InvoiceIssuedV1`.
2. Otevřete stránku znovu – zprávy jsou pořád tam (proud se čtením nemění), na rozdíl od front.
3. JSON zprávy `OrderPlacedV2` – jméno, e-mail a adresa jsou `•••`, položky a ceny zůstaly.
