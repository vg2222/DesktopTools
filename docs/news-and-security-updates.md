# Announcements and security updates

DesktopTools includes a **News** page and checks this repository's public `news/announcements.json` once an hour while running. The check is separate from automatic release checks and can be disabled on the News page. Background failures are silent; the bundled or last successfully fetched items remain available in the running app. No screenshots, notes or recordings are sent with a news check.

Ordinary announcements appear once as a DesktopTools notification and stay on the News page. Opening the page clears their unread indicator. Keep each item short and provide translations for all five supported languages. The feed is limited to 20 entries and 64 KiB. Links can point only inside `https://github.com/vg2222/DesktopTools`; DesktopTools displays the text literally and does not execute feed content.

## Publish an announcement

Edit `news/announcements.json` on the protected main branch. Give each new item a unique lowercase ID, a UTC publication time, `kind: "news"`, localized `title` and `message` objects, and an optional repository link. English text is required; the other four languages fall back to English if absent. An optional `expiresUtc` hides an ordinary announcement after that time. Review the JSON and its wording before merging because clients will read it without a new app release.

## Mark a security update as urgent

Use a `security` item with `minimumSafeVersion` and a link to the DesktopTools release or published repository security advisory. Example:

```json
{
  "id": "security-example-1-2-7",
  "kind": "security",
  "publishedUtc": "2026-10-01T12:00:00Z",
  "minimumSafeVersion": "1.2.7",
  "title": { "en": "Security update available" },
  "message": { "en": "Update to 1.2.7 to address the reported issue." },
  "url": "https://github.com/vg2222/DesktopTools/releases/tag/v1.2.7"
}
```

First prepare, test and publish the fixed stable release, including its installer and SHA-256 manifest. Then publish the security item. An affected app keeps an in-app warning visible. If the existing GitHub release check confirms a stable release at or above `minimumSafeVersion`, the notification becomes an urgent update offer; otherwise it offers details and a manual check. **The feed never downloads, installs, closes, or restarts DesktopTools.** Selecting Update still uses the normal confirmation and checksum-verified installer flow. Choosing **Remind me later** snoozes the urgent notification for about an hour in the current session; the in-app warning remains until a safe version is installed or the item is withdrawn.

Use [GitHub's repository security advisories](https://docs.github.com/en/code-security/concepts/vulnerability-reporting-and-management/repository-security-advisories) for coordinated disclosure. This in-app alert supplements the advisory and release notes; older DesktopTools versions without this feature will not receive it. The feed and release metadata share the repository's GitHub trust boundary, so the emergency marker is a presentation signal, not an independent signature or authority to force an installation.
