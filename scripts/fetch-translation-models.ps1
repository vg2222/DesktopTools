$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/DesktopTools/Assets/Translation'))
$entries = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $entries) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $entry.File))
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset path outside translation folder.' }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "Existing model checksum mismatch: $($entry.File)" }
        continue
    }
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
    $temporary = $target + '.download'
    Invoke-WebRequest -Uri $entry.Url -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "Downloaded model checksum mismatch: $($entry.File)" }
    Move-Item -LiteralPath $temporary -Destination $target
}
