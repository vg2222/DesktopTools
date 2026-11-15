$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  url64bit      = 'https://github.com/vg2222/DesktopTools/releases/download/v1.3.0/DesktopTools-1.3.0-win-x64-portable.zip'
  checksum64    = '6BECBF0763F08A698CFBAB7CF6E00F1F5D905CE07B875AC8B53BAD53D26228C5'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs
$exe = Join-Path $toolsDir 'DesktopTools-win-x64\DesktopTools.exe'
Install-ChocolateyShortcut -ShortcutFilePath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk') -TargetPath $exe
