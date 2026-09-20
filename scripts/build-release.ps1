[CmdletBinding()]
param([string]$OutputDirectory = 'App release')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/DesktopTools/DesktopTools.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+([.-][A-Za-z0-9.-]+)?$') { throw "Invalid project version: $version" }

$releaseRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory)) }
$repoRoot = [IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
if (-not $releaseRoot.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output must stay inside the repository.' }
if ([IO.Path]::GetFileName($releaseRoot) -ne 'App release') { throw 'Release output folder must be named App release.' }
if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

$archiveName = "DesktopTools-$version-win-x64-portable.zip"
$artifactArchive = Join-Path $repo ('artifacts/' + $archiveName)
$before = @(Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts') -Directory -Filter 'publish-*' | ForEach-Object FullName)
& (Join-Path $PSScriptRoot 'publish.ps1') -ArchiveName $archiveName
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed with exit code $LASTEXITCODE." }
$portableSource = Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts') -Directory -Filter 'publish-*' |
    Where-Object { $_.FullName -notin $before } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $portableSource) { throw 'Could not identify the new portable staging directory.' }
$portableSource = Join-Path $portableSource.FullName 'DesktopTools-win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $portableSource 'DesktopTools.exe'))) { throw 'Published executable is missing.' }

& (Join-Path $portableSource 'DesktopTools.exe') --smoke
if ($LASTEXITCODE -ne 0) { throw "Published smoke check failed with exit code $LASTEXITCODE." }
$smokeOutput = Join-Path $portableSource 'artifacts'
if (Test-Path -LiteralPath $smokeOutput) { Remove-Item -LiteralPath $smokeOutput -Recurse -Force }

$portableFolder = Join-Path $releaseRoot 'DesktopTools-portable'
Copy-Item -LiteralPath $portableSource -Destination $portableFolder -Recurse
$portableArchive = Join-Path $releaseRoot $archiveName
Copy-Item -LiteralPath $artifactArchive -Destination $portableArchive

$installerStage = Join-Path $repo ('artifacts/installer-' + [Guid]::NewGuid().ToString('N'))
$uninstallOutput = Join-Path $installerStage 'uninstall'
$setupOutput = Join-Path $installerStage 'setup'
New-Item -ItemType Directory -Force -Path $installerStage | Out-Null
try {
    $installerProject = Join-Path $repo 'installer/DesktopTools.Installer.csproj'
    & dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:InstallerMode=Uninstall -p:InstallerVersion=$version -o $uninstallOutput
    if ($LASTEXITCODE -ne 0) { throw "GUI uninstaller publish failed with exit code $LASTEXITCODE." }
    $uninstaller = Join-Path $uninstallOutput 'DesktopTools.Installer.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'GUI uninstaller executable is missing.' }
    & dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:InstallerMode=Setup -p:InstallerVersion=$version "-p:PayloadZip=$portableArchive" "-p:UninstallerExe=$uninstaller" -o $setupOutput
    if ($LASTEXITCODE -ne 0) { throw "GUI setup publish failed with exit code $LASTEXITCODE." }
    $builtSetup = Join-Path $setupOutput 'DesktopTools.Installer.exe'
    if (-not (Test-Path -LiteralPath $builtSetup)) { throw 'GUI setup executable is missing.' }
    $setup = Join-Path $releaseRoot "DesktopTools-$version-win-x64-setup.exe"
    Copy-Item -LiteralPath $builtSetup -Destination $setup
}
finally {
    if (Test-Path -LiteralPath $installerStage) { Remove-Item -LiteralPath $installerStage -Recurse -Force }
}

$releaseNotes = Join-Path $repo "docs/releases/$version.md"
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $releaseRoot 'RELEASE-NOTES.md')
$readme = @"
DesktopTools $version

Recommended: DesktopTools-$version-win-x64-setup.exe
Portable:    $archiveName or the DesktopTools-portable folder

The installer is per-user, lets you choose a writable installation folder, creates Start Menu and Desktop shortcuts, and registers an uninstaller. Existing installations offer update, repair and uninstall. Uninstall keeps notes and settings by default, with an explicit option to delete DesktopTools-managed data. External original media is preserved. The default location requires no administrator rights.

Updates: Settings > Updates checks stable GitHub releases at startup and hourly by default, with configurable intervals or an off switch. Background updating confirms restart and unsaved-work loss before downloading a checksum-verified installer.

This release is unsigned. Windows SmartScreen may show a warning. Verify SHA256SUMS.txt before running downloaded files.

Requirements: Windows 11 x64. Screen recording also requires Microsoft Visual C++ x64 Redistributable and Windows Media Foundation. Setup checks the Visual C++ runtime and offers Microsoft's official download page when missing. Choose x64 and run Microsoft's installer, then return and select Check again. Other tools can be installed and used without it. The runtime is licensed separately and is not bundled or silently installed by DesktopTools.
Project: https://github.com/vg2222/DesktopTools
"@
Set-Content -LiteralPath (Join-Path $releaseRoot 'README.txt') -Value $readme -Encoding utf8

$assets = @(
    Get-Item -LiteralPath (Join-Path $releaseRoot "DesktopTools-$version-win-x64-setup.exe")
    Get-Item -LiteralPath $portableArchive)
$checksums = foreach ($asset in $assets) { '{0}  {1}' -f (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash, $asset.Name }
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $checksums -Encoding ascii
$commit = $null
$resolvedCommit = & git -C $repo rev-parse --verify HEAD 2>$null
if ($LASTEXITCODE -eq 0) { $commit = $resolvedCommit.Trim() }
$sourceModified = @(& git -C $repo status --porcelain --untracked-files=normal).Count -gt 0
if ($sourceModified) { $commit = $null }
$metadata = [ordered]@{
    product = 'DesktopTools'; version = $version; platform = 'Windows 11 x64'; generatedUtc = [DateTime]::UtcNow.ToString('o')
    installer = $assets[0].Name; portableArchive = $assets[1].Name; portableFolder = 'DesktopTools-portable'
    signed = $false; commit = $commit; sourceModified = $sourceModified
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'release.json') -Encoding utf8
Write-Host "Release folder: $releaseRoot"
Get-ChildItem -LiteralPath $releaseRoot | Select-Object Name, Length
