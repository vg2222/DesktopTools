$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  url64bit      = 'https://github.com/vg2222/DesktopTools/releases/download/v1.2.7/DesktopTools-1.2.7-win-x64-portable.zip'
  checksum64    = '277E9609D8764F8E5A5A330EFC98F376432427AD2C25BF430361C01D4A4B24F1'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs
$exe = Join-Path $toolsDir 'DesktopTools-win-x64\DesktopTools.exe'
Install-ChocolateyShortcut -ShortcutFilePath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk') -TargetPath $exe
