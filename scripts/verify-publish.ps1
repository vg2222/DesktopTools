[CmdletBinding()]
param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Directory)
$ErrorActionPreference = 'Stop'
$packageRoot = (Get-Item -LiteralPath $Directory).FullName
$packagePrefix = $packageRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
foreach ($file in $files) {
    if ($file.Extension -in '.download', '.tmp', '.pdb', '.lib' -or $file.Name -eq 'ScreenRecorderLib.xml') {
        throw "Temporary or development-only file in package: $($file.FullName.Substring($packagePrefix.Length))"
    }
}
foreach ($document in ($files | Where-Object Extension -eq '.md')) {
    $text = Get-Content -LiteralPath $document.FullName -Raw -Encoding utf8
    foreach ($match in [regex]::Matches($text, '\]\((?<target>[^\s)]+)')) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:|#|//)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '[#?]', 2)[0])
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $document.DirectoryName $target))
        if (-not $resolved.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $resolved)) {
            throw "Broken package documentation link: $($document.FullName.Substring($packagePrefix.Length)) -> $target"
        }
    }
}
Write-Output 'Verified published file inventory and relative documentation links.'
