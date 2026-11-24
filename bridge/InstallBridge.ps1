# Installs the Melvor Idle 2 MCP bridge into the Melvor Idle 2 game directory.
#
# Layout it creates (no game files besides hostfxr.dll are modified):
#   <game_root>\Melvor2BridgeHook.dll        managed bridge (HTTP API :17817 + embedded dashboard)
#   <data_dir>\hostfxr_clr.dll               original hostfxr, renamed in place
#   <data_dir>\hostfxr.dll                   lightweight wrapper (bootstraps the hook)
#
# Usage:
#   .\InstallBridge.ps1                             # auto-detect Steam Melvor Idle 2 & use prebuilt DLLs
#   .\InstallBridge.ps1 -GameDir "D:\Games\Melvor Idle 2"
#   .\InstallBridge.ps1 -Rebuild                    # force rebuild from C# / C sources (needs dotnet + gcc)
#
# NOTE: run this while the game is CLOSED (the DLLs are locked while running).

param(
    [string]$GameDir = "",
    [switch]$Rebuild
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

    # 1. Check relative parent (when placed under <GameDir>\Tools\melvor2-mcp)
    $relCandidate = Join-Path $projectRoot '..\..'
    if (Test-Path (Join-Path $relCandidate 'Melvor2.exe')) {
        return (Resolve-Path $relCandidate).Path
    }

    # 2. Check Steam registry + libraryfolders.vdf
    $steamPaths = @()
    try {
        $reg = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
        if ($reg -and $reg.SteamPath) {
            $steamPaths += ($reg.SteamPath -replace '/', '\')
        }
    } catch {}
    $steamPaths += @(
        'C:\Program Files (x86)\Steam',
        'C:\Program Files\Steam'
    )

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
    Write-Error @"
Could not automatically locate Melvor Idle 2 (Melvor2.exe).
Please specify the game folder explicitly:
  .\InstallBridge.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2"
"@
}

$dataDir = Get-ChildItem $resolvedGameDir -Directory -Filter 'data_*' | Select-Object -First 1
if (-not $dataDir) { Write-Error "data_* directory not found under $resolvedGameDir" }
$dataDir = $dataDir.FullName

if (Get-Process Melvor2 -ErrorAction SilentlyContinue) {
    Write-Error "Melvor2.exe is currently running; please close the game first (DLLs would be locked)."
}

$hookBuilt = Join-Path $bridge 'bin\Release\net8.0\Melvor2BridgeHook.dll'
$wrapperDll = Join-Path $bridge 'hostfxr_wrapper.dll'

# Use prebuilt DLLs by default so end-users don't need .NET SDK or MinGW GCC.
# Only build if -Rebuild is passed OR prebuilt DLLs are missing.
if ($Rebuild -or -not (Test-Path $hookBuilt)) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { Write-Error "dotnet SDK not found in PATH (required when -Rebuild is set or prebuilt DLL is missing)." }
    Write-Host "Building Melvor2BridgeHook ..."
    & $dotnet.Source build (Join-Path $bridge 'Melvor2BridgeHook.csproj') -c Release | Select-Object -Last 6
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed." }
}

if ($Rebuild -or -not (Test-Path $wrapperDll)) {
    $gcc = Get-Command gcc -ErrorAction SilentlyContinue
    if (-not $gcc) { Write-Error "gcc (MinGW-w64) not found in PATH (required when -Rebuild is set or prebuilt wrapper is missing)." }
    Write-Host "Building hostfxr_wrapper.dll ..."
    $wrapperSrc = Join-Path $bridge 'hostfxr_wrapper.c'
    & $gcc.Source -c (Join-Path $bridge 'hostfxr_thunks.S') -o (Join-Path $bridge 'hostfxr_thunks.o')
    if ($LASTEXITCODE -ne 0) { Write-Error "gcc failed on thunks." }
    & $gcc.Source -shared -O2 -o $wrapperDll $wrapperSrc `
        (Join-Path $bridge 'hostfxr_thunks.o') (Join-Path $bridge 'hostfxr_wrapper.def')
    if ($LASTEXITCODE -ne 0) { Write-Error "gcc failed on wrapper." }
    Remove-Item (Join-Path $bridge 'hostfxr_thunks.o') -Force -ErrorAction SilentlyContinue
}

# Backup original hostfxr.dll -> hostfxr_clr.dll (idempotent)
$realFxr = Join-Path $dataDir 'hostfxr_clr.dll'
$origFxr = Join-Path $dataDir 'hostfxr.dll'
if (-not (Test-Path $realFxr)) {
    if (-not (Test-Path $origFxr)) {
        Write-Error "Original hostfxr.dll not found under $dataDir"
    }
    Move-Item $origFxr $realFxr
}

Copy-Item $wrapperDll $origFxr -Force
Copy-Item $hookBuilt (Join-Path $resolvedGameDir 'Melvor2BridgeHook.dll') -Force

Write-Host ""
Write-Host "[OK] Melvor2-MCP Bridge installed successfully!" -ForegroundColor Green
Write-Host "  Game Dir : $resolvedGameDir"
Write-Host "  Wrapper  : $origFxr (original saved as hostfxr_clr.dll)"
Write-Host "  Hook DLL : $(Join-Path $resolvedGameDir 'Melvor2BridgeHook.dll')"
Write-Host ""
Write-Host "Launch Melvor Idle 2 via Steam; the bridge & dashboard will listen on:"
Write-Host "  http://127.0.0.1:17817/" -ForegroundColor Cyan
Write-Host "Logs: %APPDATA%\Games By Malcs\Melvor Idle 2\logs\{hostfxr_wrapper,melvor2_bridge,melvor2_compat_report}.{log,json}"
