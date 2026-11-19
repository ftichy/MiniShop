# Mikroservisy krok M3: Vyhodnocení – zastavit

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Po vyjmutí Reportingu (M1) a Notifications (M2C) se změřila
skutečná cena v produkci a rozhodlo se, jestli pokračovat. Výsledek a zdůvodnění pro čtenáře příručky:
[architektura/mikroservisy.md](../prirucka/architektura/mikroservisy.md).

## Jak se měřilo (19. 11. 2026)

1. **Doba nasazení a CI** – délky jobů „Build a testy“ a „Nasazení na VM“ z GitHub Actions (`gh run view`), 5 běhů
   před M1 a 4 po M2C.
2. **Paměť a CPU** – `docker stats --no-stream` na VM v klidu a během generátoru provozu.
3. **Zátěž** – [generátor provozu](../prirucka/nasazeni/generator-provozu.md) `--customers 4 --rounds 3 --pause 5`
   (12 nákupů: zrušená, doručená, vrácená), doba potvrzení z jeho souhrnu.
4. **Metrické řady** – Explore, zdroj `grafanacloud-usage`, `grafanacloud_instance_active_series` za 24 h.
5. **Zkouška výpadku** – ruční záloha → `docker stop minishop-notifications-api-4` → generátor `--customers 1 --rounds 2`
   → `rabbitmqctl list_queues` → alarm `MiniShopQueueSilent` až do *Firing* → `docker start` → fronta znovu.

## Výsledky

| | Před | Po M2C |
| --- | --- | --- |
| Paměť kontejnerů | 1,34 GB | 1,67 GiB (Reporting 168, Notifications 184 MiB; pod zátěží +~25 MiB) |
| CPU pod zátěží | — | Web 9,4 % (vedoucí kopie), Notifications/Reporting ~0 % |
| Potvrzení objednávky | 4,9 s (G2) | 3,1–6,4 s, průměr 4,3 s, 12/12 OK |
| Nasazení na VM | 71–100 s | 103–156 s |
| Build a testy | 157–184 s | 182–209 s |
| Aktivní řady, běžně | ~3 350 | ~4 700 (po restartu služeb ~2 600, s časem roste) |
| Aktivní řady, nasazení + provoz | ~6 300 | **~10 200** (limit 10 000); `MiniShopMetricsNearLimit` jen *Pending* (9 893), pak sám Normal |
| Výpadek Notifications | — | 11 zpráv čekalo, 0 v dead-letter; *Firing* za ~13 min; po startu vše zpracováno do 25 s |

Vedlejší nálezy:
- Fakturace (ve Webu) měla při souběhu 4 zákazníků chyby zpracování ~0,4/min – konflikt na řadě čísel faktur,
  consumer zopakuje (e-shop krok 15). Dead-letter 0, nic se neztratilo. S mikroservisami nesouvisí.
- `MiniShopQueueNoConsumer` při zastavené službě nehlásí nic – správně, celou zastavenou službu hlídá `MiniShopQueueSilent`.
- `docker compose` na VM bez `sudo -u deploy` nečte `.env` (práva 600) – na frontu stačí `docker exec minishop-rabbitmq-1 rabbitmqctl …`.

## Rozhodnutí

- **Zastavit.** Payments, Invoicing ani Reviews se nevyjímají, knihovny se nedělí. Reporting.Api a Notifications.Api
  zůstávají jako ukázka.
- **Krok M4: méně metrik v Alloyi** – zahodit metriky, které nikdo nečte, aby nasazení nepřekračovalo limit Grafany.
  Vyplatí se i bez dalších služeb.

## Známá omezení

- Měření je z jednoho dne a jednoho běhu generátoru – čísla jsou orientační, ne benchmark.
- Výpadek se zkoušel jen u Notifications; výpadek Webu (e-maily s kontaktem do dead-letter) se nezkoušel.

## Změněné soubory

- Nová stránka příručky `docs/prirucka/architektura/mikroservisy.md`; `architektura/kontejnery.md`, `historie.md`.
- `docs/Mikroservisy-roadmap.md` (stav M3, krok M4), `CLAUDE.md`.

## Vyzkoušejte sami

- Zopakujte zkoušku výpadku se `reporting-api` – přehled tržeb ukáže upozornění, po startu se dožene.
- V Grafaně `grafanacloud_instance_active_series` během příštího nasazení – uvidíte, jak se řady na ~20 minut zdvojí.
