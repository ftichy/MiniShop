---
title: Repozitář a ochrana
summary: Veřejný repozitář, ruleset na větev main, secret scanning s push protection a Dependabot.
order: 1
---

> **V jedné větě:** Repozitář [ftichy/MiniShop](https://github.com/ftichy/MiniShop) je **veřejný**; do `main` se
> dostane jen PR se zeleným CI a GitHub hlídá, aby do kódu neutekl žádný tajný klíč.

## Co se nastavilo

| Kde (Settings → …) | Nastavení | Proč |
| --- | --- | --- |
| General → Danger Zone | **Public** | rulesety se na bezplatném účtu vynucují jen u veřejných repozitářů (Pro by stál celý rozpočet); navíc Actions bez limitu a secret scanning zdarma |
| Rules → Rulesets | **`main-protection`**, cíl: výchozí větev, *Active* | ochrana `main` |
| – pravidla | zákaz smazání, zákaz force push, **povinný PR**, **povinný check „Build a testy“** | rozbitý kód se do `main` nedostane |
| – schválení | 0 povinných | jediný vývojář sám sobě schválit nemůže; ochranu dělá CI |
| – bypass list | prázdný | pravidla platí i pro správce – „rychlá oprava přímo do main“ je přesně ta, co se rozbije |
| Code security | **Secret scanning** + **Push protection** | GitHub odmítne push, ve kterém pozná heslo nebo token |
| Code security | **Dependabot** alerts a security updates | upozorní na zranitelné závislosti |
| Environments | **`production`** (jen z větve `main`) | vlastní secrets pro nasazení – viz [CD](cd.md) |
| Pull Requests | povolený *auto-merge* | PR se sloučí sám, jakmile CI zezelená |

Před zveřejněním se prošla celá historie nástrojem **gitleaks** – nenašel žádné tajné hodnoty.

## Dependabot

[.github/dependabot.yml](code:.github/dependabot.yml) – každé pondělí zkontroluje:

- **NuGet** balíčky (ve skupinách `microsoft`, `opentelemetry`, `serilog`, `tests` – související balíčky v jednom PR,
  jinak by build padal na konfliktu verzí),
- **GitHub Actions** použité ve workflow,
- **Docker** základní image v Dockerfile.

Každý jeho PR projde stejným CI jako jakákoli jiná změna; sloučení je ruční.

## Pravidla práce

1. Nová větev → commity → `git push` → pull request.
2. CI musí zezelenat (povinný check).
3. Sloučení (merge commit) → automaticky build image a nasazení.

## Na internetu

- [GitHub: Rulesets](https://docs.github.com/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets)
- [GitHub: Secret scanning](https://docs.github.com/code-security/secret-scanning/introduction/about-secret-scanning)
- [GitHub: Push protection](https://docs.github.com/code-security/secret-scanning/introduction/about-push-protection)
- [GitHub: Dependabot](https://docs.github.com/code-security/dependabot)
- [gitleaks](https://github.com/gitleaks/gitleaks)
