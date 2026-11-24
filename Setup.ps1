# One-Click Setup Wizard for Melvor2-MCP (V0.0.1-0116a1)
#
# Performs:
#   1. Creates a local Python virtual environment (.venv) and installs fastmcp.
#   2. Auto-detects Steam's Melvor Idle 2 installation (or uses -GameDir).
#   3. Extracts offline game data (MelvorBase.json + locales) from your local Melvor2.exe
#      and regenerates the knowledge base indexes (< 2 seconds, no game launch needed).
#   4. Optionally installs the in-game .NET bridge DLLs (unless -SkipBridge is set).
#   5. Generates a ready-to-copy mcp_config.local.json with exact paths for your machine.
#
# Usage:
#   .\Setup.ps1
#   .\Setup.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Melvor Idle 2"
#   .\Setup.ps1 -SkipBridge

param(
    [string]$GameDir = "",
    [switch]$SkipBridge,
    [switch]$SkipExtract
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$version = (Get-Content (Join-Path $root 'VERSION.txt') -Raw -ErrorAction SilentlyContinue).Trim()
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Melvor2-MCP Setup Wizard  ($version)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Python >= 3.10 and create .venv
$pyCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pyCmd) {
    Write-Error "Python 3.10+ not found in PATH. Please install Python 3.10+ from https://www.python.org/downloads/ and re-run Setup.ps1."
}

$venvDir = Join-Path $root '.venv'
$venvPy = Join-Path $venvDir 'Scripts\python.exe'
if (-not (Test-Path $venvPy)) {
    Write-Host "[1/4] Creating Python virtual environment (.venv) ..." -ForegroundColor Yellow
    & $pyCmd.Source -m venv $venvDir
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to create .venv." }
} else {
    Write-Host "[1/4] Using existing .venv ..." -ForegroundColor Green
}

Write-Host "      Installing Python dependencies (fastmcp) ..."
& $venvPy -m pip install --upgrade pip --quiet
& $venvPy -m pip install -r (Join-Path $root 'requirements.txt') --quiet
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to install requirements.txt." }
Write-Host "      Python environment ready." -ForegroundColor Green

# 2. Extract offline game database & generate knowledge base
if (-not $SkipExtract) {
    Write-Host ""
    Write-Host "[2/4] Extracting offline knowledge base from Melvor2.exe ..." -ForegroundColor Yellow
    $extractArgs = @((Join-Path $root 'knowledge\extract_offline.py'))
    if ($GameDir) {
        $extractArgs += @('--exe', (Join-Path $GameDir 'Melvor2.exe'))
    }
    & $venvPy @extractArgs
    if ($LASTEXITCODE -eq 0) {
        & $venvPy (Join-Path $root 'knowledge\generate.py')
        Write-Host "      Knowledge base updated from your local game build." -ForegroundColor Green
    } else {
        Write-Warning "Could not auto-extract Melvor2.exe. Bundled guides/indexes remain available; pass -GameDir to extract full MelvorBase.json."
    }
} else {
    Write-Host "[2/4] Skipped offline extraction (-SkipExtract)."
}

# 3. Install in-game bridge DLLs
Write-Host ""
if (-not $SkipBridge) {
    Write-Host "[3/4] Installing in-game .NET bridge (requires game to be closed) ..." -ForegroundColor Yellow
    if (Get-Process Melvor2 -ErrorAction SilentlyContinue) {
        Write-Warning "Melvor2.exe is currently running. Close the game and run .\bridge\InstallBridge.ps1 later to enable live control."
    } else {
        $bridgeParams = @{}
        if ($GameDir) { $bridgeParams['GameDir'] = $GameDir }
        try {
            & (Join-Path $root 'bridge\InstallBridge.ps1') @bridgeParams
        } catch {
            Write-Warning "Bridge installation skipped: $_"
            Write-Warning "You can install it later with: .\bridge\InstallBridge.ps1 -GameDir `"<Your Melvor Idle 2 Path>`""
        }
    }
} else {
    Write-Host "[3/4] Skipped in-game bridge install (-SkipBridge)."
}

# 4. Generate local ready-to-copy MCP client JSON
Write-Host ""
Write-Host "[4/4] Generating local MCP client configuration ..." -ForegroundColor Yellow
$cfgObj = [ordered]@{
    mcpServers = [ordered]@{
        "melvor-idle-2" = [ordered]@{
            command = $venvPy
            args    = @("-m", "melvor2_mcp")
            cwd     = $root
            env     = [ordered]@{
                MELVOR2_BRIDGE_PORT = "17817"
            }
        }
    }
}
$cfgJson = $cfgObj | ConvertTo-Json -Depth 5
$localCfgPath = Join-Path $root 'mcp_config.local.json'
Set-Content -Path $localCfgPath -Value $cfgJson -Encoding UTF8

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Setup Complete! Copy this into your MCP client config:" -ForegroundColor Green
Write-Host "  (Saved to: $localCfgPath)" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Green
Write-Host $cfgJson
Write-Host ""
Write-Host "Dashboard URLs:" -ForegroundColor Cyan
Write-Host "  - In-game (when Melvor2 is running) : http://127.0.0.1:17817/"
Write-Host "  - Standalone (works offline too)    : .\StartDashboard.ps1 -Open  (http://127.0.0.1:17818/)"
