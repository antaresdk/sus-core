# pre-commit hook — auto-generate missing .meta files
# Scans staged files/dirs against git index and working tree.
# If a file/dir is staged but its .meta is missing — generates and stages it.
# GUIDs are deterministic: MD5(repo-root-relative-path) for reproducibility.
#
# Unity hides folders whose names end with "~" (Documentation~, Tools~, Starter~, …).
# A folder .meta for those paths causes endless console warnings
# ("A meta data file exists but its folder … can't be found"). Never generate them.

$ErrorActionPreference = "SilentlyContinue"

function Test-UnityHiddenTildePath {
    param([string]$RelativePath)
    # True if any path segment ends with "~" (Unity AssetDatabase ignore).
    return ($RelativePath -match '(^|/)[^/]+~(/|$)') -or ($RelativePath -match '~$')
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Only run when there are staged changes
$staged = git diff --cached --name-only 2>$null
if (-not $staged) { exit 0 }

# Collect all known entries (both in index and working tree)
$allFiles = git ls-files 2>$null
$allDirs = @{}
foreach ($f in $allFiles) {
    $dir = Split-Path $f -Parent
    while ($dir -and $dir -ne '.') {
        $allDirs[$dir.Replace('\','/')] = $true
        $dir = Split-Path $dir -Parent
    }
}

# Also include root-level dirs
git ls-tree -d --name-only HEAD 2>$null | ForEach-Object { $allDirs[$_] = $true }

# Collect .meta files we already have
$knownMetas = @{}
foreach ($f in $allFiles) {
    if ($f -match '\.meta$') {
        $base = $f -replace '\.meta$',''
        $knownMetas[$base] = $true
    }
}

# Generate GUID from relative path (deterministic via MD5)
function New-DeterministicGuid {
    param($relativePath)
    $normalized = $relativePath.Replace('\','/').TrimStart('/')
    $bytes = [System.Security.Cryptography.MD5]::Create().ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes($normalized)
    )
    return [Guid]::new($bytes).ToString("N")
}

# Templates — match exact Unity format (trailing space after empty values, no importer for plain files)
$fileMeta = @'
fileFormatVersion: 2
guid: {0}
'@

$asmdefMeta = @'
fileFormatVersion: 2
guid: {0}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'@

$folderMeta = @'
fileFormatVersion: 2
guid: {0}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'@

function Get-MetaTemplate {
    param($path)
    # Directories are detected via Test-Path below
    if ($path -match '\.asmdef$') { return $asmdefMeta }
    return $fileMeta
}

function Write-And-Stage {
    param($path, $content)
    $dir = Split-Path $path -Parent
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($path, $content, [System.Text.Encoding]::ASCII)
    git add $path 2>$null
    Write-Host "  [meta] auto-generated: $path" -ForegroundColor DarkGray
}

$anyGenerated = $false

# 1. Check staged files — each needs a .meta
foreach ($f in $staged) {
    $f = $f.Replace('\','/')
    # Skip .meta themselves, .gitignore, files in .git/
    if ($f -match '\.meta$' -or $f -match '^\.git' -or $f -eq '.gitignore') { continue }
    # Files inside Unity-hidden "~" folders don't need AssetDatabase metas
    if (Test-UnityHiddenTildePath $f) { continue }
    if (-not $knownMetas.ContainsKey($f)) {
        $metaPath = "$f.meta"
        $guid = New-DeterministicGuid $f
        $template = Get-MetaTemplate $f
        $content = $template -f $guid
        Write-And-Stage $metaPath $content
        $knownMetas[$f] = $true
        $anyGenerated = $true
    }
}

# 2. Check parent directories of staged files — each needs a .meta
foreach ($f in $staged) {
    $f = $f.Replace('\','/')
    if ($f -match '\.meta$' -or $f -eq '.gitignore') { continue }
    $dir = Split-Path $f -Parent
    while ($dir -and $dir -ne '.') {
        $dir = $dir.Replace('\','/')
        # Never create Documentation~.meta / Tools~.meta / Starter~.meta etc.
        if (Test-UnityHiddenTildePath $dir) {
            $dir = Split-Path $dir -Parent
            continue
        }
        if (-not $knownMetas.ContainsKey($dir)) {
            $metaPath = "$dir.meta"
            $guid = New-DeterministicGuid $dir
            $content = $folderMeta -f $guid
            Write-And-Stage $metaPath $content
            $knownMetas[$dir] = $true
            $anyGenerated = $true
        }
        $dir = Split-Path $dir -Parent
    }
}

exit 0
