$ErrorActionPreference = 'Stop'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
