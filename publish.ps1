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
$agentPublish = "build\agent"
$trayPublish = "build\tray"
$consolePublish = "build\console"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "bin/Release", "obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "FeeSyncer.Sms/bin/Release", "FeeSyncer.Sms/obj/Release", "FeeSyncer.Agent/bin/Release", "FeeSyncer.Agent/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "FeeSyncer.Tray/bin/Release", "FeeSyncer.Tray/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "FeeSyncer.Console/bin/Release", "FeeSyncer.Console/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "build" -ErrorAction SilentlyContinue
}

$scFlag = if ($SelfContained -eq "true") { "--self-contained" } else { "--no-self-contained" }

Write-Host "Publishing FeeSyncer.Sms..." -ForegroundColor Cyan
dotnet publish FeeSyncer.Sms.csproj -c $configuration -r $runtime $scFlag -o $servicePublish

Write-Host "Publishing FeeSyncer.Agent..." -ForegroundColor Cyan
dotnet publish FeeSyncer.Agent/FeeSyncer.Agent.csproj -c $configuration -r $runtime $scFlag -o $agentPublish

Write-Host "Publishing FeeSyncer.Tray..." -ForegroundColor Cyan
dotnet publish FeeSyncer.Tray/FeeSyncer.Tray.csproj -c $configuration -r $runtime $scFlag -o $trayPublish

Write-Host "Publishing FeeSyncer.Console..." -ForegroundColor Cyan
dotnet publish FeeSyncer.Console/FeeSyncer.Console.csproj -c $configuration -r $runtime $scFlag -o $consolePublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  build\service\    -> FeeSyncer.Sms.exe"
Write-Host "  build\agent\      -> FeeSyncer.Agent.exe"
Write-Host "  build\tray\       -> FeeSyncer.Tray.exe"
Write-Host "  build\console\    -> FeeSyncer.Console.exe"
