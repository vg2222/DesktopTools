$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  url64bit      = 'https://github.com/vg2222/DesktopTools/releases/download/v1.2.8/DesktopTools-1.2.8-win-x64-portable.zip'
  checksum64    = 'FDD217FC07E4DE50DD9DB3A999F0DC7FE2D6848DDD0017421FF0F56A441AF79C'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs
$exe = Join-Path $toolsDir 'DesktopTools-win-x64\DesktopTools.exe'
Install-ChocolateyShortcut -ShortcutFilePath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk') -TargetPath $exe
