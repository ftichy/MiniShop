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
| – pravidla | zákaz smazání, zákaz force push, **povinný PR**, **povinný check „Build a testy“** (testy nad PostgreSQL; v krocích 2–3C i druhý job „Testy PostgreSQL“) | rozbitý kód se do `main` nedostane |
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

**Hlavní verze .NET Dependabot nenabízí** (`ignore` … `semver-major` pro `Microsoft.AspNetCore.*`,
`Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.*`, `System.*`, `Npgsql*` a image `dotnet/*`). Přechod z .NET 10
na 11 je samostatný krok – SDK v `global.json`, image a všechny balíčky naráz. Runtime 11 aplikaci pro .NET 10 nespustí:
16. 11. 2026 se sloučil image `dotnet/aspnet:11.0` pro sklad (PR #123), nová kopie byla nezdravá a `deploy.sh` vrátil
předchozí verzi (Dockerfile vráceny na `10.0` v PR #136). **Pozor:** Dependabot PR prochází povinným „Build a testy“, ale
spuštění kontejneru hlídají jen nepovinné „E2E testy“ a „ZAP sken“ – červené E2E/ZAP u Dependabota = nesloučit.
Opravy `10.0.x` a nové verze ostatních knihoven (MailKit, Stripe…) chodí dál. Několik `Microsoft.Extensions.*` je už
ve verzi 11.0.0 (PR #135) – na .NET 10 fungují, sladí se při přechodu na .NET 11.

## Pravidla práce

1. Nová větev → commity → `git push` → pull request.
2. CI musí zezelenat (povinný check „Build a testy“).
3. Sloučení (merge commit) → automaticky build image a nasazení.

## Na internetu

- [GitHub: Rulesets](https://docs.github.com/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets)
- [GitHub: Secret scanning](https://docs.github.com/code-security/secret-scanning/introduction/about-secret-scanning)
- [GitHub: Push protection](https://docs.github.com/code-security/secret-scanning/introduction/about-push-protection)
- [GitHub: Dependabot](https://docs.github.com/code-security/dependabot)
- [gitleaks](https://github.com/gitleaks/gitleaks)
