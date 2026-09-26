[CmdletBinding()]
param([ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$')][string]$ArchiveName = 'DesktopTools-win-x64.zip')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $repo 'artifacts/dotnet-home'
    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
}
$artifacts = Join-Path $repo 'artifacts'
# A unique staging directory avoids stale files and never deletes an existing user's package.
$stage = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $stage 'DesktopTools-win-x64'
$archive = Join-Path $artifacts $ArchiveName
Push-Location $repo
try {
    # Test builds may replace project.assets.json with a framework-only restore. Clean also
    # resolves packages, so restore the release RID before asking it to clean that target.
    & dotnet restore src/DesktopTools/DesktopTools.csproj -r win-x64 -p:SelfContained=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Release restore failed with exit code $LASTEXITCODE." }
    # A clean release compile prevents stale CodeView/PDB paths from an earlier incremental build entering public binaries.
    & dotnet clean src/DesktopTools/DesktopTools.csproj -c Release -r win-x64 -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Clean failed with exit code $LASTEXITCODE." }
    & dotnet publish src/DesktopTools/DesktopTools.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishTrimmed=false -p:PublishSingleFile=false -p:DebugSymbols=false -p:DebugType=None -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'DesktopTools.exe'))) { throw 'Published executable is missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'ScreenRecorderLib.dll'))) { throw 'Screen recorder backend is missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $repo 'docs/licenses/ScreenRecorderLib-LICENSE.txt'))) { throw 'Screen recorder license is missing.' }
    $translationRoot = Join-Path $output 'Assets/Translation'
    foreach ($entry in (Get-Content (Join-Path $translationRoot 'manifest.json') -Raw | ConvertFrom-Json)) {
        if ((Get-FileHash -LiteralPath (Join-Path $translationRoot $entry.File) -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "Translation asset missing or damaged: $($entry.File). Run scripts/fetch-translation-models.ps1." }
    }
    foreach ($notice in @('EN-RU-APACHE-2.0.txt','CC-BY-4.0.txt','TOKENIZERS-LICENSE.txt','TOKENIZERS-NOTICES.txt','ATTRIBUTION.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $translationRoot $notice))) { throw "Missing translation notice: $notice" }
    }
    foreach ($asset in @('u2netp.onnx', 'U2NET-LICENSE.txt', 'ONNXRUNTIME-LICENSE.txt', 'ONNXRUNTIME-NOTICES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $output ('Assets/Models/' + $asset)))) { throw "Missing background removal asset: $asset" }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $output 'Assets/Models/u2netp.onnx') -Algorithm SHA256).Hash -ne '309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8') { throw 'Background removal model checksum mismatch.' }
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $output 'DesktopTools-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $output 'DesktopTools-THIRD-PARTY-NOTICES.md')
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $output
    Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $output
    Copy-Item -LiteralPath (Join-Path $repo 'CONTRIBUTING.md') -Destination $output
    Copy-Item -LiteralPath (Join-Path $repo 'AGENTS.md') -Destination $output
    foreach ($readme in @('README.md','README.ru.md','README.de.md','README.fr.md','README.es.md')) {
        Copy-Item -LiteralPath (Join-Path $repo $readme) -Destination $output
    }
    Copy-Item -LiteralPath (Join-Path $repo 'SECURITY.md') -Destination $output
    $readmeAssets = Join-Path $output 'assets/readme'
    New-Item -ItemType Directory -Force -Path $readmeAssets | Out-Null
    foreach ($media in @('hero.gif','home.png','editor.png','recorder.png')) {
        Copy-Item -LiteralPath (Join-Path $repo ('assets/readme/' + $media)) -Destination $readmeAssets
    }
    $publicDocs = Join-Path $output 'docs'
    New-Item -ItemType Directory -Force -Path $publicDocs | Out-Null
    foreach ($document in @('architecture.md','audio-controls.md','background-removal.md','compatibility.md','localization.md','manual-testing.md','roadmap.md','screen-recorder.md','sharing-blackout.md','text-tools.md','updates.md','verification.md','video-editor.md','security-checks.md','release-preparation.md')) {
        Copy-Item -LiteralPath (Join-Path $repo ('docs/' + $document)) -Destination $publicDocs
    }
    Copy-Item -LiteralPath (Join-Path $repo 'docs/licenses') -Destination $publicDocs -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'docs/releases') -Destination $publicDocs -Recurse
    # Only published README media are bundled; local capture galleries stay local.
    # Runtime packs keep their notices at package root; dotnet publish does not copy them automatically.
    $assets = Get-Content -LiteralPath (Join-Path $repo 'src/DesktopTools/obj/project.assets.json') -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath (Join-Path $output 'DesktopTools.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $packageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $packagePath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($packageId + '/' + $framework.version)
            if (Test-Path -LiteralPath $candidate) { $packagePath = $candidate; break }
        }
        if (-not $packagePath) { throw "Cannot locate license files for $packageId $($framework.version)." }
        $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
        if (-not ($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Missing runtime license for $packageId." }
        $noticeFolder = Join-Path $output ('notices/' + $packageId)
        New-Item -ItemType Directory -Force -Path $noticeFolder | Out-Null
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $noticeFolder }
    }
    # Public binaries omit debug symbols and development-only native metadata. Symbols can be produced separately when needed.
    Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
        $_.Extension -in '.pdb', '.lib' -or $_.Name -eq 'ScreenRecorderLib.xml'
    } | Remove-Item -Force
    $temporaryArchive = Join-Path $stage 'DesktopTools-win-x64.zip'
    Compress-Archive -LiteralPath $output -DestinationPath $temporaryArchive -CompressionLevel Optimal
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
    Write-Host "Portable folder: $output"
    Write-Host "Portable archive: $archive"
    Write-Host "SHA256: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)"
} finally { Pop-Location }
