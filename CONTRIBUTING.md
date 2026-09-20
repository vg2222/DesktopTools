# Contributing to DesktopTools

Thanks for helping improve DesktopTools. Issues, documentation fixes, translations, focused bug fixes, and feature proposals are welcome. Maintainers review pull requests before merging; opening one does not grant direct write access or guarantee acceptance.

## Before you start

Search [existing issues](https://github.com/vg2222/DesktopTools/issues). For a bug, use the bug-report form and include the version, Windows/display environment, steps, expected result, and actual result. For a feature, describe the user problem first. Discuss larger changes in an issue before investing substantial work. For a security issue, follow [SECURITY.md](SECURITY.md) instead of posting details publicly.

Do not upload private desktop content, credentials, personal documents, or unredacted logs. Use disposable test data and remove identifying information from screenshots.

## Build locally

Development targets Windows 11 x64 and the SDK pinned in [global.json](global.json). From the repository root in PowerShell:

```powershell
./scripts/fetch-translation-models.ps1
./scripts/build.ps1
./scripts/test.ps1
```

The model fetch verifies downloaded files against the manifest checksum. The default test script runs noninteractive checks. For a UI, capture, installer, or hardware-dependent change, run the applicable [manual Windows checks](docs/manual-testing.md) and describe your actual environment. Run only checks relevant to a documentation-only change.

## Make a focused change

Fork the repository, create a branch, and open a pull request. Keep the change small enough to review and explain what behavior changes. Follow the boundaries in [AGENTS.md](AGENTS.md) and [architecture.md](docs/architecture.md). Preserve original media and app-managed notes/settings unless the feature explicitly changes their lifecycle. Add or update focused tests when changing state, persistence, coordinate conversion, output pixels, shortcuts, or native resource handling.

Update user-facing strings across supported languages when relevant; [localization.md](docs/localization.md) describes the workflow. Keep generated binaries, working exports, private screenshots, and local release bundles out of the pull request. Preserve third-party notices and licenses.

In the pull request, include the problem, the resulting behavior, validation results, and any remaining limitation. Distinguish automated checks from manual observation, and write **Not tested** for checks you could not run. A maintainer decides whether and when to merge or publish a release. Contributions are provided under the project's [MIT license](LICENSE).
