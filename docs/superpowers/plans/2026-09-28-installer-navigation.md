# Installer navigation and resizing implementation plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Steps use checkboxes for tracking.

**Goal:** Make the installer sidebar meaningful, expose advanced choices there, and allow the centered setup window to resize.

**Architecture:** Keep the existing installer modes and actions. Replace the inert sidebar tile and decorative feature list with a task summary and useful version/requirements information. Put hidden advanced controls behind a sidebar action; add a resize grip without changing the layered window that provides rounded transparency.

**Tech stack:** C# 13, WPF, .NET 10. The installer render harness is `tests/DesktopTools.InstallerTests`.

**Spec:** User request in the DesktopTools conversation, 2026-09-28.

## Global constraints

- Preserve install, update, repair, uninstall, data, and language-switching behavior.
- Build only a local candidate; do not push or publish.
- Use existing translated labels where possible and retain keyboard access.

## Review focus

- Advanced choices must be discoverable in update and older-setup modes and absent when irrelevant.
- Opening older-version options must not bypass the existing warning.
- Resizing must keep footer actions visible and center content scrollable.
- Language switching must preserve the selected uninstall data choice.
- Rounded corners must remain intact.

## Task 1: Sidebar behavior and layout

**Files:** `installer/InstallerWindow.Layout.cs`, `installer/InstallerWindow.cs`, `tests/DesktopTools.InstallerTests/Program.cs`.

- [x] Add render assertions for sidebar visibility, advanced opening, and removal of inert navigation.
- [x] Run the render test to confirm these assertions fail.
- [x] Implement a plain task summary, lower-left metadata, and sidebar Advanced options action.
- [x] Run the render test and inspect install, update, maintenance, older, and uninstall images.

## Task 2: Resizable shell and local candidate

**Files:** `installer/InstallerWindow.Layout.cs`, `tests/DesktopTools.InstallerTests/Program.cs`, local release checkpoint.

- [x] Add a resize-grip test for dimension changes and minimum bounds.
- [x] Run it red, add the grip, and run it green.
- [x] Run focused installer behavior and motion checks.
- [x] Commit locally, build and verify a local installer and portable candidate, then update the checkpoint.
