# Local recording memory fix for 1.2.8

This change is based on DesktopTools tag `v1.2.8` (`7341dd123c08d10cf83104f9181549ab66c0fb2f`) and ScreenRecorderLib tag `v7.0.1` (`646383234187efd3854e6b1069612b80c3ab9981`). It shipped in 1.2.9 as a locally built backend; the default NuGet dependency is unchanged.

## Cause and change

`OutputManager::WriteFrameToVideo` previously created and registered a new full-size D3D11 texture for each submitted frame. References on the successful COM path were balanced, but sustained recording retained increasing committed memory on the tested NVIDIA driver. Restoring sink-writer throttling and enabling low-latency mode separately did not remove the hardware-path growth.

The patch in `patches/ScreenRecorderLib-7.0.1-bounded-frames.patch` uses an `IMFVideoSampleAllocatorEx` pool instead. It starts with four ARGB32 samples and permits at most sixteen. Media Foundation returns a sample only when its consumers have released it, preserving asynchronous encoder ownership. The copy uses the actual DXGI subresource. Samples keep their allocator-owned buffers.

If the pool is full, the recorder skips that input frame while retaining real timestamps and continuing audio. It never allocates an unlimited fallback or waits inside allocation. Two seconds of uninterrupted exhaustion produces an error rather than silently recording permanently frozen video. Finalization logs the exhaustion count when diagnostic logging is enabled. Source and destination dimensions, format and multisampling must match before copying. Reset and finalization release the pool after flushing or releasing its consumers.

At 2560x1440 the BGRA surfaces themselves occupy roughly 56–225 MiB; this bounds these input surfaces, not every allocation in the capture, audio, driver or encoder stack. In particular, the software H.264 encoder still uses considerably more memory than the hardware path.

Resolution, bitrate, quality presets, capture APIs, selected FPS, variable timestamps, audio preferences and UI remain unchanged. No working-set trimming, forced garbage collection or hidden hardware FPS cap is used.

## Build and test

Build the native backend with Visual Studio C++/CLI, ATL, the Windows SDK and .NET Framework 4.8 targeting tools:

```powershell
./scripts/build-recorder-backend.ps1
```

The script pins the upstream source, applies the patch without resetting an existing checkout, builds x64, and prints the DLL path and SHA-256. It does not replace the installed application. Alternative local toolchains can be selected with `-MSBuildPath`, `-PlatformToolset`, `-TargetFrameworkRootPath` and `-ImportLibraryDirectory`.

Pass the resulting absolute DLL path explicitly when building DesktopTools and its tests:

```powershell
dotnet build DesktopTools.sln -c Release -p:RecorderBackendPath=<absolute-path-to-ScreenRecorderLib.dll>
./scripts/test.ps1 -NoBuild
```

Without this override, the project still resolves the unchanged official NuGet dependency. The original package cache is never patched.

The integration harness adds `--record-memory-only`. It starts an isolated, full-screen generated GDI fixture so source painting is not counted in the recorder's process memory. Set `PROBE_OUT` to a new absolute directory, `PROBE_SECONDS` (default 330), `PROBE_FPS` (default 30), `PROBE_AUDIO` (`sys` or `none`), and `PROBE_HW` (`1` or `0`). `PROBE_MOTION=1` enables a scrolling detailed texture; `PROBE_TONE=1` plays a quiet generated 440 Hz loop for loopback validation. Microphone capture stays off. The harness uses the primary display, records H.264/MP4, samples private bytes/working set/handles, finalizes the file, and checks dimensions, duration, audio presence and sustained memory growth. `PROBE_MAX_PRIVATE_MIB` sets a safety limit.

Generated clips and measurements belong under ignored `artifacts/` or an external local working directory. Never commit captured desktop content. Decode the numbered frames to measure distinct throughput; an FPS label alone is insufficient.

Long memory runs evaluate the final minute (maximum 32 MiB growth), while shorter runs allow an initial 20-second warm-up and at most 128 MiB growth. A separate private-memory safety budget remains active throughout recording. The extended settling window distinguishes lazy driver/encoder allocations from growth after five minutes. `--record-memory-metrics-only` rechecks the saved raw samples and rejects incomplete or safety-aborted runs; it does not alter recordings or measurements.

## Verification environment and limits

Windows 11 Pro build 26300, NVIDIA GTX 1660 SUPER (6 GB), 16 GB system RAM, 2560x1440 display at 180 Hz. Managed builds used .NET SDK 10.0.400; native builds used MSVC 14.51 and Windows SDK 10.0.26100.0. The solution built with zero warnings/errors and the complete default test script passed. The upstream native build emits its existing signedness warning on a clean build and a debug-format override warning with this local toolchain.

The original hardware 144 FPS/system-audio path reached about 1.9 GiB private memory in 52 seconds in an isolated recording process. The pooled path remained around 440 MiB over 90 seconds, with about 142 unique decoded frames/s and no corrupt or regressing IDs. A six-minute 30 FPS/system-audio run remained around 440 MiB after warm-up, with about 29.9 unique frames/s and no corrupt IDs. Software and forced manual-NV12 conversion also reached a plateau in focused runs. These private-byte measurements must not be equated to the user's 8.4 GB Task Manager working-memory observation.

144 FPS is a capture target, not a guarantee on every source or workload. Throughput without audio was lower on this machine even before the patch; the high-rate tests must report decoded throughput per scenario. **Not tested:** actual War Thunder gameplay, another GPU vendor, 4K/multi-monitor/DPI combinations, real GPU device loss, remote viewer exclusion and recovery from a permanently hung driver. The existing sink-writer finalization wait is not changed by this patch.

The user subsequently confirmed that recording works normally and no longer consumes excessive RAM. This is additional manual confirmation; it does not replace the measured scenarios and limitations above.
