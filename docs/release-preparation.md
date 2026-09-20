# Release preparation

## Source and documentation

- Keep build output, local release bundles, private notes, credentials and test captures out of Git. Ignore rules do not remove already tracked files; inspect the staged inventory before committing.
- Keep all five READMEs aligned. Each uses the shared rounded hero and three screenshots from `assets/readme`. `scripts/prepare-readme-media.py` regenerates them using Python and Pillow; source media remain unchanged.
- Confirm the project version, `v<version>` tag and `docs/releases/<version>.md` agree. Record compatibility boundaries rather than presenting unperformed hardware checks as passed.
- After making the repository public, enable private vulnerability reporting in GitHub settings. Review branch protection and Actions permissions before accepting contributions.

## Final bundle

1. Build from the final reviewed source with `./scripts/build-release.ps1`. Do not reuse a bundle whose included documentation or source predates the release candidate.
2. Run `./scripts/verify-release.ps1` to check versioned filenames, metadata and SHA-256 hashes. This is an integrity check, not a claim that the app has passed hardware acceptance.
3. Verify installation, repair, update and uninstall in a disposable account or VM, including preservation of external media and the keep/delete data choices.
4. Compare the final downloadable files with `SHA256SUMS.txt`, and keep the release notes aligned with the published assets. See [security checks](security-checks.md).

## GitHub publication

GitHub Actions workflows are manual-only for now. Pushing a branch or version tag does not start a hosted build or publish a release. Build and verify the final bundle locally, push the matching `v<version>` tag, then create a stable GitHub Release manually with `docs/releases/<version>.md` as its description. A manually started **Build release** workflow on a version tag can publish a release, so select its ref deliberately if using it later.

The release includes the installer, portable ZIP, `SHA256SUMS.txt` and `release.json`. The updater needs the first three; see [update protocol](updates.md). Download links and the latest-release badge work once the first stable release exists. No pending scan may be presented as a successful result.

After publication, download and verify both assets. Check README media/language links, release notes and update discovery. Full live update validation requires a second, higher published version.
