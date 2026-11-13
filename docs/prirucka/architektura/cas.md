---
title: Jak zobrazovat čas
summary: V databázi a ve zprávách je vždy UTC, člověku se ukazuje český čas (Europe/Prague) přes jediný pomocník CzechTime.
order: 10
---

> **V jedné větě:** Uložený čas je jako světový čas na nádražních hodinách – pro všechny stejný. Až když ho ukazujeme
> člověku, přepočteme ho na místní (český) čas, a to vždy na jednom místě.

## Pravidlo

- Databáze, kontrakty i doména mají čas **vždy v UTC** (`…AtUtc`).
- Zobrazení (stránky, e-maily, PDF faktur) jde **jen přes** [`CzechTime`](code:src/MiniShop.Application/Abstractions/CzechTime.cs):
  v Razoru `@x.CreatedAtUtc.ToCzechString()` (výchozí formát `d. M. yyyy H:mm`, např. `13. 11. 2026 9:14`) nebo
  `.ToCzechString("d. M. yyyy")`, v kódu `CzechTime.Format(…)` / `CzechTime.FromUtc(…)`.
- **Nikdy `DateTime.ToLocalTime()`** – ten bere časovou zónu stroje. Kontejner v produkci běží v UTC, takže časy by byly
  o hodinu (v létě o dvě) pozadu; na Windows s českou zónou by se chyba lokálně neprojevila. Hlídá to
  `TimeConventionTests`.

## Proč ne `TZ` v kontejneru

Zóna procesu by závisela na prostředí (jiný hosting, test, CI v UTC) a změnila by i čas v logech. `CzechTime` je nezávislý na
zóně stroje a jde ho otestovat (`CzechTimeTests`: zima, léto, oba přechody času, `Unspecified` z databáze = UTC).

## Chybějící časová zóna

Zónu `Europe/Prague` potřebuje systém (balíček tzdata, v Linuxu; ve Windows ji dodá ICU). Web ji při startu ověří
(`CzechTime.EnsureAvailable`) – chybí-li, **nespustí se** a v logu je jasná hláška. Tiché přepnutí na UTC by byla přesně ta chyba,
kterou pravidlo odstraňuje.
