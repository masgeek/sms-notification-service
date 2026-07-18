#!/usr/bin/env pwsh
# Publish framework-dependent (non self-contained) artifacts.
# Requires .NET runtime on the target machine.
# Usage:
#   ./publish-framework.ps1              # Publish both projects
#   ./publish-framework.ps1 -Clean       # Remove bin/obj/publish folders first

param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$servicePublish = "publish-framework"
$trayPublish = "publish-tray-framework"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "bin/Release", "obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Tray/bin/Release", "SmsNotificationService.Tray/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $servicePublish, $trayPublish -ErrorAction SilentlyContinue
}

Write-Host "Publishing SmsNotificationService (framework-dependent)..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.csproj -c $configuration -r $runtime --no-self-contained -o $servicePublish

Write-Host "Publishing SmsNotificationService.Tray (framework-dependent)..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Tray/SmsNotificationService.Tray.csproj -c $configuration -r $runtime --no-self-contained -o $trayPublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  $servicePublish/  -> SmsNotificationService.exe"
Write-Host "  $trayPublish/ -> SmsNotificationService.Tray.exe"
