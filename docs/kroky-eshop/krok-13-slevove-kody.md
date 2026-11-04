# E-shop krok 13: Slevové kódy

Zákazník v pokladně uplatní slevový kód (procenta nebo Kč, s platností, minimem nákupu a limitem použití). Admin
kódy zakládá a vypíná na stránce **Kupony**. Hlavní téma je **limit použití při souběhu**: poslední použití nesmí
projít dvakrát. Řeší ho optimistická souběžnost na agregátu `Coupon` – ve stejné transakci jako objednávka.
Volitelný námět z [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 12](krok-12-vraceni-zbozi.md).

## Jak to funguje

Přirovnání: poslední kus v regálu – sáhnou po něm dva, do košíku ho dá jen jeden.

```
 pokladna: Uplatnit kód ─► ověřit (existuje, platí, minimum, zákazník ho nepoužil) ─► Cart.CouponCode
 Objednat ─► Cart.CheckOut ─► Order.AddItem ─► znovu ověřit ─► Order.ApplyCoupon ─► Coupon.Redeem (počet +1, Version +1)
          ─► sleva rozpočítaná do slev položek ─► doprava z ceny po slevě
          ─► COMMIT: objednávka + prázdný košík + kupon (WHERE Version = původní) + Outbox[OrderPlaced]
 souběh: druhá pokladna ─► 0 řádků ─► ConcurrencyConflictException ─► celá transakce zpět ─► „kód mezitím použil někdo jiný“
 OrderRejected / OrderCancelled ─► ReleaseCouponHandler ─► Coupon.Release (počet −1)        (vratka ne)
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Optimistická souběžnost** | žádný zámek; zápis projde, jen když se data od načtení nezměnila | `Coupon.Version` (concurrency token) |
| **Hranice transakce = hranice modulu** | ve stejné DB stačí jedna transakce, napříč moduly by byla potřeba sága | kupon i objednávka v `ordering.db` |
| **Kompenzace přes doménové události** | vrácení použití jako reakce na fakt, ne volání z pokladny | `ReleaseCouponHandler` na `OrderRejected`/`OrderCancelled` |
| **Stabilní kontrakt** | změna ceny bez nové verze zprávy – sleva už v položkách | `OrderItem.Discount`, `OrderPlacedV2` beze změny |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Domain/Orders/Coupons/Coupon.cs` | agregát kuponu (+ `CouponKind`) |
| `src/MiniShop.Application/Ordering/Coupons/*` | `ICouponRepository`, `CouponService` (admin), `ReleaseCouponHandler` (kompenzace) |
| `src/MiniShop.Infrastructure/Ordering/CouponRepository.cs`, `Configurations/CouponConfiguration.cs` | EF, migrace `Coupons` |
| `src/MiniShop.Web/Pages/Admin/Coupons/Index.cshtml(.cs)` | stránka Kupony |
| `docs/prirucka/toky/slevove-kody.md`, tento souhrn | dokumentace |
| `tests/…/Domain/CouponTests.cs` | pravidla kuponu, rozpočítání na haléře |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs`, `OrderItem.cs` | `ApplyCoupon` (rozpočítání slevy), `CouponId`, `CouponCode`, `CouponDiscount`; `OrderItem.AddDiscount` |
| `Domain/Orders/Carts/Cart.cs` | `CouponCode`, `ApplyCoupon`, `RemoveCoupon` (nová verze pro pokladnu), vyprázdnění kód maže |
| `Application/Ordering/Checkout/*` | `ApplyCouponAsync`, `RemoveCouponAsync`, souhrn se slevou a `CouponProblem`, ověření a použití při objednání, hláška při souběhu |
| `Application/Ordering/IOrderRepository.cs`, `OrderService` DTO | `HasUsedCouponAsync`, `OrderDto.CouponCode/CouponDiscount` |
| `Web/Pages/Checkout/Index.cshtml(.cs)`, `Orders/Detail.cshtml`, `_Layout.cshtml` | pole kódu (vlastní formulář, PRG), kód na detailu, menu **Kupony** |
| `Web/Extensions/…` | registrace `CouponService`, `ReleaseCouponHandler` |
| příručka (`toky/index`, `pokladna`, `moduly/ordering`, `historie`) | dokumentace |
| `tests/…/OrderServiceTests.cs`, `AuthorizationMatrixTests.cs` | celé cesty pokladnou, souběh, kompenzace, oprávnění |

## Rozhodnutí v tomto kroku

- **Sleva rozpočítaná do položek** – žádný nový kontrakt; e-maily, přehled tržeb i vrácení peněz při vratce sedí.
- **Použití se počítá až při objednání**, ne při uplatnění – uplatněný a neobjednaný kód nic neblokuje.
- **Jeden zákazník jednou** dotazem na jeho platné objednávky (zamítnuté a zrušené se nepočítají); bezpečné i při
  souběhu, protože každé použití zvýší verzi kuponu.
- **Kupon se sčítá se slevou za množství** a počítá se z ceny po ní; doprava zdarma z ceny po kuponu.
- **Formulář kódu mimo sledovaný formulář pokladny** – Journeys ho nezapisují, takže se nemusí kopírovat do `Replay.cshtml`.
- **Vratka použití nevrací** – nákup proběhl.

## Známá omezení

- Kupon nejde omezit na kategorii nebo produkt, ani na první nákup.
- Úpravu kuponu (jiná sleva, delší platnost) admin nemá – jen založit nový a starý vypnout.
- Po vypnutí kuponu zákazníci, kteří ho mají uplatněný v košíku, uvidí v pokladně hlášku a musí ho odebrat.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu migrace `…_Coupons`.
2. Admin → **Kupony** → založ `JARO10`, 10 %, max. 1 použití.
3. Zákazník → košík → pokladna → kód `jaro10` → **Uplatnit** → v souhrnu „Slevový kód JARO10 −…“.
4. Objednej → na detailu objednávky kód a položky po slevě; v Kuponech „Použito 1 / 1“.
5. Jiný zákazník s kódem → „Slevový kód je už vyčerpaný.“
6. Zruš první objednávku → za pár sekund „Použito 0 / 1“, kód jde použít znovu.

### Produkce

Nic nového k nastavení – migrace `ordering.db` proběhne sama při startu.

## Vyzkoušejte sami

- Najdi v testu `Kupon_Soubeh_VerzeKuponuPustiJenJedenZapis`, kde se rozhodne, která pokladna vyhrála.
- Co by se stalo bez `IsConcurrencyToken()` na `Coupon.Version`? Zkus to a pusť test.
- Navrhni kupony jako samostatný modul (vlastní DB) – jaké zprávy by pokladna potřebovala a proč by vznikla sága?
- Proč `ReleaseCouponHandler` nereaguje na `OrderReturned`?
