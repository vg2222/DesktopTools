[CmdletBinding()]
param([string]$Directory = 'App release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bundle = if ([IO.Path]::IsPathRooted($Directory)) { $Directory } else { Join-Path $repo $Directory }
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/DesktopTools/DesktopTools.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$metadata = Get-Content -LiteralPath (Join-Path $bundle 'release.json') -Raw | ConvertFrom-Json
if ($metadata.product -ne 'DesktopTools' -or $metadata.version -ne $version) { throw 'Release metadata does not match the project.' }
$expected = @("DesktopTools-$version-win-x64-setup.exe", "DesktopTools-$version-win-x64-portable.zip")
if ($metadata.installer -ne $expected[0] -or $metadata.portableArchive -ne $expected[1]) { throw 'Release filenames do not match the version.' }
$entries = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $bundle 'SHA256SUMS.txt')) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([A-Fa-f0-9]{64})  ([^/\\]+)$') { throw 'Malformed checksum entry.' }
    $hash = $Matches[1]; $name = $Matches[2]
    if ($entries.ContainsKey($name) -or $name -notin $expected) { throw "Unexpected or duplicate checksum: $name" }
    $entries[$name] = $hash
}
if ($entries.Count -ne 2) { throw 'The manifest must cover both release assets.' }
foreach ($name in $expected) {
    $file = Get-Item -LiteralPath (Join-Path $bundle $name)
    if ($file.Length -le 0 -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entries[$name]) {
        throw "Release asset failed integrity verification: $name"
    }
}
if ($metadata.sourceModified -or !$metadata.commit) {
    Write-Warning 'Bundle records uncommitted source. This verifies integrity, not final release provenance.'
}
Write-Output "Verified DesktopTools $version metadata and both release checksums."
