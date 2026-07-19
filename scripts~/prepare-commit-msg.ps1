# prepare-commit-msg — strip Cursor attribution trailers (Windows PowerShell)
# Installed into .git/hooks/prepare-commit-msg

param(
    [Parameter(Mandatory = $true)][string]$CommitMsgFile,
    [string]$Source,
    [string]$Sha1
)

if (-not (Test-Path $CommitMsgFile)) { exit 0 }

$lines = Get-Content -Path $CommitMsgFile -ErrorAction SilentlyContinue
if (-not $lines) { exit 0 }

$filtered = $lines | Where-Object {
    $_ -notmatch '(?i)^\s*Co-authored-by:\s*Cursor(\s+Agent)?\s*<cursoragent@cursor\.com>\s*$' -and
    $_ -notmatch '(?i)^\s*Made-with:\s*Cursor\s*$' -and
    $_ -notmatch '(?i)cursoragent@cursor\.com'
}

# Avoid leaving a trailing blank-only body difference that confuses some tools
while ($filtered.Count -gt 0 -and [string]::IsNullOrWhiteSpace($filtered[-1])) {
    if ($filtered.Count -eq 1) { $filtered = @(); break }
    $filtered = $filtered[0..($filtered.Count - 2)]
}

Set-Content -Path $CommitMsgFile -Value $filtered -Encoding utf8
exit 0
