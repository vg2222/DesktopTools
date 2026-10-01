# Desktop Automation
User-approved scope: visual manual actions and explicit recording; button/hotkey/interval/window-appearance triggers; finite nested repeats and window/pixel conditions.
Core owns typed scripts, validation, bounded asynchronous execution and versioned atomic storage. Native owns SendInput, physical pointer coordinates and visible external window discovery. Extras owns builder and dispatcher service.
Actions: Click, Double click, Right click, Scroll, Drag, Keys, Text, Wait, Launch, Focus window, Repeat/End repeat, If window/If pixel/Else/End if.
Stored text and identifiers are never translated. English, Russian, German, French and Spanish UI catalogs use existing L.T/L.F.
Recording only occurs during an explicit session, polling key transitions and mouse buttons; it records key gestures, clicks, drags, wheel input in the builder and pauses. No keylogger, global input hook, telemetry or cloud service. Polling cannot capture all rapid input or wheel activity outside the builder; explain this limitation.
Schedules are intervals while DesktopTools runs; no persistent Windows tasks. Window appearance is an absent-to-present edge, never a trigger merely from starting DesktopTools.
Bound all scripts (100 scripts/500 steps, 16 nesting, 1000 repetitions, 10000 executed actions, 10 minutes). Cancel on Esc, stop, close and quit. Acquire Esc before sending input; reject runs while other tools are busy.
Atomic script storage preserves future and damaged documents and existing notes/settings.
