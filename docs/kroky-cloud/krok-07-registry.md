# Cloud krok 7: Image do registry (GHCR)

Do teď se image stavěly na PC a na VM se přenášely ručně (`docker save` → `scp` → `docker load`, krok 4). Nešlo to
automatizovat, nebylo vidět, *která verze* na VM běží, a předchozí verze se přepsala. Teď image staví **CI** po
každém sloučení do `main` – pro x86 i ARM – a ukládá je do **GitHub Container Registry (GHCR)**. VM si je stáhne
příkazem `docker compose pull`.

## Jak to funguje

Přirovnání: registry je **sklad s regály a štítky**. Továrna (CI) do skladu ukládá hotové krabice (image),
každou se štítkem čísla zakázky (commit). Server si ze skladu vezme krabici podle štítku. Starší krabice zůstávají
na regálu – návrat k předchozí verzi je jen „podej mi tu se starším štítkem“.

```
pull request ─► CI: Build a testy ─✅─► Image web / inventory-api: zkušební build (x86, bez uložení)
                                         → rozbitý Dockerfile se pozná ještě před sloučením
merge do main ─► CI: Build a testy ─✅─► Image web / inventory-api: build x86 + ARM ──push──►  GHCR
                                                                                              │
   ghcr.io/ftichy/minishop-web:sha-1a2b3c4   (= přesně commit 1a2b3c4, navždy stejný obsah)  │
   ghcr.io/ftichy/minishop-web:latest        (= poslední z main, posouvá se)                  │
   ghcr.io/ftichy/minishop-inventory-api:…                                                    │
                                                                                              ▼
VM: .env IMAGE_PREFIX=ghcr.io/ftichy/, IMAGE_TAG=sha-1a2b3c4 → docker compose pull → up -d
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Registry** | sklad image (push = uložit, pull = stáhnout) | GHCR `ghcr.io`, veřejné, zdarma |
| **Repozitář image / balíček** | jedna image se všemi verzemi | `minishop-web`, `minishop-inventory-api` (záložka *Packages*) |
| **Tag** | štítek verze | `sha-<7 znaků commitu>` (neměnný) a `latest` (posouvá se) |
| **Multi-arch image** | pod jedním tagem varianta pro každou architekturu | `linux/amd64` (PC, runner) + `linux/arm64` (VM) – Docker vybere sám |
| **Digest** | otisk obsahu `sha256:…` | jednoznačná identita image, i když se tag přesune |
| **`GITHUB_TOKEN`** | dočasný token, který GitHub dá každému běhu workflow | přihlášení do GHCR; právo `packages: write` jen job `images` |

**Proč `sha-…`, a ne jen `latest`:** `latest` je pohodlný, ale neříká, *co* běží – zítra bude znamenat něco
jiného. `sha-1a2b3c4` je navždy tentýž kód (commit `1a2b3c4` v historii gitu). Na VM proto běží konkrétní `sha-…`,
a návrat zpět je změna jednoho řádku v `.env`.

**Proč image až po zelených testech** (`needs: build-and-test`): do skladu se nesmí dostat krabice, která neprošla
kontrolou kvality. Image se z PR **nepublikují** – jen se zkušebně sestaví, aby se rozbitý Dockerfile poznal dřív,
než se sloučí (omezení z kroku 6).

**Jak vzniká ARM image na x86 runneru:** Dockerfile z kroku 1 kompiluje .NET nativně na stroji, který staví
(`FROM --platform=$BUILDPLATFORM` + `dotnet publish -a $TARGETARCH`). Emuluje se (QEMU) jen malá závěrečná fáze
nad runtime image pro ARM. Proto build pro obě architektury trvá jen pár minut.

**Proč veřejné image:** balíček publikovaný workflow z veřejného repozitáře je s ním propojený (label
`org.opencontainers.image.source`) a dědí jeho viditelnost. VM je pak stáhne **bez přihlášení** – na serveru není
žádný token, který by mohl uniknout. V image nic tajného není (zkontrolováno: jen veřejné certifikáty CA ze systému,
`appsettings.json` s vývojovým `guest`, prázdné `/data`) – a zdrojový kód je stejně veřejný.

## Návod krok za krokem

### A. Pull request a sloučení (vy)

- [ ] **A1.** Změny jsou ve větvi `ci/krok7-registry` (nevytvářejte novou):

  ```powershell
  git add -A
  git commit -m "CLOUD STEP7: image do GHCR (multi-arch, sha tagy)"
  git push -u origin ci/krok7-registry
  ```

- [ ] **A2.** PR → po ~3 minutách ✅ **Build a testy**, ✅ **Image web**, ✅ **Image inventory-api** (zkušební
  build). V detailu jobu `Image …` je krok *Build (zkušební)*.
- [ ] **A3.** **Merge** → na `main` se spustí CI znovu, tentokrát krok *Build a push* (x86 + ARM, ~5 minut).
- [ ] **A4.** Úklid: `git switch main; git pull; git branch -d ci/krok7-registry`.

### B. Balíčky na GitHubu (vy)

- [ ] **B1.** Profil (nebo repozitář) → **Packages** → `minishop-web` a `minishop-inventory-api`.
- [ ] **B2.** Detail balíčku: tagy `latest` a `sha-…`, vpravo *OS/Arch*: `linux/amd64`, `linux/arm64`.
- [ ] **B3.** Viditelnost **Public**. Kdyby byl *Private*: *Package settings* → *Danger Zone* → **Change visibility**
  → *Public*. Ověření: `docker pull ghcr.io/ftichy/minishop-web:latest` z PC bez `docker login`.

### C. VM přechází na registry (Claude přes ssh-agent, nebo vy)

- [ ] **C1.** `.env` na VM (`ssh minishop-vm`, `cd ~/minishop`):

  ```bash
  sed -i -e '/^IMAGE_TAG=/d' -e '/^IMAGE_PREFIX=/d' -e '/^WEB_PORT=/d' .env
  echo "IMAGE_PREFIX=ghcr.io/ftichy/" >> .env
  echo "IMAGE_TAG=sha-<7 znaků commitu z main>" >> .env
  ```

  (`WEB_PORT` se od kroku 5 nepoužívá – Web není publikovaný.)
- [ ] **C2.** Nová `compose.yaml` z repozitáře (`scp deploy/compose.yaml minishop-vm:minishop/` z PC), pak na VM:

  ```bash
  docker compose pull
  docker compose up -d
  docker compose ps
  ```

  `pull` stáhne jen to, co se změnilo (vrstvy, které už VM má, přeskočí). `up -d` vymění kontejnery, jejichž image
  je jiná; data ve volumes zůstanou.
- [ ] **C3.** Staré ručně přenesené image smazat: `docker image rm minishop-web:arm64 minishop-inventory-api:arm64`.

### D. Ověření

- [ ] **D1.** `docker compose images` na VM → `ghcr.io/ftichy/minishop-…` s tagem `sha-…`.
- [ ] **D2.** https://minishop-tichy.duckdns.org → objednávka → Confirmed.
- [ ] **D3.** Návrat na předchozí verzi (zkouška): `IMAGE_TAG=sha-<starší commit>` → `docker compose pull && docker compose up -d`
  → a zpět. Takhle vypadá rollback.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `docs/kroky-cloud/krok-07-registry.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `.github/workflows/ci.yml` | job `images` (matrix `web`, `inventory-api`): PR = zkušební build x86, `main` = build x86+ARM a push do GHCR; tagy `sha-…` a `latest`; cache vrstev v GitHub Actions; `packages: write` jen pro tento job |
