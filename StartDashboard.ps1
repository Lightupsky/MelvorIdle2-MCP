# Launches the Melvor Idle 2 Plan & Status Web Dashboard on http://127.0.0.1:17818
param(
    [int]$Port = 17818,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$venvPy = Join-Path $root '.venv\Scripts\python.exe'
if (Test-Path $venvPy) {
    $py = $venvPy
} else {
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if (-not $cmd) { Write-Error "Python not found. Run .\Setup.ps1 first." }
    $py = $cmd.Source
}

$argsList = @('-m', 'melvor2_mcp.dashboard', '--port', $Port)
if ($Open) { $argsList += '--open' }
& $py @argsList
