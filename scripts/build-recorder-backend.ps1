[CmdletBinding()]
param(
    [string]$NativeRepositoryPath = (Join-Path $PSScriptRoot '../artifacts/ScreenRecorderLib'),
    [string]$MSBuildPath,
    [string]$PlatformToolset = 'v143',
    [string]$TargetFrameworkRootPath,
    [string]$ImportLibraryDirectory
)
$ErrorActionPreference = 'Stop'
$revision = '646383234187efd3854e6b1069612b80c3ab9981'
$patch = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../patches/ScreenRecorderLib-7.0.1-bounded-frames.patch'))
$NativeRepositoryPath = [IO.Path]::GetFullPath($NativeRepositoryPath)
if (-not (Test-Path -LiteralPath $NativeRepositoryPath)) {
    & git clone --depth 1 --branch v7.0.1 https://github.com/sskodje/ScreenRecorderLib.git $NativeRepositoryPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the pinned recorder source.' }
}
$actual = & git -C $NativeRepositoryPath rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $revision) { throw "Expected ScreenRecorderLib v7.0.1 ($revision), found $actual. No source was reset." }
& git -C $NativeRepositoryPath apply --check $patch 2>$null
if ($LASTEXITCODE -eq 0) {
    & git -C $NativeRepositoryPath apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Applying the recorder patch failed.' }
} else {
    & git -C $NativeRepositoryPath apply --reverse --check $patch 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'The recorder patch conflicts with this checkout; no files were reset.' }
}
if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.ATL -find 'MSBuild/Current/Bin/MSBuild.exe' | Select-Object -First 1
    }
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'Install Visual C++/CLI, ATL and .NET Framework 4.8 targeting tools, or supply -MSBuildPath.' }
$arguments = @('ScreenRecorderLib/ScreenRecorderLib.vcxproj', '/m', '/p:Configuration=Release', '/p:Platform=x64', "/p:PlatformToolset=$PlatformToolset", '/v:minimal')
if ($TargetFrameworkRootPath) { $arguments += "/p:TargetFrameworkRootPath=$TargetFrameworkRootPath" }
$originalCL = $env:CL; $originalTrailingCL = $env:_CL_; $originalLink = $env:LINK
Push-Location $NativeRepositoryPath
try {
    # The upstream coroutine header predates C++20; use its existing compatibility mode.
    $env:CL = "$originalCL /D_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS /MP4"
    $env:_CL_ = "$originalTrailingCL /Z7"
    if ($ImportLibraryDirectory) { $env:LINK = "$originalLink /LIBPATH:`"$ImportLibraryDirectory`"" }
    & $MSBuildPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "Native recorder build failed ($LASTEXITCODE)." }
    $binary = Join-Path $NativeRepositoryPath 'ScreenRecorderLib/bin/x64/Release/ScreenRecorderLib.dll'
    Get-FileHash -LiteralPath $binary -Algorithm SHA256
    Write-Output "Recorder backend: $binary"
    Write-Output 'Use dotnet build with -p:RecorderBackendPath=<absolute DLL path> to test this backend. Installed files are not replaced by this script.'
} finally {
    Pop-Location
    $env:CL = $originalCL; $env:_CL_ = $originalTrailingCL; $env:LINK = $originalLink
}
