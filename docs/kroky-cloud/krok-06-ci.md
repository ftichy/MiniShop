# Cloud krok 6: CI – build a testy při každém push a pull requestu

Do teď se testy spouštěly ručně (`dotnet test`) – a co se musí pamatovat, to se jednou zapomene. Teď je spouští
**GitHub Actions** automaticky u každé změny, na čistém Linuxovém stroji. Větev `main` je chráněná: změna do ní
jde jen přes pull request a sloučit ho jde, až když CI svítí zeleně.

## Jak to funguje

Přirovnání: CI je **kontrola kvality na konci výrobní linky**. Každý kus (commit) projde stejnými zkouškami,
bez výjimek a bez únavy. Ochrana větve je pravidlo „vadný kus nesmí do expedice“ (`main`, odkud se v kroku 8
bude nasazovat).

```
git push / pull request
        │
        ▼
GitHub Actions ── workflow .github/workflows/ci.yml ("CI")
        │
        ▼  runner: čistá Ubuntu VM od GitHubu (po běhu zanikne)
  job "Build a testy"
    1. checkout            – stáhne kód commitu
    2. setup-dotnet        – nainstaluje SDK podle global.json
    3. cache NuGet         – balíčky z minulého běhu (klíč = otisk .csproj/.props)
    4. restore, build      – Release
    5. test                – 160 testů, výsledek test-results.trx
    6. upload-artifact     – .trx ke stažení u běhu (i při selhání)
        │
        ▼
  ✅ / ❌ u commitu a v PR  ──►  ruleset na main: bez ✅ nejde sloučit
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Workflow** | automatizace popsaná v YAML | `.github/workflows/ci.yml`, jméno „CI“ |
| **Trigger** (`on:`) | kdy se spustí | push do `main`, každý pull request, ručně (*Run workflow*) |
| **Runner** | stroj, kde workflow běží | `ubuntu-24.04` (zdarma, u veřejného repozitáře bez limitu minut; pevná verze místo `ubuntu-latest`) |
| **Job / step** | skupina kroků / jeden příkaz nebo akce | job `build-and-test` („Build a testy“), 7 kroků |
| **Action** | hotový krok od někoho jiného | `actions/checkout`, `setup-dotnet`, `cache`, `upload-artifact` |
| **Artefakt** | soubor uložený u běhu | `test-results` (.trx, 14 dní) |
| **Status check** | výsledek jobu u commitu/PR | „Build a testy“ |
| **Ruleset** (ochrana větve) | pravidla, co se s větví smí | `main`: jen přes PR, jen se zeleným CI, bez force push a mazání |

**Proč na čistém stroji:** na vašem PC jsou věci, které v repozitáři nejsou – soubor, který jste zapomněl
přidat do commitu, balíček v cache, Windows (necitlivý na velikost písmen v cestách). CI začíná pokaždé od nuly,
stejně jako server, na který se bude nasazovat.

**Proč `global.json`:** určuje verzi .NET SDK – stejnou na PC i v CI. `rollForward: latestFeature` dovolí novější
záplaty (lokálně 10.0.200, v Docker image SDK 10.0.402), ale ne .NET 11.

**Proč `concurrency`:** když pushnete dvakrát rychle po sobě, starší běh se zruší – výsledek starého commitu
nikoho nezajímá a nezabírá runner.

**Proč `permissions: contents: read`:** workflow dostane od GitHubu token (`GITHUB_TOKEN`). Ten má mít jen
nutná práva – CI jen čte kód. Kdyby někdo podstrčil škodlivý krok (třeba přes PR), nic nezapíše.

## Návod krok za krokem

### A. Commit a push (vy)

- [ ] **A1.** Commitněte změny tohoto kroku (`.github/workflows/ci.yml`, `global.json`, `.gitignore`, `README.md`,
  `docs/…`, `CLAUDE.md`) a **pushněte do `main`** (`git push`). Ještě bez ochrany – je to poslední přímý push.
- [ ] **A2.** GitHub → repozitář → záložka **Actions** → běh „CI“ → po ~2–3 minutách ✅. Klik na běh: kroky, log,
  dole artefakt `test-results`. První běh je pomalejší (cache NuGet je prázdná).

### B. Zveřejnění repozitáře (vy)

Historie byla před zveřejněním zkontrolovaná: žádné `.env`, `.pem`, `.db`, hesla jen výchozí vývojová (`guest`)
a vzory (`ZmenteMe123`). Autor commitů = váš e-mail (bude vidět; pro nové commity lze nastavit skrytou adresu
GitHubu *Settings → Emails → Keep my email addresses private*).

- [ ] **B1.** Repozitář → **Settings** → **General** → úplně dole *Danger Zone* → **Change visibility** →
  **Make public** → potvrdit názvem repozitáře.
- [ ] **B2.** *Settings* → **Code security** (*Security*): zapnout **Secret scanning** a **Push protection**
  (u veřejných repozitářů zdarma) – GitHub odmítne push, ve kterém pozná heslo nebo token.

### C. Ochrana větve `main` – ruleset (vy)

- [ ] **C1.** *Settings* → **Rules** → **Rulesets** → **New ruleset** → **New branch ruleset**.
- [ ] **C2.** Ruleset name: `main-protection`, Enforcement status: **Active**.
  *Bypass list* nechte prázdný (pravidla platí i pro vás).
- [ ] **C3.** *Target branches* → **Add target** → **Include default branch**.
- [ ] **C4.** *Rules*:
  - ✅ **Restrict deletions** (výchozí)
  - ✅ **Require a pull request before merging** → *Required approvals*: **0** (sám sobě PR schválit nemůžete;
    v týmu by tu bylo 1+)
  - ✅ **Require status checks to pass** → **Add checks** → napsat `Build a testy` → vybrat (nabídne se až po
    prvním běhu CI – proto nejdřív A2)
  - ✅ **Block force pushes** (výchozí)
- [ ] **C5.** **Create**.

### D. Zkouška: rozbitý test v PR

- [ ] **D1.** Nová větev a schválně chybný test:

  ```powershell
  git switch -c test/cervene-ci
  ```

  V `tests/MiniShop.Tests/Domain/OrderTests.cs` (nebo libovolném testu) změňte očekávanou hodnotu v jednom
  `Assert.Equal`, commit, `git push -u origin test/cervene-ci`.
- [ ] **D2.** GitHub nabídne **Compare & pull request** → *Create pull request*. Za ~2 minuty: ❌ „Build a testy“,
  tlačítko *Merge* zašedlé s hláškou *Required statuses must pass*. **Tohle je správně.**
- [ ] **D3.** Detail neúspěšného běhu → krok *Testy* → v logu který test a proč selhal.
- [ ] **D4.** Úklid: PR → **Close pull request** → *Delete branch*. Lokálně `git switch main` a
  `git branch -D test/cervene-ci`.
- [ ] **D5.** Přímý push do `main` je teď odmítnutý (`GH013: Repository rule violations`). Každá další změna:
  větev → push → PR → ✅ → *Merge* → lokálně `git switch main && git pull`.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `.github/workflows/ci.yml` | workflow „CI“: restore, build, test (Release), cache NuGet, artefakt `.trx` |
| `global.json` | .NET SDK 10.0.200, `rollForward: latestFeature` |
| `docs/kroky-cloud/krok-06-ci.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `README.md` | odznak CI (zelený/červený podle posledního běhu na `main`) |
| `.gitignore` | `TestResults/` |

