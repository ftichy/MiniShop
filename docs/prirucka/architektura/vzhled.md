---
title: Vzhled a mobil
summary: Design systém nad Bootstrapem 5.3 (CSS proměnné, barva značky indigo), hlavička s menu pro mobil, karta produktu jako sdílený partial, ikony jako inline SVG – vše lokálně, bez CDN.
order: 9
---

> **V jedné větě:** MiniShop dál stojí na **Bootstrapu 5.3** (lokálně ve `wwwroot/lib`), vlastní vzhled je jen sada
> **CSS proměnných** a pár komponent v [`site.css`](code:src/MiniShop.Web/wwwroot/css/site.css). Stránky se přizpůsobí
> telefonu (*mobile first*) – menu se sbalí do „hamburgeru“, filtry výlohy se vysunou z boku.

## Design systém

Barvy, zaoblení a stíny jsou proměnné na jednom místě (`:root` v `site.css`) – změna vzhledu = změna proměnné,
ne hledání v šablonách. Proměnné `--ms-*` jsou naše, `--bs-*` přepisují výchozí hodnoty Bootstrapu.

| Proměnná | Hodnota | Kde je vidět |
| --- | --- | --- |
| `--ms-brand` | `#4f46e5` (indigo) | tlačítka, odkazy, aktivní stránka, odznak košíku |
| `--ms-canvas` / `--ms-surface` | světle šedá / bílá | pozadí stránky / karty, hlavička |
| `--ms-radius` | `0.875rem` | zaoblení karet |
| `--ms-tap` | `2.75rem` (44 px) | minimální dotyková plocha (ikony v hlavičce, štítky kategorií) |

Písmo je systémové (`system-ui`) – nic se nestahuje a na každém zařízení vypadá „domácky“.

## Hlavička a patička

```mermaid
flowchart LR
    subgraph mobil["Telefon (< 992 px)"]
        m1["Logo"] --- m2["Košík"] --- m3["☰ menu"]
        m3 -.->|rozbalí| m4["Obchod · Objednávky · Správa · Hledání · Účet"]
    end
    subgraph pc["Počítač"]
        p1["Logo · Obchod · Objednávky · Správa ▾ · Dokumentace"] --- p2["Hledání"] --- p3["Účet ▾"] --- p4["Košík"]
    end
```

- [_Layout.cshtml](code:src/MiniShop.Web/Pages/Shared/_Layout.cshtml): Bootstrap `navbar-expand-lg` + `collapse`;
  košík je **mimo** sbalovanou část, takže je vidět i na telefonu.
- Administrace má jedinou rozbalovací nabídku **Správa** (Katalog a objednávky / Přehledy / Provoz).
- Účet ([_LoginPartial](code:src/MiniShop.Web/Pages/Shared/_LoginPartial.cshtml)): rozbalovací nabídka – objednávky,
  nastavení účtu a e-mailů, odhlášení.
- Rozbalování obstarává **Bootstrap JS** (`bootstrap.bundle.min.js` lokálně, ověřený otiskem SRI z oficiálního vydání) –
  soubor, ne inline skript, takže [CSP](moduly/accounts.md) `script-src 'self'` zůstává.

## Výloha a úvod

- **Karta produktu** je jeden partial [_ProductCard](code:src/MiniShop.Web/Pages/Shared/_ProductCard.cshtml)
  (model [ProductCardModel](code:src/MiniShop.Web/Catalog/ProductCardModel.cs)) – stejná ve výloze i na úvodu.
  Celá karta je odkaz na detail, tlačítko „Do košíku“ zůstává samostatně klikací.
- Na telefonu **dvě karty vedle sebe**, cena na jednom řádku a tlačítko pod ní přes celou šířku.
- **Filtry výlohy** jsou `offcanvas-lg`: na počítači sloupec vlevo, na telefonu panel vysunutý tlačítkem *Filtry*.
  Formulář je pořád obyčejný GET (`/Shop?q=…&category=…`) – funguje i bez JavaScriptu a jde sdílet odkazem.
- **Štítky kategorií** nad výsledky (na telefonu vodorovně posuvné).
- **Úvod** ([Index.cshtml.cs](code:src/MiniShop.Web/Pages/Index.cshtml.cs)) je pro zákazníka: hlavní nabídka,
  výhody, kategorie a čtyři doporučené produkty (nejlépe hodnocené skladem) z kopie katalogu.
