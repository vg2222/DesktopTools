# Verification

DesktopTools is verified through automated checks, isolated Windows integration scenarios and manual compatibility checks. Passing on one machine does not establish support for every display, audio device or screen-sharing application.

## Automated checks

Use Windows 11 x64 and the SDK pinned in [global.json](../global.json):

```powershell
./scripts/fetch-translation-models.ps1
./scripts/build.ps1
./scripts/test.ps1
```

The automated harnesses cover core state/history, image transforms and original-file protection, settings and profiles, native helpers, updater protocol and notification lifetime, and installer maintenance/rollback logic.

## Interactive checks

```powershell
./scripts/test.ps1 -Interactive
./scripts/test.ps1 -Performance
```

Interactive scenarios require an unlocked Windows desktop. They may open test windows, move the pointer, use the clipboard and exercise capture/input against fixtures. Run them when the desktop is available for testing. Rendered UI and detailed results are written under the ignored `artifacts` directory.

For a focused change, run the relevant scenario and report its actual outcome. Distinguish automated results, local visible behavior and output observed by a remote viewer. Do not describe unperformed checks as passed.

## Release acceptance

Before publication, verify the final installer and portable archive against their SHA-256 manifest and review the source/release version match. Test installation, repair, update and both uninstall choices in a disposable account or VM. A complete updater acceptance check needs two published stable versions.

Physical mixed-DPI displays, monitor disconnection, real microphone/loopback synchronization, sustained recording, device switching and remote screen-sharing behavior need suitable hardware or a separate viewer. These checks are not replaced by synthetic fixtures or successful Windows capture-affinity calls.

See [manual testing](manual-testing.md), [compatibility](compatibility.md), [release preparation](release-preparation.md) and [download verification](security-checks.md).
