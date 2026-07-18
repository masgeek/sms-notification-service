#!/usr/bin/env pwsh
# Publish both projects to their respective output directories.
# Usage:
#   ./publish.ps1              # Publish both projects
#   ./publish.ps1 -Clean       # Remove bin/obj/publish folders before building
#   ./publish.ps1 -SelfContained false  # Framework-dependent (smaller, needs .NET runtime on target)

param(
    [switch]$Clean,
    [string]$SelfContained = "true"
)

$ErrorActionPreference = "Stop"

$servicePublish = "publish"
$trayPublish = "publish-tray"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "bin/Release", "obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Tray/bin/Release", "SmsNotificationService.Tray/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $servicePublish, $trayPublish -ErrorAction SilentlyContinue
}

$scFlag = if ($SelfContained -eq "true") { "--self-contained" } else { "--no-self-contained" }

Write-Host "Publishing SmsNotificationService..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.csproj -c $configuration -r $runtime $scFlag -o $servicePublish

Write-Host "Publishing SmsNotificationService.Tray..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Tray/SmsNotificationService.Tray.csproj -c $configuration -r $runtime $scFlag -o $trayPublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  $servicePublish/  -> SmsNotificationService.exe"
Write-Host "  $trayPublish/ -> SmsNotificationService.Tray.exe"
