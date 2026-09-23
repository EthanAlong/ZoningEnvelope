# Build the plugin and drop it where Rhino 8 auto-registers packages.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\install-local.ps1 [-Configuration Release]
# Then start (or restart) Rhino 8 and run: ZoneEnvelope
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\ZoningEnvelope\ZoningEnvelope.csproj"

dotnet build $proj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { $version = "0.1.0" }
$out = Join-Path $root "src\ZoningEnvelope\bin\$Configuration\net8.0-windows"
$rhp = Join-Path $out "ZoningEnvelope.rhp"
if (-not (Test-Path $rhp)) { throw "not found: $rhp" }

$dest = Join-Path $env:APPDATA "McNeel\Rhinoceros\packages\8.0\ZoningEnvelope\$version"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $rhp $dest -Force
Copy-Item (Join-Path $out "ZoningEnvelope.pdb") $dest -Force -ErrorAction SilentlyContinue

# manifest so the Package Manager lists it
@"
name: zoningenvelope
version: $version
authors:
  - Ethan Huang
description: Zoning code to buildable envelope, live in Rhino.
"@ | Out-File (Join-Path $dest "manifest.yml") -Encoding utf8

Write-Host ""
Write-Host "Installed to $dest"
Write-Host "Start Rhino 8 (restart if it is running) and type: ZoneEnvelope"
Write-Host "If the command is unknown, drag $rhp onto the Rhino window once (PlugInManager install)."
