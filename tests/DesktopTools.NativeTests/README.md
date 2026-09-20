# Windows native checks

Run from the repository root in an interactive Windows desktop session:

```powershell
dotnet run --project tests/DesktopTools.NativeTests/DesktopTools.NativeTests.csproj
```

This executable checks real `RegisterHotKey` ownership, conflicts, swaps and rollback, monitor enumeration, coordinate conversions, profile file handling, foreground-handle validation, and backdrop fallback configuration. It uses Ctrl+Alt+Shift+F21 through F24 temporarily and releases them on exit. If one is already assigned by another application, initial registration fails with a clear message; the test does not take over that assignment.

The backdrop checks create hidden HWNDs. They verify API configuration and opaque fallback, not visible material quality. These checks do not establish cross-process input behavior, visual alignment at mixed DPI, screenshot composition quality, or Discord capture compatibility; those require the manual Windows checklist.
