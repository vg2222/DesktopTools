# Guides, recording markers and notes implementation plan

> Historical plan. The later 1.2.7 scope removed note-to-window attachment; notes now open independently with an optional Always on top setting. See `docs/releases/1.2.7.md` for the current behavior.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create numbered screenshot guides, mark moments during recordings, and make attaching a note to a chosen window reliable and discoverable.

**Architecture:** Keep note attachment in the notes service and reuse the existing Windows window enumeration. Keep recording timestamps with the active recorder session and expose them after saving. Compose guide output from copies of selected images; never edit originals.

**Tech Stack:** .NET WPF, existing DesktopTools UI and native services.

**Spec:** User approval in this chat, 2026-09-29.

## Global Constraints

- Do not release or publish this work.
- Preserve notes, recordings and source images.
- Localize all new user-facing strings.

## Review Focus

- Missing or inaccessible target window must report a useful error without attaching to another window.
- A note must remain attached to the selected window, including after the notes collection changes selection.
- Marker times must account for pauses and never exceed the saved recording.
- Invalid or oversized guide images must fail safely without modifying originals.
- Export cancellation must not create a partial output file.

## Tasks

### Window notes

- [x] Add a focused test for attachment selection and error handling; verify it fails.
- [x] Add direct attach/new-for-window actions in notes and reliable selection, then verify.

### Recording markers

- [x] Add a focused marker timeline test; verify it fails.
- [x] Add a marker action to the recording HUD and a saved-session marker list with seeking, then verify.

### Screenshot guide

- [x] Add a focused composition test; verify it fails.
- [x] Add screenshot ordering, captions, numbered export and a discoverable app entry, then verify.

### Final checks

- [x] Build and run focused tests; inspect modified diff and report any untested UI behavior.

## Verification checkpoint

The integration project built with no warnings. Focused notes, recorder state, guide export and feature-settings checks passed on Windows. The guide builder and exported PNG were rendered and visually inspected. Manual checks for another user's desktop, window lifecycle and full installer remain in `docs/manual-testing.md` and were not run. No release or push was performed.
