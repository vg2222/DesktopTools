# Audio controls

Choose an output device at the top of the mixer. The master slider and application columns then follow that device. Each application has its own volume and mute control; microphone volume is below the columns. Opening or refreshing the mixer does not change audio settings.

Device lists and volume values refresh every three seconds while the window is visible. Refresh waits while dragging a slider or choosing a device. If Windows rejects a device change, the selector returns to the actual default and shows an error. The settings icon opens Windows sound settings.

If no default output is connected, the application list is empty. Attempts to adjust an old control still fail and restore its displayed value; an unplugged device is never treated as a successful volume change.

Application controls remain tied to the output device they displayed. If the default changes before the next refresh, a stale control is rejected rather than changing the same application's session on another output. Failed volume edits return to the last confirmed value; refresh reconciles the actual device state. Unchanged mute controls are reused between refreshes.

Output selection sets the console and multimedia defaults. It leaves the separate communications default unchanged. Applications that explicitly select their own device may continue using that device.

## Implementation and compatibility

Enumeration uses Microsoft's [active audio endpoint API](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-enumaudioendpoints). Selection uses the private Windows policy COM interface. Its availability is checked without changing a device; unsupported systems retain the Windows settings fallback. The ABI identifiers and method order were cross-checked against the [policy interface](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs) and [client declaration](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/PolicyConfigClient.cs). This is not a public Windows SDK compatibility guarantee.

The implementation releases enumeration, endpoint and policy references after each operation. A failure after changing the first role attempts to restore that role if it still matches our selection. Automated checks use simulated switching; real device enumeration and capability detection are read-only. Actual audible switching remains a manual check.
