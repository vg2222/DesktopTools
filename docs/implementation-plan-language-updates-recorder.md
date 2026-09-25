# Language, updates, and recorder implementation plan

1. Add regression checks for Windows language detection, persisted language, automatic relaunch arguments, and quarter-hour update intervals.
2. Make new app settings use the Windows UI language, preserve explicit preferences, and relaunch after a saved language change while waiting for the old process to exit.
3. Add language choice to the first setup step and the installer. Localize installer controls using the app's existing catalogs, with installer-specific entries where needed.
4. Extend automatic update intervals to 15 and 30 minutes plus useful hourly choices. Keep stored hour values compatible with existing settings.
5. Reorganize Updates into a clear status panel and preferences section; move recorder transport controls to a fixed bottom bar and separate source/quality settings.
6. Run Windows build and relevant automated checks, inspect rendered UI, then review the diff and remaining limits.
