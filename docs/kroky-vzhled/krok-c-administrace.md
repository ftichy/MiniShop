# Vzhled krok C: Administrace

Poslední krok redesignu: stránky administrace (Produkty, Expedice, Vratky, Uživatelé, Tržby, Faktury, Košíky, Kupony,
Hodnocení, Zprávy, Průběh objednávky) se dají ovládat i z telefonu a vypadají jednotně. Adresy, formuláře, handlery
ani `data-track` se nezměnily.

## Jak to funguje

- **Jednotná hlavička** `_AdminHeader` (`AdminHeaderModel`): drobečková navigace „Správa › Stránka“ (u detailu
  uživatele a úpravy produktu i nadřazená stránka jako odkaz) a nadpis – nahradila `<h1>` na 12 stránkách.
- **Tabulky (16 na 10 stránkách)** v obalu `table-responsive admin-table`: karta s bílým pozadím a záhlavím
  malými písmeny; na telefonu se tabulka posouvá do strany uvnitř karty, stránka nepřetéká.
- **Expedice:** fronta *Na cestě* jako seznam (tlačítko Doručeno/Vyzvednuto vpravo, na telefonu pod textem), počty ve
  frontách jako jemné štítky.
- **Formuláře** zakládání produktu, kategorie, kuponu a úprava produktu v kartě; na telefonu hlavní tlačítko přes celou
  šířku (`.admin-form`).
- **Barvy:** štítky v administraci (`_RoleBadge`, `_LockBadge`, kupony, vratky, hodnocení, typy událostí v Průběhu
  objednávky – `JourneyModel.KindBadge`) mají `badge-soft-*`; pruhy grafu tržeb barvu značky (`.progress`).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/Ui/AdminHeaderModel.cs` | model hlavičky stránky administrace |
| `src/MiniShop.Web/Pages/Shared/_AdminHeader.cshtml` | drobečková navigace + nadpis |

## Změněné soubory

- `Pages/Admin/*/Index.cshtml` (CartInsights, Coupons, Fulfilment, Invoices, Messages, Returns, Reviews, Sales, Users),
  `Admin/Users/Detail.cshtml`, `_RoleBadge`, `_LockBadge`, `Pages/Products/Index.cshtml`, `Products/Edit.cshtml`,
  `Pages/Orders/Journey.cshtml(.cs)` – hlavička, obal tabulek, štítky, formuláře.
- `wwwroot/css/site.css` – `.admin-table`, `.admin-title`, `.admin-form`, `.list-group`, `.progress`, `badge-soft-dark`.
- Příručka: Vzhled a mobil (sekce Administrace), historie.

## Rozhodnutí v tomto kroku

- **Tabulky zůstaly tabulkami** (jen responzivní obal) – admin porovnává sloupce (ceny, stavy, počty), na telefonu je
  posouvání do strany přijatelné. Na seznam se převedla jen místa, kde je hlavní akce na konci řádku (Expedice).
- **Obal přidán jednotně** (`table-responsive admin-table`) – nová tabulka v administraci = stejný obal (pravidlo v příručce).
- **Drobečková navigace místo zpětných odkazů** – na telefonu je menu Správa schované, drobečky ukážou, kde admin je.

## Známá omezení

- Tabulky na telefonu vyžadují posouvání do strany (Produkty, Uživatelé, Zprávy).
- Přehrání objednávky (`/Orders/Replay`) má jen nové barvy – ovládání přehrávače se na úzkém displeji zalomí, rozložení
  zůstalo (logika přehrávače beze změny).
- Tmavý režim není (případný krok D).

## Jak ověřit

- `dotnet test MiniShop.slnx` – matice oprávnění (menu Správa, `data-track`), Expedice, Vratky, uživatelé, Zprávy.
- Snímky administrace v headless Edge (500 px a 1366 px): Produkty, Expedice, Uživatelé, Zprávy, Tržby – tabulky
  v kartě se posouvají uvnitř, stránka nepřetéká; testovací účet dostal roli Admin jen dočasně v lokální DB.

## Vyzkoušejte sami

1. Na telefonu otevřete ☰ → Správa → Produkty – nad nadpisem je „Správa / Produkty“, tabulka se posouvá prstem.
2. Expedice: objednávku zabalte, odešlete a ve frontě *Na cestě* klepněte na Doručeno – vše bez posouvání do strany.
3. Uživatelé → detail uživatele – drobečky „Správa / Uživatelé / Jméno“ vedou zpět na seznam.
