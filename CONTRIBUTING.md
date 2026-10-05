# Contributing to DesktopTools

Thanks for helping improve DesktopTools. Issues, documentation fixes, translations, focused bug fixes, and feature proposals are welcome. Maintainers review pull requests before merging; opening one does not grant direct write access or guarantee acceptance.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md) in project discussions and reviews.

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

Fork the repository (people with write access create the branch in this repository instead), create a branch, and open a pull request. [Branches, commits and pull requests](#branches-commits-and-pull-requests) below describe the conventions. Keep the change small enough to review and explain what behavior changes. Follow the boundaries in [AGENTS.md](AGENTS.md) and [architecture.md](docs/architecture.md). Preserve original media and app-managed notes/settings unless the feature explicitly changes their lifecycle. Add or update focused tests when changing state, persistence, coordinate conversion, output pixels, shortcuts, or native resource handling.

Update user-facing strings across supported languages when relevant; [localization.md](docs/localization.md) describes the workflow. Keep generated binaries, working exports, private screenshots, and local release bundles out of the pull request. Preserve third-party notices and licenses.

In the pull request, include the problem, the resulting behavior, validation results, and any remaining limitation. Distinguish automated checks from manual observation, and write **Not tested** for checks you could not run. A maintainer decides whether and when to merge or publish a release. Contributions are provided under the project's [MIT license](LICENSE).

## Branches, commits and pull requests

`main` is always releasable and changes only through pull requests that pass the **Windows build and checks** workflow. Nobody pushes to it directly and history is never rewritten.

**Branches.** Start from an up-to-date `main` and use one short-lived branch per topic, named `<type>/<short-description>`: `feat/`, `fix/`, `perf/`, `docs/`, `refactor/`, `test/` or `chore/` (for example `perf/ocr-reading-budget`). A GitHub-name prefix such as `vg2222/feat-text-view` is fine too. Branches are deleted after they are merged. If another change reaches `main` first, bring your branch up to date with `git fetch origin` and `git rebase origin/main`, then `git push --force-with-lease` (only ever on your own branch).

**Commits.** One logical change per commit, so each one builds and passes the checks. Write the subject as an instruction in sentence case without a final period, around 70 characters ("Read text from several readings of the picture"). The body says *why* the change is needed and what you measured; include numbers for performance and accuracy work. Do not commit generated files, local release bundles, private screenshots or credentials.

**Pull requests.** Keep one topic per pull request and fill in the template, including the validation you actually ran and **Not tested** for what you could not run. Add the label `bug` or `enhancement`, because release notes are grouped by label. Write `Fixes #123` to close an issue. Mark work in progress as a draft. The maintainer is requested for review automatically; address review comments with new commits and resolve each conversation before merging.

**Merging.** Normal pull requests use **Squash and merge**, which leaves one clear commit per pull request on `main`. A large pull request whose commits were already curated one per feature (for example a release branch) uses **Create a merge commit** so those commits stay visible. The branch is deleted automatically afterwards.

**Releases.** Only the maintainer publishes. A release needs the new version in `src/DesktopTools/DesktopTools.csproj`, release notes in `docs/releases/<version>.md`, and the `Build release` workflow; after it is published, `python scripts/update-packaging.py <version>` regenerates the winget, Scoop and Chocolatey files.

Performance and accuracy work has its own guardrails in [optimization.md](docs/optimization.md).

