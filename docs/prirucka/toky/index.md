---
title: Sekvenční diagramy
summary: Hlavní scénáře krok za krokem – kdo komu co posílá a v jakém pořadí.
order: 30
---

> **Jak číst sekvenční diagram:** čas běží **shora dolů**, každý svislý pruh je jeden účastník (služba, třída,
> databáze). Plná šipka = volání nebo odeslání, čárkovaná = odpověď. Barevné rámečky oddělují fáze, které mezi
> sebou mají **časovou mezeru** (čekání na kolo outbox workeru, doručení přes RabbitMQ).

## Přehled scénářů

| Scénář | Co ukazuje |
| --- | --- |
| [Vytvoření objednávky](vytvoreni-objednavky.md) | hlavní tok: formulář → sága → sklad → potvrzení |
| [Zamítnutí objednávky](zamitnuti-objednavky.md) | sklad nemá dost zboží |
| [Timeout a kompenzace](timeout-a-kompenzace.md) | sklad neodpověděl včas; pozdní rezervace se vrací |
| [Zrušení objednávky](zruseni-a-vyrizeni.md) | zrušení kdykoli před odesláním; sága dožene pozdní rezervaci i platbu |
| [Expedice](expedice.md) | balení → odeslání (`OrderShippedV1`, sklad vydá rezervaci) → doručení; souběh odeslat × zrušit |
| [Platba kartou](platba.md) | čekání na platbu, podepsaný webhook, zamítnutí a timeout, vrácení pozdní platby |
| [Změna produktu](zmena-produktu.md) | sklad → kopie katalogu v Orderingu |
| [Obnova katalogu](obnova-katalogu.md) | prázdná nebo neúplná kopie katalogu po startu |
| [Změna dostupnosti](zmena-dostupnosti.md) | rezervace/naskladnění změní pásmo dostupnosti → výloha |
| [Cesta jedné zprávy](cesta-zpravy.md) | outbox → RabbitMQ → inbox, včetně chyb |
| [Průběh formuláře](prubeh-formulare.md) | sledování a přehrání pokladny (staré proudy: formulář objednávky) |
| [Pokladna](pokladna.md) | košík → objednávka v jedné transakci; dvojí odeslání, souběh, změna ceny |
| [Opuštěný košík](opusteny-kosik.md) | reakce na „ticho“: worker → `CartAbandonedV1` → připomínka jen se souhlasem |
| [Vrácení zboží](vraceni-zbozi.md) | druhá sága: žádost → převzetí adminem → naskladnění → vrácení peněz; zamítnutí a vypršení |
| [Slevové kódy](slevove-kody.md) | kód v pokladně, poslední použití při souběhu (verze kuponu), vrácení použití po zamítnutí/zrušení |
| [Hodnocení produktu](hodnoceni-produktu.md) | kopie nákupů rozhodne, kdo smí hodnotit; souhrn hodnocení do kopie katalogu a výlohy |
| [Vystavení faktury](faktura.md) | po zaplacení (karta) nebo doručení (dobírka) faktura z nepřerušené řady, e-mail s PDF v příloze |

Diagramy v Markdownu jsou psané v [Mermaidu](https://mermaid.js.org/syntax/sequenceDiagram.html). Velký diagram
zvětšíte tlačítkem **Plná velikost** v jeho pravém horním rohu.
