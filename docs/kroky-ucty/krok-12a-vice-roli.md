# Účty krok 12A: víc rolí najednou

Místo jedné role **Admin** má MiniShop sedm zaměstnaneckých rolí podle práce ve skutečném e-shopu a k nim roli
**Zákazník**. Uživatel jich může mít víc najednou – správce si třeba přidá roli Zákazník a nakupuje. Role přiděluje online
**jen správce uživatelů**.

Krok je první ze tří:

| Krok | Co |
| --- | --- |
| **12A (tento)** | víc rolí na účet, převod adminů, zaškrtávání rolí, pojistky, e-mail o změně oprávnění |
| 12B | správa po oblastech – každá role vidí jen své stránky, menu podle rolí, matice oprávnění |
| 12C | střet zájmů – zaměstnanec nevyřizuje vlastní objednávku, vratku ani hodnocení svého nákupu |

Přirovnání: dřív měl každý zaměstnanec generální klíč od celé budovy. Teď dostane svazek klíčů jen od dveří, které
potřebuje (12A připraví svazky, 12B vymění zámky ve dveřích).

## Jak to funguje

| Role (v kódu) | Česky |
| --- | --- |
| `User` | Zákazník (jméno v databázi zůstává) |
| `UserAdmin` | Správce uživatelů |
| `CatalogManager` | Správce sortimentu |
| `Warehouse` | Sklad a expedice |
| `CustomerSupport` | Zákaznická podpora |
| `Marketing` | Marketing |
| `Accounting` | Účetní |
| `Operations` | Provoz |

1. **Přidělení:** na `/Admin/Users/Detail/{id}` zaškrtávátka rolí a **Uložit role** (jen `UserAdmin`) →
   `AccountAdministration.SetRolesAsync` v transakci pod zámkem: kontrola správce (v databázi), pravidla, nová sada rolí,
   nový security stamp, audit `RolesChanged` s detailem („+Sklad a expedice, −Marketing“).
2. **Pojistky:** vždy zůstane aktivní správce uživatelů; správu uživatelů si nikdo neodebere sám; neznámá role = chyba.
3. **E-mail:** změnila-li se zaměstnanecká role, dostane uživatel „MiniShop: změna oprávnění“ (`AccountEmails.SendRolesChangedAsync`).
   Chyba odeslání role nevrací, jen se zaloguje.
4. **Správa (12A):** policy `RequireAdmin` = **kterákoli** zaměstnanecká role + 2FA. Bez 2FA → `/Account/TwoFactor`.
5. **Zaměstnanec + zákazník:** objednává (policy `RequireCustomer` = role `User`), na `/Orders` přepíná **Moje / Všechny**
   (`?all=true`). Akce zákazníka (vratka, platba, „Moje objednávky“) berou `ToCustomerOrderAccess` – jen vlastní objednávky.
   Detail objednávky ukáže platbu a vratku jen u vlastní (`IsOwnCustomerOrder`).
6. **Převod:** migrace `StaffRoles` (ruční SQL) založí role a každému s rolí Admin přidá všech sedm zaměstnaneckých.
   Role Admin u účtu zůstane (starší verze kódu při postupném nasazení), uložení rolí v aplikaci ji odebere.

## Nové soubory

- Migrace `StaffRoles` (Accounts) – sloupec `AccountAuditEntries.Detail` + data (role, převod adminů)
- `tests/MiniShop.Tests/Web/StaffRolesTests.cs`

## Změněné soubory

`AccountRoles` (role, `Staff`, `All`, `Label`, `LegacyAdmin`), `AccountAdministration` (`SetRolesAsync` místo
Promote/Demote, `AccountSummary.Roles`, `AccountRoleChange`, společný rámec `RunAsync`), `AccountAuditEntry` (`RolesChanged`,
`Detail`), `AccountsDbContext`, `AccountsSeeder` (první správce = všechny zaměstnanecké role), `AccountDeletion`,
`AccountsServiceCollectionExtensions` (policy), `OrderAccessExtensions` (`IsStaff`, `ToCustomerOrderAccess`), `TwoFactorHelper`,
`AccountEmails`, stránky `Admin/Users` (detail se zaškrtávátky, štítky rolí, audit s detailem), `Orders/Index` (Moje / Všechny),
`Orders/Detail` (`IsOwnCustomerOrder`), `Pay`, `_LoginPartial` (štítek „správa“), `OrderDetailDto.CustomerId`, testy
(`AccountAdministrationTests`, `AccountsTests`, `UserAdministrationTests`, matice, 2FA testy, `MiniShopWebFactory.CreateWithRolesAsync`),
příručka (modul Accounts, historie), CLAUDE.md.

## Rozhodnutí

- **Jméno role zákazníka zůstává `User`** – jména rolí jsou v databázi, přejmenování by bylo expand → contract kvůli ničemu.
- **Původní role Admin se nemaže** v migraci: starší verze kódu (postupné nasazení) by jinak na minutu přišla o správce.
  Smaže se v pozdějším kroku (contract).
- **Převod v SQL, ne při startu** – seed běží ve všech kopiích Webu najednou, migrace jen jednou.
- **Role přiděluje jen správce uživatelů** (ne vedoucí oblastí) – jedno místo odpovědnosti, jednodušší audit.
- **Akce zákazníka jen u vlastních objednávek** i pro zaměstnance – podpora cizí objednávku vidí a zruší, ale „vrátit
  za zákazníka“ nemůže.
- **Vlastní 2FA pro každou zaměstnaneckou roli**, zapamatované zařízení jen zákazník (jako dřív admin).

## Známá omezení

- Do kroku 12B smí každá zaměstnanecká role celou správu (stejně jako dřív Admin).
- Střet zájmů (vyřizovat vlastní objednávku) hlídá až krok 12C.
- Role Admin v databázi zůstává, dokud se neodstraní (contract).

## Jak ověřit

- `dotnet test` – `AccountAdministrationTests` (víc rolí, audit, sám sobě, jiný zaměstnanec, migrace převodu),
  `StaffRolesTests` (skladník, zaměstnanec-zákazník, formulář rolí + e-mail, bez práva správce), `UserAdministrationTests`,
  `AccountsTests`, `AuthorizationMatrixTests`.
- Produkce: po nasazení má produkční admin všech 7 zaměstnaneckých rolí; na svém detailu si zaškrtne **Zákazník**,
  uloží, odhlásí se a přihlásí – vidí košík, pokladnu a přepínač Moje / Všechny objednávky.

## Vyzkoušejte sami

1. Založte nový účet, jako správce mu dejte roli **Sklad a expedice** – přijde mu e-mail; po přihlášení musí nastavit 2FA.
2. Zkuste si na svém detailu odškrtnout **Správce uživatelů** – políčko je zamčené, server by to stejně odmítl.
