# Package-manager files

`packaging/` holds ready-to-publish files for winget, Scoop and Chocolatey. They install the **portable ZIP**: the graphical installer has no silent mode, which winget and Chocolatey require. The app is unsigned, so Windows SmartScreen may ask for confirmation on first start.

Regenerate them for a published release (hashes are read from that release's `SHA256SUMS.txt`):

```powershell
python scripts/update-packaging.py 1.2.7
winget validate --manifest packaging/winget/manifests/v/vg2222/DesktopTools/1.2.7
```

Validated with `winget validate`. **Not tested:** an actual `winget install`, `scoop install` or `choco install` run, and acceptance by the winget, Scoop or Chocolatey moderation teams.

## Publishing (needs the owner's accounts)

- **winget:** fork `microsoft/winget-pkgs`, copy `packaging/winget/manifests/v/vg2222/DesktopTools/<version>` to the same path, open a pull request. `wingetcreate update vg2222.DesktopTools --version <version> --urls <zip url> --submit` does this for later releases.
- **Scoop:** create a bucket repository (for example `vg2222/scoop-desktoptools`), add `packaging/scoop/desktoptools.json` as `bucket/desktoptools.json`, then `scoop bucket add desktoptools https://github.com/vg2222/scoop-desktoptools` and `scoop install desktoptools`. `autoupdate` keeps it current with Scoop's checkver tooling.
- **Chocolatey:** `choco pack packaging/chocolatey/desktoptools.nuspec`, then `choco push` with an API key from a chocolatey.org account. New packages are reviewed by moderators.

Nothing here is submitted automatically.

## ARM64

`./scripts/publish.ps1 -Runtime win-arm64` creates `artifacts/DesktopTools-win-arm64.zip` from the same sources with the ARM64 .NET runtime, ARM64 ONNX Runtime and SkiaSharp natives and the ARM64 build of ScreenRecorderLib. The executable and every native DLL were checked to carry the ARM64 machine type, but the build has **not been run on ARM hardware**. The graphical installer and the update check still use the x64 build, which Windows 11 ARM runs through emulation; the winget, Scoop and Chocolatey files above also point to the x64 ZIP. Adding an `arm64` architecture entry to them needs a released ARM64 ZIP and its SHA-256, so it is left until one is published and verified.
