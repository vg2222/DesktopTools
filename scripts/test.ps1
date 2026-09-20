[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$Interactive, [switch]$Performance)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $repo 'artifacts/dotnet-home'
    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
}
Push-Location $repo
try {
    foreach ($project in @('tests/DesktopTools.Tests/DesktopTools.Tests.csproj', 'tests/DesktopTools.NativeTests/DesktopTools.NativeTests.csproj', 'tests/DesktopTools.CaptureTests/DesktopTools.CaptureTests.csproj', 'tests/DesktopTools.ImageTests/DesktopTools.ImageTests.csproj', 'tests/DesktopTools.UpdateTests/DesktopTools.UpdateTests.csproj', 'tests/DesktopTools.InstallerTests/DesktopTools.InstallerTests.csproj')) {
        & dotnet run --project $project -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Tests failed for $project with exit code $LASTEXITCODE." }
    }
    if ($Interactive -or $Performance) {
        $testArguments = @('run', '--project', 'tests/DesktopTools.IntegrationTests/DesktopTools.IntegrationTests.csproj', '-c', $Configuration)
        if ($Performance) { $testArguments += @('--', '--performance') }
        & dotnet @testArguments
        if ($LASTEXITCODE -ne 0) { throw "Interactive tests failed with exit code $LASTEXITCODE." }
    }
} finally { Pop-Location }
