param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'OceanExecutorUI' 'OceanExecutorUI.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'The .NET SDK is not installed or not on PATH. Install .NET 6 SDK from https://dotnet.microsoft.com/download and re-run this script.'
    exit 1
}

Write-Host "Restoring packages..." -ForegroundColor Cyan
$restoreResult = dotnet restore $projectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
$buildResult = dotnet build $projectPath -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Launching app ($Configuration)..." -ForegroundColor Cyan
$runResult = dotnet run --project $projectPath -c $Configuration
exit $LASTEXITCODE
