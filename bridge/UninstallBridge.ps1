# Cleanly uninstalls the Melvor Idle 2 MCP bridge and restores original hostfxr.dll.
#
# Usage:
#   .\UninstallBridge.ps1
#   .\UninstallBridge.ps1 -GameDir "D:\Games\Melvor Idle 2"
#
# NOTE: run this while the game is CLOSED.

param(
    [string]$GameDir = ""
)

$ErrorActionPreference = 'Stop'
$bridge = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $bridge

function Find-Melvor2GameDir {
    param([string]$ExplicitDir)
    if ($ExplicitDir) {
        if (Test-Path (Join-Path $ExplicitDir 'Melvor2.exe')) {
            return (Resolve-Path $ExplicitDir).Path
        }
        Write-Error "Melvor2.exe not found under specified -GameDir: $ExplicitDir"
    }

    $relCandidate = Join-Path $projectRoot '..\..'
    if (Test-Path (Join-Path $relCandidate 'Melvor2.exe')) {
        return (Resolve-Path $relCandidate).Path
    }

    $steamPaths = @()
    try {
        $reg = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
        if ($reg -and $reg.SteamPath) { $steamPaths += ($reg.SteamPath -replace '/', '\') }
    } catch {}
    $steamPaths += @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')

    $libraries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($sp in $steamPaths) {
        if (-not $sp -or -not (Test-Path $sp)) { continue }
        [void]$libraries.Add($sp)
        $vdf = Join-Path $sp 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            $content = Get-Content $vdf -Raw -ErrorAction SilentlyContinue
            foreach ($m in [regex]::Matches($content, '"path"\s+"([^"]+)"')) {
                $libPath = $m.Groups[1].Value -replace '\\\\', '\'
                if (Test-Path $libPath) { [void]$libraries.Add($libPath) }
            }
        }
    }

    foreach ($lib in $libraries) {
        $cand = Join-Path $lib 'steamapps\common\Melvor Idle 2'
        if (Test-Path (Join-Path $cand 'Melvor2.exe')) {
            return (Resolve-Path $cand).Path
        }
    }
    return $null
}

$resolvedGameDir = Find-Melvor2GameDir -ExplicitDir $GameDir
if (-not $resolvedGameDir) {
    Write-Error "Could not locate Melvor Idle 2. Pass -GameDir explicitly."
}

if (Get-Process Melvor2 -ErrorAction SilentlyContinue) {
    Write-Error "Melvor2.exe is currently running; please close the game first."
}

$dataDir = Get-ChildItem $resolvedGameDir -Directory -Filter 'data_*' | Select-Object -First 1
if ($dataDir) {
    $realFxr = Join-Path $dataDir.FullName 'hostfxr_clr.dll'
    $origFxr = Join-Path $dataDir.FullName 'hostfxr.dll'
    if (Test-Path $realFxr) {
        Move-Item $realFxr $origFxr -Force
        Write-Host "[OK] Restored original $origFxr" -ForegroundColor Green
    } else {
        Write-Host "[INFO] hostfxr_clr.dll not found (bridge wrapper may already be uninstalled)."
    }
}

$hookDll = Join-Path $resolvedGameDir 'Melvor2BridgeHook.dll'
if (Test-Path $hookDll) {
    Remove-Item $hookDll -Force
    Write-Host "[OK] Removed $hookDll" -ForegroundColor Green
}

Write-Host "Melvor2-MCP bridge uninstalled cleanly."
