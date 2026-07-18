# commit-msg — last-chance strip of Cursor attribution trailers
param(
    [Parameter(Mandatory = $true)][string]$CommitMsgFile
)

if (-not (Test-Path $CommitMsgFile)) { exit 0 }

$raw = Get-Content -Path $CommitMsgFile -Raw -ErrorAction SilentlyContinue
if (-not $raw) { exit 0 }

$cleaned = [regex]::Replace($raw, '(?im)^\s*Co-authored-by:\s*Cursor(\s+Agent)?\s*<cursoragent@cursor\.com>\s*\r?\n', '')
$cleaned = [regex]::Replace($cleaned, '(?im)^\s*Made-with:\s*Cursor\s*\r?\n', '')
$cleaned = [regex]::Replace($cleaned, '(?im)^.*cursoragent@cursor\.com.*\r?\n', '')
$cleaned = $cleaned.TrimEnd() + "`n"

if ($cleaned -ne $raw) {
    [System.IO.File]::WriteAllText($CommitMsgFile, $cleaned)
}
exit 0
