# Desktop Automation Implementation Plan
Goal: build the agreed local visual automation builder in codex/desktop-automation.
Architecture: Core script/runtime/store, Native explicit input and window discovery, Extras builder/trigger service, small controller partial and dashboard entry.
Spec: docs/superpowers/specs/2026-10-02-desktop-automation-design.md
Constraints: existing WPF design/localization/settings, offline translation unchanged, separate feature branch, no publication.
- [ ] Core tests: nested loops/conditions, malformed blocks, action limits, cancellation, atomic storage and future versions.
- [ ] Core model, executor and protected storage.
- [ ] Native input, explicit recorder, visual action editor and triggers.
- [ ] Controller lifetime, dashboard/settings entry and all four catalogs.
- [ ] Core/native/UI checks with synthetic targets and actual window screenshot review.
- [ ] Commit stages, checkpoint and installer/portable testing bundle.
Review focus: cancellation releases modifiers; disabled triggers remain disabled after restart; future files survive; malformed control blocks fail before input; external focus changes cannot silently redirect keyboard actions.
