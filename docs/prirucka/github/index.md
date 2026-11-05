---
title: GitHub a CI/CD
summary: Repozitář, ochrana větve main, automatické testy, image v registry a nasazení – co se nastavilo a proč.
order: 50
---

> **V jedné větě:** Každá změna jde přes **pull request**, kde ji **CI** otestuje; po sloučení do `main` se sama
> sestaví do image a **nasadí do cloudu**. Nic se nedělá ručně na serveru.

## Pipeline

```mermaid
flowchart LR
    pr["Pull request"] --> bt["Build a testy<br/>(povinný check)"]
    bt --> img1["Zkušební build image<br/>(jen x86, bez push)"]
    bt -->|"zeleně → lze sloučit"| main["main"]
    main --> bt2["Build a testy"]
    bt2 --> img2["Image web + inventory-api<br/>amd64 + arm64 → GHCR"]
    img2 --> dep["Deploy<br/>environment production"]
    dep --> vm["VM v OCI"]
    dep --> smoke["Smoke test<br/>HTTPS + /health"]

    click bt "/Docs/github/ci"
    click img2 "/Docs/github/ci"
    click dep "/Docs/github/cd"
    click main "/Docs/github/repozitar"
```

| Stránka | Obsah |
| --- | --- |
| [Repozitář a ochrana](repozitar.md) | veřejný repozitář, ruleset `main-protection`, secret scanning, Dependabot |
| [CI – build, testy, image](ci.md) | workflow `ci.yml`, cache, artefakty, multi-arch image, GHCR |
| [CD – nasazení](cd.md) | workflow `deploy.yml`, Tailscale, vynucený příkaz, `deploy.sh`, rollback |
| [Secrets a konfigurace](secrets.md) | kde leží tajné hodnoty, jak se dostanou na VM a jak se mění |

## Proč GitHub Actions

- Kód už na GitHubu je – žádná další služba ani účet.
- Pro veřejný repozitář zdarma a bez limitu minut; registry (GHCR) také.
- Workflow jsou YAML v repozitáři – mění se stejně jako kód, přes PR.

Alternativy: GitLab CI, Azure DevOps, Jenkins (vlastní server). Pro projekt na GitHubu nedávají smysl.

## Na internetu

- [GitHub Actions – dokumentace](https://docs.github.com/actions)
- [Martin Fowler: Continuous Integration](https://martinfowler.com/articles/continuousIntegration.html)
