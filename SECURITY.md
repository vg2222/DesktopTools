# Security policy

## Supported versions

Security fixes target the latest published DesktopTools release. Older builds may need to update before a fix is available.

## Report a vulnerability

Do not open a public issue with exploit details or private user data. Use GitHub's private vulnerability reporting for this repository when available. Include the affected version, impact, reproduction steps, and suggested mitigation, using disposable test data.

If private reporting is unavailable, open a public issue requesting a private maintainer contact without including vulnerability details. Do not attach credentials, private screenshots, recordings, or personal files.

DesktopTools is a local desktop application without an account, telemetry, or cloud service. Capture exclusion and sharing-only blackout are compatibility features, not security boundaries. See [download verification](docs/security-checks.md) for release checksum guidance.

Optional model downloads happen only at the user's request, after acceptance of the publisher's licence. The app uses the publisher's URL at a pinned revision from its embedded catalog, checks redirects against allowed HTTPS hosts, and verifies each file's exact size and SHA-256 before installation. The download host can see the user's IP address; images, recordings and text are not uploaded. Models run on the processor on this PC. Update and news checks remain separate, configurable GitHub requests. See [model downloads and local storage](docs/ai-models.md).

When a fix is published, DesktopTools may show an [in-app security update notice](docs/news-and-security-updates.md). It does not install or restart automatically; users confirm the existing update flow. Publish a GitHub security advisory and release notes as well, because older app versions cannot receive in-app news.
