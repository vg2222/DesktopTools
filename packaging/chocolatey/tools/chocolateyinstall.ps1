$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  url64bit      = 'https://github.com/vg2222/DesktopTools/releases/download/v1.2.9/DesktopTools-1.2.9-win-x64-portable.zip'
  checksum64    = 'B2EC92406C6D4BD398EA56A9BF3469A533C375529505C57A44AF132386599445'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs
$exe = Join-Path $toolsDir 'DesktopTools-win-x64\DesktopTools.exe'
Install-ChocolateyShortcut -ShortcutFilePath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk') -TargetPath $exe
