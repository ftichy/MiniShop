# E-shop krok 18 – „Dejte mi vědět“ (upozornění na naskladnění)

Issue #88. U vyprodaného zboží je tlačítko **Dejte mi vědět**; po naskladnění přijde přihlášenému zákazníkovi jeden e-mail.

## Jak to funguje

Přirovnání: lísteček na pultu „zavolejte mi, až zboží přijde“. Prodavač zavolá jednou a lísteček zahodí.

1. Přihlášený zákazník (role User, potvrzený e-mail) klikne na `/Shop/{id}` u vyprodaného produktu → `BackInStockService.SubscribeAsync`
   uloží `BackInStockSubscription` (zákazník, produkt, název) do schématu `notifications`. Unikátní index `CustomerId + ProductId` = jeden lísteček.
2. Sklad při změně pásma pošle `StockAvailabilityChangedV1` (existující kontrakt, beze změny).
3. `BackInStockHandler` (Notifications) při `InStock`/`LowStock` projde lístečky produktu. U každého: ověří přes `ICustomerContacts`
   potvrzený e-mail, pošle e-mail, **smaže lísteček a uloží** – proto „jen jednou“. Nepotvrzený e-mail / smazaný účet = lísteček se jen zavře.
4. Smazání účtu (`CustomerAnonymizedV1`) lístečky zákazníka smaže.

## Nové soubory
- `Domain/Notifications/BackInStockSubscription.cs`
- `Application/Notifications/`: `IBackInStockRepository`, `BackInStockService`, `BackInStockHandler`, `BackInStockEmailTemplates`
- `Infrastructure/Notifications/`: `BackInStockRepository`, `Configurations/BackInStockSubscriptionConfiguration`, migrace `…_BackInStockSubscriptions`

## Změněné soubory
`NotificationSettings.ProductLink`, `OrderNotificationsHandler` (anonymizace), `NotificationsDbContext`, `NotificationsInfrastructure`,
`ApplicationServiceCollectionExtensions`, `Pages/Shop/Detail.cshtml(.cs)` (handlery `Notify`, `CancelNotify`), `Pages/Privacy.cshtml`, příručka Notifications.

## Rozhodnutí
- Žádný nový kontrakt ani nová zpráva; Ordering ani Inventory se nemění.
- E-mail i při přechodu na „Poslední kusy“ (`LowStock`) – je to zase skladem.
- Název produktu se uloží při přihlášení (zpráva o dostupnosti ho nenese); může zestárnout.
- Souhlas = samotné kliknutí na tlačítko, stačí potvrzený e-mail (na rozdíl od připomínky košíku).
- Migrace je čistý expand (nová tabulka), funguje se starší verzí kódu.

## Známá omezení
- Pád přesně mezi odesláním e-mailu a uložením = e-mail podruhé (at-least-once jako jinde).
- Souběžné naskladnění dvěma zprávami najednou může při souběhu skončit konfliktem a zopakováním zprávy; inbox + smazání lístečku duplicitu zabrání.
- Zákazník nemá přehled svých hlídání, jen tlačítko u produktu.
- Dvojklik na „Dejte mi vědět“: druhý požadavek narazí na unikátní index (`ConcurrencyConflictException`), stránka ho bere jako úspěch – lísteček je jeden (test `DejteMiVedet_SoubezneDvaKliky_KonfliktNeDvaListecky`).

## Jak ověřit
`dotnet test MiniShop.slnx` (`DejteMiVedet_*` v `OrderServiceTests`, matice oprávnění). Ručně: vyprodat produkt, přihlášený
zákazník klikne „Dejte mi vědět“, admin produkt naskladní → po pár sekundách e-mail (Mailpit), druhý nepřijde.

## Vyzkoušejte sami
Zrušte upozornění a naskladněte znovu – e-mail nepřijde. Smažte účet – řádek `BackInStockSubscriptions` zmizí.

## Jak krok vznikl

První krok vytvořený **agentem Claude v GitHub Issues** ([příručka](../prirucka/github/agent.md)): zadání v issue #88 →
`@claude analyzuj` (plán v komentáři) → `@claude implementuj` → PR #89 (CI zelené, sloučil člověk) → nasazeno 12. 11. 2026.
Po kontrole doplněno ručně: ošetření dvojkliku a dokumentace (roadmapa, historie, stav v CLAUDE.md).
