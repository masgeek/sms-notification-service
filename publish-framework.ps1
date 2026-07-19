#!/usr/bin/env pwsh
# Publish framework-dependent (non self-contained) artifacts.
# Requires .NET runtime on the target machine.
# Usage:
#   ./publish-framework.ps1              # Publish all projects
#   ./publish-framework.ps1 -Clean       # Remove bin/obj/build folders first

param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$servicePublish = "build\service-framework"
$trayPublish = "build\tray-framework"
$consolePublish = "build\console-framework"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "bin/Release", "obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Tray/bin/Release", "SmsNotificationService.Tray/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Console/bin/Release", "SmsNotificationService.Console/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "build" -ErrorAction SilentlyContinue
}

Write-Host "Publishing SmsNotificationService (framework-dependent)..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.csproj -c $configuration -r $runtime --no-self-contained -o $servicePublish

Write-Host "Publishing SmsNotificationService.Tray (framework-dependent)..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Tray/SmsNotificationService.Tray.csproj -c $configuration -r $runtime --no-self-contained -o $trayPublish

Write-Host "Publishing SmsNotificationService.Console (framework-dependent)..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Console/SmsNotificationService.Console.csproj -c $configuration -r $runtime --no-self-contained -o $consolePublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  build\service-framework\    -> SmsNotificationService.exe"
Write-Host "  build\tray-framework\       -> SmsNotificationService.Tray.exe"
Write-Host "  build\console-framework\    -> SmsNotificationService.Console.exe"
