[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$AppPath,
    [Parameter(Mandatory=$true)][string]$Repository,
    [string]$Branch = "main",
    [string]$Tag,
    [string]$BackupRoot = "C:\fee-processor-backups",
    [string]$IisSiteName = "FeeProcessor",
    [string[]]$WindowsServices = @("FeeProcessorQueue")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Tool([string]$Name) {
    $command = Get-Command $Name -CommandType Application,ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { throw "$Name was not found on PATH." }
    $command.Source
}
function Run([string]$File, [string[]]$Args) {
    Push-Location $AppPath
    try { & $File @Args; if ($LASTEXITCODE -ne 0) { throw "$File exited with code $LASTEXITCODE" } }
    finally { Pop-Location }
}
function Service([string]$Action, [string]$Name) {
    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        & sc.exe $Action $Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to $Action service $Name." }
    }
}

if ($Tag -and $PSBoundParameters.ContainsKey("Branch")) { throw "Specify either Branch or Tag, not both." }
if (-not (Test-Path (Join-Path $AppPath ".git"))) { throw "Not a Git checkout: $AppPath" }
$git = Tool "git"; $php = Tool "php"; $composer = Tool "composer"
$webAdmin = Get-Module -ListAvailable -Name WebAdministration
$origin = (& $git -C $AppPath remote get-url origin 2>$null).Trim()
if ($origin -and $origin -ne $Repository) { throw "Git origin mismatch." }
if ((& $git -C $AppPath status --porcelain).Trim()) { throw "Checkout has local changes." }
$previous = (& $git -C $AppPath rev-parse HEAD).Trim()
$BackupRoot = if ([string]::IsNullOrWhiteSpace($BackupRoot)) { Join-Path $AppPath "backups" } else { $BackupRoot }
$backup = Join-Path $BackupRoot (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Set-Content (Join-Path $backup "previous-commit.txt") $previous
Copy-Item (Join-Path $AppPath ".env") (Join-Path $backup ".env") -Force -ErrorAction SilentlyContinue
try {
    foreach ($service in $WindowsServices) { Service "stop" $service }
    if ($webAdmin) {
        Import-Module WebAdministration
        if (Get-Website -Name $IisSiteName -ErrorAction SilentlyContinue) { Stop-Website $IisSiteName }
    }
    Run $git @("fetch", "--prune", "--tags", "origin")
    if ($Tag) {
        $resolvedTag = $Tag
        if ($Tag -eq "*") {
            $resolvedTag = (& $git -C $AppPath ls-remote --tags --sort=-v:refname origin "refs/tags/*" |
                Where-Object { $_ -notmatch '\^\{\}$' } |
                Select-Object -First 1) -replace '^\S+\s+refs/tags/', ''
            if ([string]::IsNullOrWhiteSpace($resolvedTag)) { throw "No remote tags were found." }
            Write-Host "Resolved * to latest tag $resolvedTag."
        }
        Run $git @("checkout", "--force", "tags/$resolvedTag")
    }
    else { Run $git @("checkout", "--force", $Branch); Run $git @("reset", "--hard", "origin/$Branch") }
    Run $composer @("install", "--no-dev", "--optimize-autoloader", "--no-interaction")
    Run $php @("artisan", "migrate", "--force")
    foreach ($command in @("optimize:clear", "config:cache", "route:cache", "view:cache", "event:cache")) {
        Run $php @("artisan", $command)
    }
    if ($webAdmin -and (Get-Website -Name $IisSiteName -ErrorAction SilentlyContinue)) { Start-Website $IisSiteName }
    foreach ($service in $WindowsServices) { Service "start" $service }
} catch {
    foreach ($service in $WindowsServices) { Service "start" $service }
    if ($webAdmin -and (Get-Website -Name $IisSiteName -ErrorAction SilentlyContinue)) { Start-Website $IisSiteName }
    throw
}
