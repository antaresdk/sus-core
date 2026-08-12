# commit-msg — strip Cursor attribution + R25 public-scope guard (forbidden names / mojibake)
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$CommitMsgFile,
    [Parameter(ValueFromRemainingArguments = $true)][object[]]$Unused
)

if (-not (Test-Path $CommitMsgFile)) { exit 0 }

$raw = Get-Content -Path $CommitMsgFile -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
if (-not $raw) { exit 0 }

$cleaned = [regex]::Replace($raw, '(?im)^\s*Co-authored-by:\s*Cursor(\s+Agent)?\s*<cursoragent@cursor\.com>\s*\r?\n', '')
$cleaned = [regex]::Replace($cleaned, '(?im)^\s*Made-with:\s*Cursor\s*\r?\n', '')
$cleaned = [regex]::Replace($cleaned, '(?im)^.*cursoragent@cursor\.com.*\r?\n', '')
$cleaned = $cleaned.TrimEnd() + "`n"

if ($cleaned -ne $raw) {
    [System.IO.File]::WriteAllText($CommitMsgFile, $cleaned)
}

# R25: via neutral umbrella shim (do not hardcode internal tool paths that break PS vars)
$repoRoot = Split-Path -Parent $PSScriptRoot
$umbrella = Split-Path -Parent $repoRoot
$docsTool = Join-Path $umbrella "tools\docs-tool\index.mjs"
if (Test-Path $docsTool) {
    node $docsTool commit-msg-check $CommitMsgFile
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
