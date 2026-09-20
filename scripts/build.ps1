[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $repo 'artifacts/dotnet-home'
    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
}
Push-Location $repo
try {
    & dotnet restore DesktopTools.sln
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }
    & dotnet build DesktopTools.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
} finally { Pop-Location }
