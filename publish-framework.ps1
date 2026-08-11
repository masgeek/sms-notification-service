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
$agentPublish = "build\agent-framework"
$trayPublish = "build\tray-framework"
$consolePublish = "build\console-framework"
$runtime = "win-x64"
$configuration = "Release"

if ($Clean) {
    Write-Host "Cleaning intermediate output..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force "src/Sms/bin/Release", "src/Sms/obj/Release" -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force "src/Agent/bin/Release", "src/Agent/obj/Release" -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force "src/Tray/bin/Release", "src/Tray/obj/Release" -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force "src/Console/bin/Release", "src/Console/obj/Release" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "build" -ErrorAction SilentlyContinue
}

Write-Host "Publishing FeeSyncer.Sms (framework-dependent)..." -ForegroundColor Cyan
dotnet publish src/Sms/FeeSyncer.Sms.csproj -c $configuration -r $runtime --no-self-contained -o $servicePublish

Write-Host "Publishing FeeSyncer.Agent (framework-dependent)..." -ForegroundColor Cyan
dotnet publish src/Agent/FeeSyncer.Agent.csproj -c $configuration -r $runtime --no-self-contained -o $agentPublish

Write-Host "Publishing FeeSyncer.Tray (framework-dependent)..." -ForegroundColor Cyan
dotnet publish src/Tray/FeeSyncer.Tray.csproj -c $configuration -r $runtime --no-self-contained -o $trayPublish

Write-Host "Publishing FeeSyncer.Console (framework-dependent)..." -ForegroundColor Cyan
dotnet publish src/Console/FeeSyncer.Console.csproj -c $configuration -r $runtime --no-self-contained -o $consolePublish

Write-Host "`nDone. Output:" -ForegroundColor Green
Write-Host "  build\service-framework\    -> FeeSyncer.Sms.exe"
Write-Host "  build\agent-framework\      -> FeeSyncer.Agent.exe"
Write-Host "  build\tray-framework\       -> FeeSyncer.Tray.exe"
Write-Host "  build\console-framework\    -> FeeSyncer.Console.exe"
