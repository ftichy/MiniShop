# Víc skladů a dělené zásilky – roadmapa (směr C)

E-shop má dva sklady (Praha, Brno). Když jeden sklad nemá všechno, objednávka se rozdělí na **balíky** – každý zabalí
svůj sklad, každý má vlastní zásilku u dopravce a objednávka je doručená, až dorazí všechny.

Přirovnání: objednáte si nábytek a přijedou dvě dodávky z různých skladů v různé dny. Obchod musí hlídat obě
a „doručeno“ je až po té druhé.

## Co se naučíme

1. **Přidělení zásob (sourcing)** – odkud se zboží vezme, rozhoduje sklad (zná zásoby), Ordering se dozví jen výsledek.
2. **Hranice agregátu** – balík patří do objednávky (`Order` → `OrderParcel`); pravidlo „doručeno až po všech balících“
   hlídá jeden agregát.
3. **Scatter-gather** – jeden příkaz se rozpadne na N zásilek a čeká se na všechny odpovědi (proměnlivý počet účastníků).
4. **Částečné selhání** – vrácený balík se kompenzuje jen svou částí (navazuje na vratku po částech, e-shop krok 22).
5. **Expand → contract** – nové verze kontraktů od konce, produkce po každém kroku funguje.

## Rozhodnutí (22. 11. 2026)

- Dva sklady: **Praha** (dnešní zásoby, výchozí) a **Brno** (prázdný, naskladní se ručně).
- Přidělení: **jeden sklad, když to jde** (méně balíků), jinak rozdělit s co nejméně balíky; při shodě sklad s nižším ID.
- Doprava se platí **jednou** za objednávku, i když přijdou dva balíky.
- Zaměstnanci: v Expedici **filtr „sklad“**, žádné nové role.
- Zrušení: **jen dokud žádný balík neodešel**.

## Kroky

| Krok | Obsah | Stav |
| --- | --- | --- |
| C1 | Sklady v Inventory: `Warehouse`, zásoba po skladech (`WarehouseStock`), migrace dnešních zásob do Prahy, `StockQuantity` = součet (výloha beze změny), naskladnění do vybraného skladu, rezervace s `WarehouseId` (přidělení), vrácení do skladu původu. Zákazník nic nepozná. | hotovo (23. 11. 2026, ověřeno v produkci) |
| C2 | Balíky v objednávce (expand): `StockReservedV2` s rozdělením → `OrderParcel` v Orderingu, Expedice po skladech; předání zatím za celou objednávku. | v PR |
| C3 | Dělené zásilky: předání po balících `CreateShipmentV2`, `Shipment` podle `OrderId + ParcelNumber`, odesláno = poslední balík odešel, doručeno = dorazily všechny; detail s balíky, e-mail a push za balík. | plán |
| C4 | Výjimky a úklid: vrácený balík `ParcelReturnedToSenderV1` (naskladnit, refund a dobropis jen jeho část), zrušení jen před prvním balíkem, contract starých cest, generátor provozu, příručka a sekvenční diagram. | plán |

Souhrny kroků: `docs/kroky-eshop/krok-28…` (C1 = e-shop krok 28A, C2 = 28B, C3 = 28C, C4 = 28D).
