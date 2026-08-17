# Install git hooks for sus-core (one-time setup)
# Run from repo root: .\scripts~\install-hooks.ps1
# Folder is `scripts~` so Unity AssetDatabase ignores git-hook tooling.

$ErrorActionPreference = "Stop"
$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    Write-Host "ERROR: Not inside a git repo" -ForegroundColor Red
    exit 1
}

$hooksDir = "$repoRoot\.git\hooks"
$scriptDir = Join-Path $repoRoot "scripts~"

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: scripts~/ directory not found in $repoRoot" -ForegroundColor Red
    exit 1
}

function Install-Hook {
    param(
        $hookName,
        $scriptName,
        [switch]$PassGitArgs
    )

    $hookSource = Join-Path $scriptDir $scriptName
    $hookDest   = Join-Path $hooksDir $hookName

    if (-not (Test-Path $hookSource)) {
        Write-Host "ERROR: $scriptName not found in scripts~/" -ForegroundColor Red
        exit 1
    }

    $argPart = if ($PassGitArgs) { ' "$1" "$2" "$3"' } else { '' }
    $wrapper = @"
#!/bin/sh
# Git $hookName hook — runs $scriptName from scripts~/
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$hookSource"$argPart
exit `$?
"@

    Set-Content -Path $hookDest -Value $wrapper -Encoding ASCII -NoNewline

    if ($IsLinux -or $IsMacOS) {
        chmod +x $hookDest
    }
}

# ─── pre-commit: auto-generate .meta for new files ───
Install-Hook "pre-commit" "pre-commit.ps1"

# ─── pre-push: docs gate + HARD version bump (T-567) ───
# Copied AS-IS, not wrapped: scripts~/pre-push is the versioned copy of the shared canonical hook
# kept in the umbrella tooling folder, identical in all four packages. The former
# PowerShell wrapper baked an ABSOLUTE path into .git/hooks, so the gate survived a folder move
# on exactly one machine and nowhere else.
$prePushSrc = Join-Path $scriptDir "pre-push"
$prePushDst = Join-Path $hooksDir "pre-push"
if (-not (Test-Path $prePushSrc)) {
    Write-Host "ERROR: scripts~/pre-push not found" -ForegroundColor Red
    exit 1
}
$text = [System.IO.File]::ReadAllText($prePushSrc) -replace "`r`n", "`n" -replace "`r", "`n"
[System.IO.File]::WriteAllText($prePushDst, $text, (New-Object System.Text.UTF8Encoding $false))

# ─── prepare-commit-msg: strip Cursor co-author trailers (T-646) ───
# Copied AS-IS, not wrapped, for the same reason as pre-push above: scripts~/prepare-commit-msg is
# the versioned copy of the shared canonical hook. It is /bin/sh on purpose — the former
# PowerShell script read the message file with the system ANSI code page (`Get-Content` without
# `-Encoding utf8`) and wrote the mis-decoded characters back, so every non-ASCII commit message
# reached the real git object corrupted.
$prepareSrc = Join-Path $scriptDir "prepare-commit-msg"
$prepareDst = Join-Path $hooksDir "prepare-commit-msg"
if (Test-Path $prepareSrc) {
    $text = [System.IO.File]::ReadAllText($prepareSrc) -replace "`r`n", "`n" -replace "`r", "`n"
    [System.IO.File]::WriteAllText($prepareDst, $text, (New-Object System.Text.UTF8Encoding $false))
}

# ─── optional message hooks ───
if (Test-Path (Join-Path $scriptDir "commit-msg.ps1")) {
    Install-Hook "commit-msg" "commit-msg.ps1" -PassGitArgs
}

Write-Host ""
Write-Host "Git hooks installed:" -ForegroundColor Green
Write-Host "  pre-commit  →  scripts~/pre-commit.ps1  (auto-generate .meta)" -ForegroundColor Green
Write-Host "  pre-push    →  scripts~/pre-push        (docs gate + strict version bump)" -ForegroundColor Green
Write-Host ""
Write-Host "To uninstall: rm .git/hooks/pre-commit .git/hooks/pre-push .git/hooks/prepare-commit-msg .git/hooks/commit-msg" -ForegroundColor DarkGray
