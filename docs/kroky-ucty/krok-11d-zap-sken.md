# Účty krok 11D: OWASP ZAP sken v CI

Poslední část zadání [issue #94](https://github.com/ftichy/MiniShop/issues/94) (A: 2FA, B: audit přihlášení, C: ZAP). Cíl: automaticky
u každého PR zkontrolovat běžné bezpečnostní nedostatky běžící aplikace – hlavičky, cookies, CSP – stejně, jak E2E testy kontrolují
nákupní cestu. Job do `ci.yml` doplnil člověk (agent Claude nesmí měnit `.github/workflows/`).

## Jak to funguje

Přirovnání: technická kontrola auta. Kontrolor obejde auto, podívá se na světla, pásy a pneumatiky – ale nenarazí s ním do zdi.
Přesně tak funguje **baseline sken**: pasivně prochází stránky a čte odpovědi, nic neútočí.

1. Job **ZAP sken** (`zap` v `ci.yml`) postaví image Webu a skladu (cache vrstev z jobu Image) a spustí aplikaci z `deploy/compose.yaml`
   – produkční konfigurace, hesla za běhu (`openssl rand`), žádné secrets.
2. Kontejner `ghcr.io/zaproxy/zaproxy:2.17.0` se připojí do sítě Compose a 2 minuty prochází `http://web:8080` (výloha, příručka,
   přihlášení, registrace…).
3. Pravidla s úrovní podle `.github/zap/rules.tsv`: **FAIL** (co MiniShop splňuje a nesmí se rozbít – job zčervená), **IGNORE**
   (vědomě neplatí, odůvodnění v komentáři), ostatní **WARN** (jen v reportu).
4. Souhrn se zobrazí v přehledu běhu, celý report (HTML/Markdown/JSON) je artefakt `zap-report` (14 dní).

## Nové soubory

- `.github/zap/rules.tsv` – pravidla ZAP s odůvodněním
- `docs/kroky-ucty/krok-11d-zap-sken.md` – tento souhrn

## Změněné soubory

`.github/workflows/ci.yml` (job `zap`), příručka `github/ci.md` (sekce Bezpečnostní sken), `treti-strany/beh.md` (image ZAP),
`architektura/moduly/accounts.md` (odkaz u hlaviček), `historie.md` (kroky 11A–11D), CLAUDE.md.

## Rozhodnutí

- **Přímo `docker run`, ne akce `zaproxy/action-baseline`:** akce ve výchozím stavu zakládá GitHub issues a při `fail_action` padá
  i na varování. Příkaz je průhledný a stejný lokálně i v CI.
- **Jen `compose.yaml`, bez E2E doplňku:** skenuje se konfigurace co nejbližší produkci (Mailpit ani zvýšený limit nejsou potřeba).
- **FAIL jen pro to, co už splňujeme:** první běh je zelený a každá červená znamená regresi, ne starý dluh.
- **IGNORE s důvodem místo trvalých WARN:** varování, která nikdo neřeší, by zakryla nové nálezy. Při prvním skenu byla všechna
  varování falešná: testovací karta Stripe `4242…`, rozsah `10.0.0.0/16` a čísla faktur v příručce, komentáře v Mermaidu,
  informace „nalezeno přihlášení/cookie“. Záměrné `style-src 'unsafe-inline'` (Mermaid) je zdokumentované v `SecurityHeaders`.
- **Nepovinný check:** job nic neblokuje (nasazení na něm nezávisí). Až se ukáže stabilní, jde ho přidat do rulesetu `main-protection`.
- **Verze ZAP napevno, zvedá se ručně:** Dependabot příkazy `docker run` v workflow nehlídá.

## Známá omezení

- Pavouk je anonymní – stránky za přihlášením (košík, pokladna, administrace) nevidí. Hlavičky nastavuje jeden middleware pro celý Web,
  cookies přihlášení hlídají testy z účtů kroku 8.
- HTTPS a HSTS z produkce (Caddy) sken neověří – jde přes HTTP uvnitř sítě Compose.
- Aktivní sken (útoky) je mimo rozsah – patří na samostatné testovací prostředí, ne do každého PR.

## Jak ověřit

- V PR: job **ZAP sken** zelený, v *Summary* běhu tabulka pravidel, artefakt `zap-report`.
- Lokálně ověřeno nad image z `main` (`sha-d498ef3`): 54 PASS, 0 WARN, 0 FAIL, 8 IGNORE. Kontrola opačného směru: stejný sken proti
  správě RabbitMQ (bez našich hlaviček) skončil FAIL (`X-Frame-Options`, `X-Content-Type-Options`) a kódem 1.

## Vyzkoušejte sami

1. V `SecurityHeaders.cs` zakomentujte řádek s `XContentTypeOptions`, pushněte do PR → job ZAP sken zčervená s pravidlem 10021.
2. Otevřete artefakt `zap-report` → `zap-report.html` a projděte, co ZAP na stránkách zkontroloval.
3. Postup lokálně je v příručce (CI → Bezpečnostní sken).
