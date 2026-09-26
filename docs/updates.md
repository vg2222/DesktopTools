# Updates and installer maintenance

DesktopTools checks the latest stable release in [vg2222/DesktopTools](https://github.com/vg2222/DesktopTools/releases). Checks run shortly after startup and every hour by default. **Settings → Updates** offers startup-only, 15-minute, 30-minute, hourly, 2-hour, 3-hour, 6-hour, 12-hour, daily and weekly intervals, or disables background checks. Manual checking is also available in About. Drafts and prereleases are ignored; an unpublished repository or missing public release is reported without blocking the app.

An available update appears in a DesktopTools notification and stays accessible from the sidebar and Updates settings after the notification is dismissed. **Release notes** opens that release on GitHub, keeps the notification open, updates its message and extends its duration. Notifications wait for mouse or keyboard activity after appearing before their dismissal timer starts; hovering pauses the timer.

Clicking the update notification or its update action shows the restart and unsaved-work confirmation inside the notification. Confirming downloads the verified setup without opening a separate app dialog. Failed automatic checks remain quiet and leave the previous update status unchanged; a failed manual check reports the error.

**Update** in the notification first asks you to confirm that the app will close and restart, losing unsaved edits and current drawings. Saved notes and settings are retained. The installer downloads with notification progress; its expected size and SHA-256 from the same release are checked before launch. Installed copies pass control to the installer only after it validates the running app, then a compact progress notification remains during file replacement. Active recordings are finalized before restart. Portable copies open the regular installer instead of silently replacing their portable directory.

Each verified installer download uses a separate staging directory so a setup still running from an earlier attempt cannot block a retry. Portable copies also wait for their app process to exit before the installer UI opens.
Before replacing an installed copy, setup switches its working directory away from the old installation folder. This prevents setup itself from holding that folder open after DesktopTools exits.

If download or handoff fails, DesktopTools remains running and shows an error notification with a sound and taskbar flash. If installation fails, setup attempts to restore the previous installation and relaunches it minimized with the error. If relaunch is impossible, setup displays the error itself. Update files are under `%LocalAppData%\DesktopTools\Updates`. These checks use GitHub's public HTTPS API and downloads; no token, account or telemetry is required. Release checks disclose the normal network request information to GitHub, but no notes, settings, media or document contents are uploaded.

## Setup choices

- No installed copy: Install and a destination picker.
- Matching version: a vertical list of **Update**, **Repair**, and **Uninstall**, each with an icon and explanation. Update checks GitHub or downloads an available newer setup.
- Newer setup: Update is the primary action; additional maintenance choices are under More options.
- Older setup: download a newer setup or uninstall; **Advanced options** offers an explicit older-version install after a warning. Notes and settings are kept, but newer settings may not work in the older app.

Every setup checks for a newer GitHub release. Failure to connect does not block a valid local install, update or repair.

Setup checks the Microsoft Visual C++ x64 runtime required by screen recording, offline translation and image background removal. If it is missing, **Open Microsoft download** opens Microsoft's [official download page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Choose the x64 package and run Microsoft's installer, which presents its own license terms and may ask for administrator permission. Return to setup and choose **Check again**; returning to the window also refreshes the check. DesktopTools can be installed without it, but those actions remain unavailable until the runtime is installed. Screen OCR and ordinary image editing remain usable. DesktopTools neither bundles nor silently installs the Visual C++ Redistributable.

Uninstall defaults to **Keep notes and settings**. **Delete all DesktopTools-managed data** additionally removes the app's settings, notes, profiles, caches and downloaded updates after a confirmation. Screenshots, recordings and other files outside DesktopTools-managed directories are preserved. Linked folders are not followed for recursive data removal.

## Publishing releases that the updater recognizes

1. Set the app version in `src/DesktopTools/DesktopTools.csproj` to `major.minor.patch`. Use a higher version for each published update; never replace a released version with different application code.
2. Write `docs/releases/<version>.md`. Run the focused checks, then `scripts/build-release.ps1`.
3. Build and verify the release locally. GitHub Actions workflows are manual-only; pushing a branch or tag does not start a hosted build. An optional manual **Build release** workflow run from the default branch produces downloadable build artifacts without publishing. Running it from a version tag can publish a release, so choose the ref deliberately.
4. Push the matching `v<version>` tag and publish a stable GitHub Release with the versioned release notes and all assets below. A tag alone, draft release or prerelease will not reach the stable updater.

Required assets:

```text
DesktopTools-<version>-win-x64-setup.exe
DesktopTools-<version>-win-x64-portable.zip
SHA256SUMS.txt
```

The checksum manifest must contain exactly one SHA-256 entry for the installer filename. The app and setup accept only DesktopTools release asset URLs and trusted GitHub HTTPS redirect destinations. Downloads use temporary files and promote them only after successful verification. Current builds are unsigned; a SHA-256 check verifies download integrity, not an independently signed publisher identity.

The client follows GitHub's [latest-release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release). Fixture tests cover the protocol without executing downloaded code. A complete live update through GitHub requires two published versions and remains a release acceptance check.
