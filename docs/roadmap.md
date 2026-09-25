# Roadmap

DesktopTools 1.2.4 includes the drawing, capture, presentation, image, video, recording, OCR, translation, notes, file shelf, audio, profile, and localization features described in the README.

## Release readiness

The remaining release acceptance work is environmental rather than missing product scope:

- Observe rendering and pointer behavior on physical mixed-DPI 100%, 150%, and 200% displays, including monitor disconnects.
- Reproduce the profile-specific Chrome pointer-drag/topmost report on the affected setup.
- Validate real microphone and system-audio synchronization, additional GPU/encoder combinations, hour-long recording, and unique-frame throughput at the 144 FPS target.
- Observe capture exclusion, sharing-only blackout, controls, and ink from a separate Discord, Teams, or equivalent viewer.

These results will be recorded as compatibility evidence. Native API success alone does not establish what a remote viewer receives.

## Future candidates

- Wider hardware and screen-sharing compatibility coverage.
- Optional signing and automated provenance for published binaries.
- Additional OCR language guidance and translation pairs where suitable offline models and licenses are available.
- Improved multi-monitor annotation workflows and stylus support.
- Video editing beyond the current single-clip operations.

Remote desktop control, cloud accounts or sync, subscriptions, and cross-platform ports are outside the current scope.
