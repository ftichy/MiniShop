# Účty krok 12C: střet zájmů

Od kroku 12A může být zaměstnanec zároveň zákazník. Bez dalšího pravidla by si skladník mohl „doručit“ vlastní objednávku,
která nikdy neodešla, nebo podpora schválit vlastní vratku a vrátit si peníze. Firmy proto mají **pravidlo čtyř očí**:
vlastní případ vyřizuje kolega. Přirovnání: pokladní si nemarkuje vlastní nákup – zavolá kolegyni.

| Krok | Co |
| --- | --- |
| 12A | víc rolí na účet, převod adminů, zaškrtávání rolí, e-mail |
| 12B | správa po oblastech |
| **12C (tento)** | střet zájmů + upozornění na chybějící 2FA |

## Jak to funguje

1. **Pravidlo v aplikační vrstvě:** `ConflictOfInterest.EnsureNotOwn(vlastník, actingUserId)` – shodují-li se, `DomainException`
   „Vlastní objednávku musí vyřídit kolega (střet zájmů)“. Volají ho:
   - `OrderService.StartPackingAsync` / `ShipAsync` / `CompleteAsync` (expedice, i API `POST /api/orders/{id}/complete`);
   - `ReturnSagaOrchestrator.AcceptGoodsAsync` / `RejectAsync` (vratka – převzetí a zamítnutí);
   - `ReviewService.SetHiddenAsync` (moderace vlastního hodnocení).
2. **Kdo to dělá:** Web předá ID přihlášeného zaměstnance (`User.ToActingUserId()`). Systém (timeout worker, sága, testy)
   předává `null` – pravidlo se ho netýká.
3. **Obrazovky:** Expedice a Vratky u vlastní objednávky ukážou štítek „vaše objednávka – vyřídí kolega“ (`_OwnOrderBadge`)
   a akce schovají / zablokují. Server pravidlo hlídá i tak.
4. **Zrušení vlastní objednávky** zůstává povolené – to smí každý zákazník.
5. **Upozornění na 2FA** (z ověření 12B): zaměstnanec bez dvoufázového ověření vidí nahoře žlutý pruh s tlačítkem Nastavit
   a v menu položku Správa, která vede na `/Account/TwoFactor`. Dřív správu prostě neviděl.

## Nové soubory

- `src/MiniShop.Application/Abstractions/ConflictOfInterest.cs`
- `src/MiniShop.Web/Pages/Admin/_OwnOrderBadge.cshtml`

## Změněné soubory

`OrderService` + `IOrderService` (`actingUserId` u expedice), `ReturnSagaOrchestrator` (`actingUserId`, `EnsureNotOwnAsync`),
`ReviewService`, `OrderAccessExtensions` (`ToActingUserId`), stránky `Admin/Fulfilment` (štítek, zablokované akce),
`Admin/Returns` (štítek, skryté akce), `Admin/Reviews` (hláška), `OrdersController`, `_Layout` (upozornění a menu bez 2FA),
testy (`OrderServiceTests.StretZajmu_*`, `StaffRolesTests`), příručka (modul Accounts, historie), CLAUDE.md, roadmapa účtů.

## Rozhodnutí

- **Pravidlo v aplikační vrstvě, ne jen na stránce** – platí i pro API a pro každou budoucí obrazovku.
- **Systémové kroky bez pravidla** – timeout vratky nebo sága nejsou „zaměstnanec“, `actingUserId` je null.
- **Štítek na obrazovce podle e-mailu** (Expedice zná jen `OrderDto` s e-mailem) – jen pohodlí; rozhoduje server podle ID účtu.
- **Zrušení vlastní objednávky povolené** – je to akce zákazníka, ne zaměstnance.

## Známá omezení

- Správce uživatelů si smí přidělit i zaměstnanecké role sám sobě (rozhodnutí 12A); pravidlo čtyř očí pro role by vyžadovalo
  druhého správce a schvalování.
- Marketing smí použít slevový kód, který sám vytvořil – kupon není vázaný na autora.

## Jak ověřit

- `dotnet test` – `OrderServiceTests.StretZajmu_*` (expedice, vratka, hodnocení), `StaffRolesTests` (štítek v Expedici a
  odmítnutí, upozornění bez 2FA).
- Produkce: účet se Skladem + Zákazníkem si objedná → v Expedici u své objednávky vidí „vaše objednávka – vyřídí kolega“
  a zaškrtávátko je neaktivní; admin (jiný účet) ji zabalí normálně.

## Vyzkoušejte sami

1. V `ConflictOfInterest.EnsureNotOwn` dočasně vraťte hned `return;` – `StretZajmu_*` testy ukážou, co všechno by zaměstnanec
   mohl udělat sám sobě.
2. Přidejte novou roli účtu bez 2FA a otevřete úvodní stránku – nahoře je pruh s tlačítkem Nastavit.
