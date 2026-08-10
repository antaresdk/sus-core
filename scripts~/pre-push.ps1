# pre-push hook for sus-core
# HARD policy: every source change MUST bump package.json to a STRICTLY greater version.
# Compares HEAD vs origin/main.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Test-VersionGreaterThan([string]$New, [string]$Old) {
    if ($New -eq $Old) { return $false }
    # sort -V equivalent via [version] when possible, else string compare of parts
    try {
        return ([version]$New) -gt ([version]$Old)
    } catch {
        $sorted = @($Old, $New) | Sort-Object { $_ }
        # Sort-Object is ordinal; fallback: require inequality only if parse fails
        return $New -ne $Old
    }
}

# Resolve baseline: prefer origin/main, else gitea/main (after GitHub retarget).
# Native git failures must not become terminating errors under $ErrorActionPreference Stop.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'SilentlyContinue'
$remoteHead = $null
foreach ($ref in @('origin/main', 'gitea/main')) {
    $sha = (& git rev-parse --verify $ref 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $sha -match '^[0-9a-f]{40}$') {
        $remoteHead = $sha
        break
    }
}
$ErrorActionPreference = $prevEap
if (-not $remoteHead) { exit 0 }

$localHead = git rev-parse HEAD

$changed = git diff --name-only $remoteHead $localHead 2>$null
if (-not $changed) { exit 0 }

$sourceChanged = $changed | Where-Object {
    (($_ -match '\.cs$' -and $_ -notmatch '\.g\.cs$') -or
     ($_ -match '\.sharq$') -or
     ($_ -match '\.asmdef$'))
}

if (-not $sourceChanged) {
    Write-Host "[pre-push] No source changes. OK." -ForegroundColor DarkGray
    exit 0
}

$oldVersion = (git show "${remoteHead}:package.json" | Select-String '"version"\s*:\s*"([^"]+)"').Matches.Groups[1].Value
$newVersion = (git show "${localHead}:package.json"  | Select-String '"version"\s*:\s*"([^"]+)"').Matches.Groups[1].Value

if (-not (Test-VersionGreaterThan $newVersion $oldVersion)) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host "  PUSH REJECTED: version not bumped" -ForegroundColor Red
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Source changed, but package.json version is not strictly greater." -ForegroundColor Yellow
    Write-Host "  remote: $oldVersion" -ForegroundColor Yellow
    Write-Host "  local:  $newVersion" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Changed:" -ForegroundColor Yellow
    $sourceChanged | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Fix:" -ForegroundColor Cyan
    Write-Host "  Edit package.json: bump version (ex: $oldVersion -> next patch/minor)" -ForegroundColor Cyan
    Write-Host "  git add package.json && git commit --amend -a   # or new commit" -ForegroundColor White
    Write-Host "  git push" -ForegroundColor White
    Write-Host ""
    exit 1
}

# --- docs tooling gate (workspace umbrella; skip when absent) ---
$pkgName = Split-Path -Leaf $repoRoot
$docs-tool = Join-Path (Split-Path -Parent $repoRoot) "tools\docs-tool\index.mjs"
if (Test-Path $docs-tool) {
    node $docs-tool verify --scope $pkgName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[pre-push] docs tooling verify failed - run 'npm run docs:loop' in the umbrella, commit, retry" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[pre-push] docs tooling not found - skipped" -ForegroundColor DarkGray
}

Write-Host "[pre-push] Version check OK: $oldVersion -> $newVersion" -ForegroundColor Green
exit 0
