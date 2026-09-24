# Build the plugin, copy it to a versioned folder under Rhino's per-user Plug-ins directory,
# and register it with Rhino 8 so it loads at startup.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\install-local.ps1 [-Configuration Release]
# Close Rhino first if it already has the plugin loaded (the .rhp is locked while loaded).
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\ZoningEnvelope\ZoningEnvelope.csproj"
$dist = Join-Path $root "dist"
$pluginGuid = "7f3c2a41-9b8e-4d6f-a2c5-3e1b9d7f8a01"   # must match [assembly: Guid] in ZoningEnvelopePlugIn.cs

# build to dist\ so a running Rhino holding bin\ never blocks the build
dotnet build $proj -c $Configuration -o $dist
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { $version = "0.1.0" }
$rhp = Join-Path $dist "ZoningEnvelope.rhp"
if (-not (Test-Path $rhp)) { throw "not found: $rhp" }

# NOT the packages\ folder: Rhino's PackageManager deletes anything there it did not install itself.
# This is the per-user plug-in folder the PlugInManager uses.
$dest = Join-Path $env:APPDATA "McNeel\Rhinoceros\8.0\Plug-ins\ZoningEnvelope ($pluginGuid)\$version"
New-Item -ItemType Directory -Force $dest | Out-Null
$destRhp = Join-Path $dest "ZoningEnvelope.rhp"
try {
    Copy-Item $rhp $destRhp -Force
    Copy-Item (Join-Path $dist "ZoningEnvelope.pdb") $dest -Force -ErrorAction SilentlyContinue
} catch {
    throw "Could not overwrite $destRhp. Close Rhino and run this script again. ($($_.Exception.Message))"
}

# remove the old copy under packages\ if a previous version of this script put one there
$stale = Join-Path $env:APPDATA "McNeel\Rhinoceros\packages\8.0\ZoningEnvelope"
if (Test-Path $stale) { Remove-Item $stale -Recurse -Force -ErrorAction SilentlyContinue }

# Register with Rhino 8 (same keys the PlugInManager writes). LoadMode 2 = load at startup.
$key = "HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-Ins\$pluginGuid"
if (-not (Test-Path $key)) { New-Item -Path $key | Out-Null }
if (-not (Test-Path "$key\PlugIn")) { New-Item -Path "$key\PlugIn" | Out-Null }
Set-ItemProperty -Path $key -Name "Name" -Value "ZoningEnvelope"
Set-ItemProperty -Path $key -Name "EnglishName" -Value "ZoningEnvelope"
Set-ItemProperty -Path $key -Name "Type" -Value 16 -Type DWord
Set-ItemProperty -Path $key -Name "IsDotNETPlugIn" -Value 1 -Type DWord
# LoadMode 1 = load when one of its commands is used (safer for Rhino's startup / layout than 2 = at startup)
Set-ItemProperty -Path $key -Name "LoadMode" -Value 1 -Type DWord
Set-ItemProperty -Path "$key\PlugIn" -Name "FileName" -Value $destRhp

Write-Host ""
Write-Host "Installed $destRhp"
Write-Host "Registered for load-at-startup. Start (or restart) Rhino 8 and type: ZoneEnvelope"
