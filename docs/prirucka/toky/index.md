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
| [Zrušení a vyřízení](zruseni-a-vyrizeni.md) | kompenzace po zrušení potvrzené objednávky |
| [Změna produktu](zmena-produktu.md) | sklad → kopie katalogu v Orderingu |
| [Obnova katalogu](obnova-katalogu.md) | prázdná nebo neúplná kopie katalogu po startu |
| [Změna dostupnosti](zmena-dostupnosti.md) | rezervace/naskladnění změní pásmo dostupnosti → výloha |
| [Cesta jedné zprávy](cesta-zpravy.md) | outbox → RabbitMQ → inbox, včetně chyb |
| [Průběh formuláře](prubeh-formulare.md) | sledování a přehrání formuláře objednávky |

Diagramy v Markdownu jsou psané v [Mermaidu](https://mermaid.js.org/syntax/sequenceDiagram.html). Velký diagram
zvětšíte tlačítkem **Plná velikost** v jeho pravém horním rohu.
