# Build script for Timberborn mods (Windows)
param(
    [string[]]$Mods
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Check prerequisites
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK not found. Install from: https://dotnet.microsoft.com/download"
    exit 1
}

# Default Steam paths for Windows
$SteamDir = "C:\Program Files (x86)\Steam\steamapps\common\Timberborn"
if ($env:STEAM_DIR) { $SteamDir = $env:STEAM_DIR }

$GameManaged = "$SteamDir\Timberborn_Data\Managed"
$ModsDir = "$env:USERPROFILE\Documents\Timberborn\Mods"

if ($env:GAME_MANAGED_DIR) { $GameManaged = $env:GAME_MANAGED_DIR }
if ($env:TIMBERBORN_MODS_DIR) { $ModsDir = $env:TIMBERBORN_MODS_DIR }

# Verify game install
if (-not (Test-Path $GameManaged)) {
    Write-Error "Game not found at $GameManaged`nSet STEAM_DIR or GAME_MANAGED_DIR env var."
    exit 1
}

# Find mods to build
if (-not $Mods) {
    $Mods = @()
    Get-ChildItem -Path $ScriptDir -Directory | ForEach-Object {
        if (Get-ChildItem -Path $_.FullName -Filter "*.csproj" -ErrorAction SilentlyContinue) {
            $Mods += $_.Name
        }
    }
}

if ($Mods.Count -eq 0) {
    Write-Host "No mods found to build."
    exit 1
}

foreach ($mod in $Mods) {
    $ModDir = Join-Path $ScriptDir $mod
    if (-not (Test-Path $ModDir)) {
        Write-Error "$mod directory not found"
        exit 1
    }

    $DeployDir = Join-Path $ModsDir $mod

    Write-Host "=== Building $mod ===" -ForegroundColor Cyan
    dotnet build $ModDir `
        "-p:GameManagedDir=$GameManaged" `
        "-p:DeployDir=$DeployDir"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "FAILED: $mod"
        exit 1
    }

    Write-Host "  Deployed to $DeployDir`n"
}

Write-Host "All mods built and deployed successfully.`n" -ForegroundColor Green
Write-Host "Deployed mods:"
foreach ($mod in $Mods) {
    Write-Host "  $(Join-Path $ModsDir $mod)\"
}