| `deploy/compose.yaml` | `image: ${IMAGE_PREFIX:-}minishop-…:${IMAGE_TAG:-local}` – lokálně beze změny |
| `deploy/.env.example` | `IMAGE_PREFIX` |

Na VM: `.env` (`IMAGE_PREFIX`, `IMAGE_TAG=sha-…`, bez `WEB_PORT`), nová `compose.yaml`.

## Rozhodnutí v tomto kroku

- **GHCR** – u GitHubu (stejný účet, `GITHUB_TOKEN` bez dalšího tajemství), veřejné image zdarma bez limitu.
  Alternativy: Docker Hub (limity stahování), OCI Container Registry (další účet a token).
- **Veřejné image** – VM bez přihlášení do registry, žádný token na serveru.
- **Multi-arch (amd64 + arm64)** – VM je ARM, ale stejnou image spustíte i na PC (`docker run ghcr.io/ftichy/minishop-web`).
- **Tagy `sha-<commit>` + `latest`** – na VM vždy `sha-…` (víte, co běží; rollback = změna tagu). `latest` jen pro
  pohodlí při ručním zkoušení.
- **Image jen z `main` a jen po zelených testech**; z PR jen zkušební build bez push.
- **Matrix místo dvou kopií jobu** – stejné kroky pro obě služby, liší se jen jméno a Dockerfile.
  `fail-fast: false`: chyba jedné služby nezruší build druhé (uvidíte obě chyby najednou).