Mimo repozitář: repozitář veřejný, secret scanning + push protection, ruleset `main-protection`.

## Rozhodnutí v tomto kroku

- **Veřejný repozitář** – ochranu větve (rulesety) GitHub u soukromých repozitářů na bezplatném účtu nevynucuje
  (potřeba Pro, ~95 Kč/měsíc = celý rozpočet). Veřejný repozitář má navíc Actions bez limitu minut a secret scanning.
- **Jeden job** – build a testy dohromady trvají ~1 minutu; rozdělení by přidalo jen čekání na další runner.
- **Release konfigurace v CI** – stejná jako v Docker image (krok 1); Debug se testuje lokálně.
- **Cache přes `actions/cache`, ne `setup-dotnet` cache** – ta vyžaduje `packages.lock.json`, které projekt nemá.
- **0 povinných schválení PR** – jediný vývojář sám sobě schválit nemůže; ochranu dělá CI.
- **Prázdný bypass list** – pravidla platí i pro správce. „Rychlá oprava přímo do main“ je přesně ta, co se rozbije.
- **Akce verzované hlavní verzí** (`checkout@v7`, `setup-dotnet@v6`, `cache@v6`, `upload-artifact@v7` – všechny na Node 24; `@v4` běžely na zastaralém Node 20) – běžná praxe; přísnější je připnout na SHA commitu (krok 10).
- **Bez testů v Dockeru a bez sestavení image** – to je krok 7 (image do registry jen z `main` po zeleném CI).

## Známá omezení

- **CI netestuje nasazení** – Dockerfile, Compose ani RabbitMQ (testy jedou nad `InProcessIntegrationBus`).
  Rozbitý Dockerfile CI nepozná až do kroku 7.
- **Neběží testy architektury proti Docker image ani integrační testy proti skutečnému brokeru.**
- **Bez měření pokrytí testy** (coverage) a bez kontroly formátování (`dotnet format`). Jde doplnit dalším krokem.
- **Výsledky testů jen jako artefakt** (.trx ke stažení), ne jako přehledná tabulka v PR.
- **Akce třetích stran** (`actions/*` od GitHubu) se stahují při každém běhu – jejich kompromitace by zasáhla CI.
- **E-mail autora** ve starých commitech je po zveřejnění veřejný.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| *Actions* po pushi do `main` | běh „CI“ ✅, artefakt `test-results` |
| `README.md` na GitHubu | odznak **CI passing** |
| PR s rozbitým testem | ❌ „Build a testy“, *Merge* zablokovaný |
| `git push` přímo do `main` | odmítnuto (`GH013`) |
| Repozitář v anonymním okně prohlížeče | viditelný |
| Lokálně v Linuxu (`docker run … sdk:10.0` + kroky z workflow) | 160/160 |

## Vyzkoušejte sami

1. **Cache.** Spusťte CI dvakrát (*Run workflow*) a porovnejte dobu kroku *Restore* – podruhé balíčky přijdou
   z cache (*Cache restored from key: nuget-Linux-…*).
2. **Zapomenutý soubor.** Na větvi přidejte do projektu novou třídu, kterou použije test, ale soubor necommitněte
   (`git add` jen test). Lokálně projde, CI spadne – přesně to CI chytá.
3. **Concurrency.** Pushněte do větve PR dva commity rychle po sobě → první běh skončí jako *Cancelled*.
4. **Odznak.** Rozbitý commit na `main` se už nedostane (ruleset) – odznak proto zůstane zelený. Zkuste ho
   v README přepnout na jinou větev (`?branch=test/…`).
5. **Push protection.** Na pokusné větvi vložte do souboru něco, co vypadá jako token GitHubu (`ghp_` + 36 znaků)
   a zkuste push → GitHub ho odmítne ještě před uložením.