- **Detail produktu**: obrázek vlevo, „nákupní box“ (cena, dostupnost, Do košíku, doprava, vrácení) vpravo;
  na telefonu pod sebou s tlačítkem přes celou šířku.

## Nákupní cesta (vzhled krok B)

- **Košík** ([Cart/Index](code:src/MiniShop.Web/Pages/Cart/Index.cshtml)): řádky jako karty (obrázek, název, dostupnost,
  množství, cena), souhrn vpravo (na počítači zůstává při rolování na místě), na telefonu **spodní lišta** s cenou
  a tlačítkem *K pokladně*.
- **Pokladna** ([Checkout/Index](code:src/MiniShop.Web/Pages/Checkout/Index.cshtml)): formulář vlevo, souhrn se slevovým
  kódem vpravo; na telefonu nejdřív souhrn (co kupuji), pak formulář a *Objednat*. Doprava a platba jako velké
  klikací **karty** (`.choice-card`, zvýraznění přes `:has(input:checked)`). `fieldset` + `legend`, ID polí, `name`
  a `data-track` zůstaly – sledování a [přehrání](../eda/proud-udalosti.md) pokladny je potřebuje
  (kopie v `Replay.cshtml` má stejné karty).
- **Objednávky**: seznam jako karty (číslo, datum, stav, položky, celkem, akce), detail s položkami a údaji v kartách
  a **průběhem jako svislou časovou osou** (`.timeline`). Štítky stavů mají jemné barvy (`OrderStatusLabels.Badge`).
- **Účet**: všechny stránky ve složce `Pages/Account` mají vlastní layout
  [_AccountLayout](code:src/MiniShop.Web/Pages/Shared/_AccountLayout.cshtml) (nastavený v `Account/_ViewStart.cshtml`) –
  úzká karta uprostřed s logem, tlačítka přes celou šířku. Vnořený layout přeposílá sekce `Scripts` a `Styles`.

## Administrace (vzhled krok C)

- **Hlavička stránky** = partial [_AdminHeader](code:src/MiniShop.Web/Pages/Shared/_AdminHeader.cshtml)
  (model [AdminHeaderModel](code:src/MiniShop.Web/Ui/AdminHeaderModel.cs)): drobečková navigace „Správa › …“ a nadpis.
  Na telefonu je menu Správa schované v ☰ – drobečky ukážou, kde admin je. Nová stránka administrace =
  `<partial name="_AdminHeader" model='new MiniShop.Web.Ui.AdminHeaderModel("Název")' />`.
- **Tabulky** jsou v obalu `table-responsive admin-table` (karta): na telefonu se posouvají do strany uvnitř karty,
  stránka zůstane v šířce displeje. **Nová tabulka v administraci = stejný obal.**
- **Expedice**: fronty *K odeslání* a *Na cestě* jako seznam – tlačítka Odeslat / Doručeno jsou na telefonu bez posouvání.
- **Formuláře** zakládání (produkt, kupon, kategorie) a úprava produktu v kartě, na telefonu hlavní tlačítko přes
  celou šířku (`.admin-form`).
- Štítky (role, blokace, stav kuponu, typy událostí průběhu objednávky) mají stejné jemné barvy jako zbytek webu
  (`badge-soft-*`), pruhy grafu tržeb barvu značky.

## Ikony

[Icons.cs](code:src/MiniShop.Web/Ui/Icons.cs) – pár ikon jako inline SVG (tvary z Bootstrap Icons, licence MIT):
`@Icons.Cart`, `@Icons.Search`, `@Icons.Menu`… Bez ikonového fontu a bez CDN; barvu berou z textu, velikost z písma.

## Pravidla

- Nová barva nebo rozměr = proměnná v `:root`, ne pevná hodnota v šabloně.
- Soubory z `wwwroot` vždy s `asp-append-version="true"` (jinak prohlížeč po nasazení použije starou verzi z cache).
- Žádné inline `<script>` ani `onclick=` (CSP, hlídá `HardeningTests`); chování = soubor ve `wwwroot/js`
  nebo atributy Bootstrapu (`data-bs-toggle`).
- `data-track` a ID polí se nemění – podle nich [sleduje a přehrává](../eda/proud-udalosti.md) průběh pokladny.
- Ověření na mobilu: DevTools (Ctrl+Shift+M) nebo snímek v headless Edge
  (`msedge --headless=new --window-size=500,1200 --screenshot=… https://localhost:7180/Shop` – menší šířku Edge nedovolí).
