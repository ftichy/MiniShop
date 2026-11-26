<#
.SYNOPSIS
  Spojí všechny otevřené PR od Dependabota do jedné větve → jeden PR, jedno CI, jedno nasazení.

.DESCRIPTION
  Běžně chodí týdenní aktualizace v jednom PR (skupina `weekly` v .github/dependabot.yml). Skript je pro případ,
  kdy je PR od Dependabota víc najednou (bezpečnostní opravy mimo rozvrh, PR ze staré konfigurace).

  1. Ověří, že pracovní strom nemá rozpracované změny (sledované soubory).
  2. Vytvoří větev deps/<datum> z aktuálního origin/main.
  3. Postupně do ní sloučí (git merge --no-ff) větev každého otevřeného PR od Dependabota.
     PR s konfliktem přeskočí (merge --abort) a vypíše – vyřeší se zvlášť (komentář `@dependabot rebase`).
  4. S přepínačem -CreatePullRequest větev pushne a otevře PR do main se seznamem nahrazených PR.
     Po sloučení do main Dependabot své PR sám zavře (zjistí, že aktualizace už je v main).

  Před -CreatePullRequest je dobré spustit `dotnet build MiniShop.slnx` a `dotnet test MiniShop.slnx`.

.EXAMPLE
  pwsh tools/merge-dependabot.ps1                      # jen lokální větev, nic se neposílá
  pwsh tools/merge-dependabot.ps1 -CreatePullRequest   # větev + PR na GitHubu
#>
[CmdletBinding()]
param(
    # Jméno nové větve (výchozí deps/RRRR-MM-DD).
    [string]$Branch = ('deps/' + (Get-Date -Format 'yyyy-MM-dd')),
    # Pushnout větev a otevřít PR do main.
    [switch]$CreatePullRequest
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    # Spustí git a při chybě skončí (PowerShell 5.1 nehlídá návratový kód nativních příkazů sám).
    git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') skončil s kódem $LASTEXITCODE" }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Chybí GitHub CLI (gh).' }

$dirty = git status --porcelain --untracked-files=no
if ($dirty) {
    throw "Pracovní strom má rozpracované změny – nejdřív je commitněte nebo odložte (git stash):`n$($dirty -join "`n")"
}

Invoke-Git fetch --quiet origin

$prs = gh pr list --author 'app/dependabot' --state open --json 'number,title,headRefName' --limit 100 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'gh pr list selhal (přihlášení: gh auth login).' }
if (-not $prs) {
    Write-Host 'Žádný otevřený PR od Dependabota – není co spojovat.'
    return
}
$prs = @($prs | Sort-Object number)
Write-Host "Otevřené PR od Dependabota: $($prs.Count)"

git rev-parse --verify --quiet "refs/heads/$Branch" | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Větev $Branch už existuje – zvolte jinou (-Branch) nebo ji smažte." }
Invoke-Git switch --quiet --create $Branch origin/main

$merged = @()
$skipped = @()
foreach ($pr in $prs) {
    Write-Host "→ #$($pr.number) $($pr.title)"
    git merge --no-ff --quiet -m "Sloučení #$($pr.number): $($pr.title)" "origin/$($pr.headRefName)"
    if ($LASTEXITCODE -eq 0) {
        $merged += $pr
    } else {
        git merge --abort
        Write-Warning "  konflikt – přeskočeno (vyřešit zvlášť, např. komentář '@dependabot rebase' po sloučení dávky)"
        $skipped += $pr
    }
}

Write-Host ''
Write-Host "Větev $Branch – sloučeno $($merged.Count), přeskočeno $($skipped.Count)."
if (-not $merged) {
    Write-Warning 'Nic se nesloučilo, PR se nevytvoří.'
    return
}

if (-not $CreatePullRequest) {
    Write-Host 'Další krok: dotnet build MiniShop.slnx; dotnet test MiniShop.slnx; pak znovu s -CreatePullRequest,'
    Write-Host "nebo ručně: git push -u origin $Branch a gh pr create."
    return
}

Invoke-Git push --quiet --set-upstream origin $Branch

$lines = @(
    'Spojené PR od Dependabota v jedné větvi (`tools/merge-dependabot.ps1`) – jedno CI a jedno nasazení místo několika.',
    '',
    '## Nahrazuje',
    ''
)
$lines += $merged | ForEach-Object { "- #$($_.number) $($_.title)" }
if ($skipped) {
    $lines += '', '## Přeskočeno (konflikt, vyřešit zvlášť)', ''
    $lines += $skipped | ForEach-Object { "- #$($_.number) $($_.title)" }
}
$lines += '', 'Po sloučení Dependabot nahrazené PR sám zavře (aktualizace už bude v `main`).'
$bodyFile = [System.IO.Path]::GetTempFileName()
try {
    # UTF-8 bez BOM – gh by BOM zobrazil v popisu PR.
    [System.IO.File]::WriteAllText($bodyFile, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding $false))
    gh pr create --base main --head $Branch --title "deps: dávka aktualizací Dependabota ($($merged.Count) PR)" --body-file $bodyFile
    if ($LASTEXITCODE -ne 0) { throw 'gh pr create selhal.' }
} finally {
    Remove-Item $bodyFile -ErrorAction SilentlyContinue
}
