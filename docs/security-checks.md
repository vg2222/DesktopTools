# Verify a download

Download DesktopTools from the project's [GitHub Releases](https://github.com/vg2222/DesktopTools/releases/latest).

## SHA-256 checksums

Each release includes `SHA256SUMS.txt`. Compare the entry for your downloaded file with its hash:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath ".\DesktopTools-1.2.3-win-x64-setup.exe"
```

Use the filename for the version you downloaded. Matching hashes verify that the file matches the release asset; they do not replace publisher signing.

Current builds are unsigned, so Windows SmartScreen may show a prompt.
