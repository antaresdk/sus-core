# Install git hooks for sus-core (one-time setup)
# Run from repo root: .\scripts\install-hooks.ps1

$ErrorActionPreference = "Stop"
$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    Write-Host "ERROR: Not inside a git repo" -ForegroundColor Red
    exit 1
}

$hooksDir = "$repoRoot\.git\hooks"
$scriptDir = "$repoRoot\scripts"

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: scripts/ directory not found in $repoRoot" -ForegroundColor Red
    exit 1
}

function Install-Hook {
    param($hookName, $scriptName)

    $hookSource = "$scriptDir\$scriptName"
    $hookDest   = "$hooksDir\$hookName"

    if (-not (Test-Path $hookSource)) {
        Write-Host "ERROR: $scriptName not found in scripts/" -ForegroundColor Red
        exit 1
    }

    $wrapper = @"
#!/bin/sh
# Git $hookName hook — runs $scriptName from scripts/
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$hookSource"
exit `$?
"@

    Set-Content -Path $hookDest -Value $wrapper -Encoding ASCII -NoNewline

    if ($IsLinux -or $IsMacOS) {
        chmod +x $hookDest
    }
}

# ─── pre-commit: auto-generate .meta for new files ───
Install-Hook "pre-commit" "pre-commit.ps1"

# ─── pre-push: enforce version bump on source changes ───
Install-Hook "pre-push" "pre-push.ps1"

Write-Host ""
Write-Host "Git hooks installed:" -ForegroundColor Green
Write-Host "  pre-commit  →  scripts/pre-commit.ps1  (auto-generate .meta)" -ForegroundColor Green
Write-Host "  pre-push    →  scripts/pre-push.ps1    (strict version bump)" -ForegroundColor Green
Write-Host ""
Write-Host "To uninstall: rm .git/hooks/pre-commit .git/hooks/pre-push" -ForegroundColor DarkGray
