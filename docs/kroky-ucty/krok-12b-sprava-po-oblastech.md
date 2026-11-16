# Účty krok 12B: správa po oblastech

V kroku 12A dostali zaměstnanci dílčí role, ale každá ještě otevřela celou správu. Teď každá role vidí a smí jen svou
oblast – skladník Expedici a naskladnění, účetní faktury a tržby, marketing kupony… Přirovnání: v 12A dostal každý
svazek klíčů, v 12B se vyměnily zámky ve dveřích, takže každý klíč odemkne jen své dveře.

| Krok | Co |
| --- | --- |
| 12A | víc rolí na účet, převod adminů, zaškrtávání rolí, e-mail |
| **12B (tento)** | politika pro každou oblast, menu podle rolí, akce podle oblasti, test všech rolí × stránek; úklid role Admin |
| 12C | střet zájmů – zaměstnanec nevyřizuje vlastní objednávku, vratku ani hodnocení svého nákupu |

## Jak to funguje

1. **Politiky oblastí** (`AuthorizationPolicies`): `RequireUserAdmin`, `RequireCatalog`, `RequireStock`, `RequireFulfilment`,
   `RequireReturns`, `RequireOrderOverview`, `RequireSupport`, `RequireMarketing`, `RequireInvoices`, `RequireAccounting`,
   `RequireOperations`, `RequireCatalogSync`. Role každé politiky jsou na jednom místě – `StaffPolicies.Roles`; Web je
   zaregistruje v cyklu (každá = role + 2FA). `RequireAdmin` = kterákoli zaměstnanecká role (zámek `/Admin`, štítek, 2FA).
2. **Stránky** mají `[Authorize(Policy = …)]` své oblasti. Stránky s víc akcemi ověřují v akci:
   - Produkty: stránka `RequireStock` (sortiment i sklad), zakládání, mazání a kategorie `RequireCatalog` (sklad je nevidí);
   - Vratky: stránka `RequireReturns`, **Přijato** `RequireFulfilment`, **Zamítnout** `RequireSupport`;
   - Objednávky: všechny = `RequireOrderOverview`, zrušit cizí = podpora (`ToCancelAccess`), průběh = podpora,
     obnova katalogu = `RequireCatalogSync`.
3. **Menu Správa** (`_Layout`) ukáže jen položky, na které uživatel má politiku; prázdné skupiny vynechá.
4. **Vlastní role platí hned:** po uložení vlastních rolí `SignInManager.RefreshSignInAsync` vydá novou cookie (oprava
   z ověření 12A – dřív bylo potřeba se odhlásit).
5. **Úklid (contract):** migrace `RemoveLegacyAdminRole` smaže původní roli Admin (pojistka: kdo ji má, nejdřív dostane
   zaměstnanecké role), kód `LegacyAdmin` zmizel.

## Nové soubory

- `tests/MiniShop.Tests/Web/StaffAreaTests.cs` – tabulka stránka/akce → role, test všech 7 rolí; menu a produkty pro sklad
- Migrace `RemoveLegacyAdminRole` (Accounts, jen ruční SQL)

## Změněné soubory

`AuthorizationPolicies` (+ `StaffPolicies`), `AccountsServiceCollectionExtensions` (registrace v cyklu), `OrderAccessExtensions`
(`ToOrderAccess` jen přehled objednávek, `ToCancelAccess`), stránky Admin/* (politiky), `Products/Index` (akce a formuláře
jen sortiment), `Admin/Returns` (Přijato / Zamítnout), `Orders/Index` a `Orders/Detail` (odkazy a Zrušit podle oblasti),
`Orders/Journey`, `Orders/Replay`, `Orders/Invoice` (`RequireInvoices`), `JourneysController`, `OrdersController`,
`_Layout` (menu), `Admin/Users/Detail` (`RefreshSignInAsync`), `AccountRoles`, `AccountAdministration`, `AccountDeletion`,
`TwoFactorHelper` (bez `LegacyAdmin`), testy (`StaffRolesTests`, migrace v `AccountAdministrationTests`), příručka
(modul Accounts – tabulka oblastí, historie), CLAUDE.md, roadmapa účtů.

## Rozhodnutí

- **Role oblastí na jednom místě** (`StaffPolicies`) – stránky znají jen jméno politiky; změna „kdo smí co“ = jeden řádek.
- **Přestavění tržeb jen účetní** – tlačítko je na stránce Tržby, kam provoz nesmí (původní návrh: účetní i provoz).
- **Vidět vs. měnit cizí objednávku:** sklad a účetní cizí objednávky vidí (balí, fakturují), zrušit je smí jen podpora.
- **Úklid role Admin až teď** – nasazená verze 12A ji nečte, takže postupnému nasazení nevadí (expand → contract).

## Známá omezení

- Střet zájmů (vyřizovat vlastní objednávku, vratku, moderovat vlastní hodnocení) řeší až krok 12C.
- Správce uživatelů nevidí seznam objednávek – jen objednávky a útratu na detailu uživatele.

## Jak ověřit

- `dotnet test` – `StaffAreaTests` (všechny role × stránky a akce, menu, produkty pro sklad), `StaffRolesTests`
  (vlastní role platí hned), `AccountAdministrationTests` (migrace převodu a úklidu), `AuthorizationMatrixTests`.
- Produkce: admin má všech 7 rolí – vidí všechno jako dřív. Vyzkoušet: v `/Admin/Users` dát testovacímu účtu jen roli
  **Sklad a expedice** – po přihlášení vidí v menu Správa jen Produkty, Expedici a Vratky; na Produktech jen naskladnění.

## Vyzkoušejte sami

1. V `StaffPolicies` přidejte `Marketing` do `RequireAccounting` – `StaffAreaTests` hned ukáže, kde se tabulka a pravidla rozcházejí.
2. Odeberte si (jako správce) roli Marketing – položka Kupony zmizí z menu hned, bez odhlášení.
