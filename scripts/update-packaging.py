"""Regenerate the winget, Scoop and Chocolatey package files for a published release.

    python scripts/update-packaging.py 1.2.7

Reads the release's SHA256SUMS.txt from GitHub, so the hashes always come from the files users download. Output goes to packaging/.
Nothing is submitted anywhere; see docs/packaging.md for the publishing steps.
"""
import json, re, sys, urllib.request
from datetime import date
from pathlib import Path

REPO = "vg2222/DesktopTools"
version = sys.argv[1] if len(sys.argv) > 1 else sys.exit(__doc__)
if not re.fullmatch(r"\d+\.\d+\.\d+", version): sys.exit("Version must look like 1.2.7")
base = f"https://github.com/{REPO}/releases/download/v{version}"
zip_name = f"DesktopTools-{version}-win-x64-portable.zip"

def fetch(url):
    with urllib.request.urlopen(url, timeout=60) as r: return r.read()

sums = {}
for line in fetch(f"{base}/SHA256SUMS.txt").decode().splitlines():
    parts = line.split()
    if len(parts) == 2: sums[parts[1].lstrip("*")] = parts[0].upper()
if zip_name not in sums: sys.exit(f"{zip_name} is not listed in SHA256SUMS.txt")
sha = sums[zip_name]
released = json.loads(fetch(f"https://api.github.com/repos/{REPO}/releases/tags/v{version}"))["published_at"][:10]
root = Path(__file__).resolve().parents[1] / "packaging"

# --- winget (portable zip: the installer is a graphical wizard without silent switches, which winget requires)
w = root / "winget" / "manifests" / "v" / "vg2222" / "DesktopTools" / version
w.mkdir(parents=True, exist_ok=True)
ident = "vg2222.DesktopTools"
(w / f"{ident}.yaml").write_text(f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: {ident}
PackageVersion: {version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
""", encoding="utf-8", newline="\n")
(w / f"{ident}.installer.yaml").write_text(f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: {ident}
PackageVersion: {version}
InstallerLocale: en-US
MinimumOSVersion: 10.0.22000.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: DesktopTools-win-x64\\DesktopTools.exe
  PortableCommandAlias: desktoptools
Commands:
- desktoptools
ReleaseDate: {released}
Installers:
- Architecture: x64
  InstallerUrl: {base}/{zip_name}
  InstallerSha256: {sha}
ManifestType: installer
ManifestVersion: 1.6.0
""", encoding="utf-8", newline="\n")
(w / f"{ident}.locale.en-US.yaml").write_text(f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: {ident}
PackageVersion: {version}
PackageLocale: en-US
Publisher: vg2222
PublisherUrl: https://github.com/vg2222
PublisherSupportUrl: https://github.com/{REPO}/issues
PackageName: DesktopTools
PackageUrl: https://github.com/{REPO}
License: MIT
LicenseUrl: https://github.com/{REPO}/blob/main/LICENSE
Copyright: Copyright (c) vg2222
ShortDescription: Screenshots, screen recording, drawing and presentation tools for Windows 11.
Description: |-
  DesktopTools brings region capture and annotation, screen recording and a video editor, on-screen drawing, laser pointer and spotlight,
  OCR, offline translation, floating notes, a teleprompter and more into one local-first app. No account and no telemetry.
Moniker: desktoptools
Tags:
- screenshot
- screen-recorder
- annotation
- presentation
- ocr
- teleprompter
ReleaseNotesUrl: https://github.com/{REPO}/releases/tag/v{version}
ManifestType: defaultLocale
ManifestVersion: 1.6.0
""", encoding="utf-8", newline="\n")

# --- Scoop (bucket manifest; autoupdate keeps it current once the bucket is published)
s = root / "scoop"; s.mkdir(parents=True, exist_ok=True)
(s / "desktoptools.json").write_text(json.dumps({
    "version": version,
    "description": "Screenshots, screen recording, drawing and presentation tools for Windows 11.",
    "homepage": f"https://github.com/{REPO}",
    "license": "MIT",
    "architecture": {"64bit": {"url": f"{base}/{zip_name}", "hash": sha.lower()}},
    "extract_dir": "DesktopTools-win-x64",
    "shortcuts": [["DesktopTools.exe", "DesktopTools"]],
    "checkver": "github",
    "autoupdate": {"architecture": {"64bit": {"url": f"https://github.com/{REPO}/releases/download/v$version/DesktopTools-$version-win-x64-portable.zip"}},
                   "hash": {"url": f"https://github.com/{REPO}/releases/download/v$version/SHA256SUMS.txt", "regex": "$sha256\\s+DesktopTools-$version-win-x64-portable.zip"}},
    "notes": "The app is not code-signed; Windows SmartScreen may ask for confirmation on first start.",
}, indent=4) + "\n", encoding="utf-8", newline="\n")

# --- Chocolatey
c = root / "chocolatey"; (c / "tools").mkdir(parents=True, exist_ok=True)
(c / "desktoptools.nuspec").write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>desktoptools</id>
    <version>{version}</version>
    <title>DesktopTools</title>
    <authors>vg2222</authors>
    <projectUrl>https://github.com/{REPO}</projectUrl>
    <licenseUrl>https://github.com/{REPO}/blob/main/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <projectSourceUrl>https://github.com/{REPO}</projectSourceUrl>
    <bugTrackerUrl>https://github.com/{REPO}/issues</bugTrackerUrl>
    <releaseNotes>https://github.com/{REPO}/releases/tag/v{version}</releaseNotes>
    <tags>screenshot screen-recorder annotation presentation ocr teleprompter windows</tags>
    <summary>Screenshots, screen recording, drawing and presentation tools for Windows 11.</summary>
    <description>DesktopTools brings region capture and annotation, screen recording and a video editor, on-screen drawing, laser pointer and spotlight, OCR, offline translation, floating notes and a teleprompter into one local-first app. No account and no telemetry. Windows 11 x64.</description>
  </metadata>
  <files>
    <file src="tools\\**" target="tools" />
  </files>
</package>
""", encoding="utf-8", newline="\n")
(c / "tools" / "chocolateyinstall.ps1").write_text(f"""$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageArgs = @{{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  url64bit      = '{base}/{zip_name}'
  checksum64    = '{sha}'
  checksumType64 = 'sha256'
}}
Install-ChocolateyZipPackage @packageArgs
$exe = Join-Path $toolsDir 'DesktopTools-win-x64\\DesktopTools.exe'
Install-ChocolateyShortcut -ShortcutFilePath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk') -TargetPath $exe
""", encoding="utf-8", newline="\n")
(c / "tools" / "chocolateyuninstall.ps1").write_text("""$ErrorActionPreference = 'Stop'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DesktopTools.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
""", encoding="utf-8", newline="\n")
print(f"Wrote packaging files for DesktopTools {version} (zip SHA-256 {sha}).")