- **`IMAGE_PREFIX` v Compose místo druhého compose souboru** – lokálně se nic nemění (prázdný prefix = `build:`).
- **Cache vrstev `type=gha`** – restore NuGet a runtime image se nemusí pokaždé stahovat.

## Známá omezení

- **Nasazení na VM je pořád ruční** (změna `IMAGE_TAG` + `pull` + `up`). Automaticky po sloučení – krok 8.
- **Staré image v GHCR se nemažou** – u veřejných balíčků to nic nestojí; úklid jde doplnit (např.
  `actions/delete-package-versions`).
- **Image nejsou podepsané** (cosign) ani skenované na zranitelnosti (Trivy) – krok 10.
- **`concurrency` ruší starší běh na `main`** – při dvou rychlých sloučeních se image ze staršího commitu
  nepublikuje (publikuje se novější, takže nic nechybí).
- **Zkušební build v PR jen pro x86** – chyba projevující se jen na ARM se pozná až na `main`.
- **Doba běhu na `main`** se prodlouží o build image (~3–5 minut).

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| PR | 3 zelené checky: *Build a testy*, *Image web*, *Image inventory-api* |
| Běh na `main` | job `Image …` s krokem *Build a push* |
| *Packages* | `minishop-web`, `minishop-inventory-api`, Public, `linux/amd64` + `linux/arm64`, tagy `latest` a `sha-…` |
| `docker pull ghcr.io/ftichy/minishop-web:latest` z PC (bez login) | stáhne x86 variantu |
| `docker compose images` na VM | `ghcr.io/ftichy/…:sha-…` |
| MiniShop | běží, objednávka Confirmed |

## Vyzkoušejte sami

1. **Jedna image, dvě architektury.** `docker buildx imagetools inspect ghcr.io/ftichy/minishop-web:latest` →
   seznam manifestů pro `linux/amd64` a `linux/arm64` (a záznamy *attestation*).
2. **Odkud je image.** V detailu balíčku na GitHubu odkaz na repozitář a commit (labely z `metadata-action`).
   Nebo `docker image inspect ghcr.io/ftichy/minishop-web:latest --format '{{json .Config.Labels}}'`.
3. **Rollback.** Na VM `IMAGE_TAG` na starší `sha-…`, `docker compose pull && docker compose up -d`, a zpět.
   Kolik to trvalo? (Data ve volumes zůstávají – pozor jen na změny schématu databáze, viz `CLAUDE.md`.)
4. **Rozbitý Dockerfile v PR.** Na pokusné větvi překlep v `src/MiniShop.Web/Dockerfile` (např. `COPY src/neexistuje`)
   → *Image web* ❌, *Image inventory-api* ✅ (díky `fail-fast: false`), *Merge* zablokovaný? (Ne – v rulesetu
   je povinný jen *Build a testy*. Chcete-li, přidejte i oba *Image …* checky.)
5. **Cache.** Porovnejte dobu kroku *Build a push* u dvou po sobě jdoucích běhů na `main`.
