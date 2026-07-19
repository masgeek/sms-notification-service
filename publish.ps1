#!/usr/bin/env pwsh
# Publish all projects to build/ subdirectories.
# Usage:
#   ./publish.ps1              # Publish all projects
#   ./publish.ps1 -Clean       # Remove bin/obj/build folders before building
#   ./publish.ps1 -SelfContained false  # Framework-dependent (smaller, needs .NET runtime on target)

param(
    [switch]$Clean,
    [string]$SelfContained = "true"
)

$ErrorActionPreference = "Stop"

$servicePublish = "build\service"
$trayPublish = "build\tray"
$consolePublish = "build\console"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "bin/Release", "obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Tray/bin/Release", "SmsNotificationService.Tray/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "SmsNotificationService.Console/bin/Release", "SmsNotificationService.Console/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "build" -ErrorAction SilentlyContinue
}

$scFlag = if ($SelfContained -eq "true") { "--self-contained" } else { "--no-self-contained" }

Write-Host "Publishing SmsNotificationService..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.csproj -c $configuration -r $runtime $scFlag -o $servicePublish

Write-Host "Publishing SmsNotificationService.Tray..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Tray/SmsNotificationService.Tray.csproj -c $configuration -r $runtime $scFlag -o $trayPublish

Write-Host "Publishing SmsNotificationService.Console..." -ForegroundColor Cyan
dotnet publish SmsNotificationService.Console/SmsNotificationService.Console.csproj -c $configuration -r $runtime $scFlag -o $consolePublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  build\service\    -> SmsNotificationService.exe"
Write-Host "  build\tray\       -> SmsNotificationService.Tray.exe"
Write-Host "  build\console\    -> SmsNotificationService.Console.exe"
