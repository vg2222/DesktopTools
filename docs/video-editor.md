# Single-clip video editor

Open **Video editor** from Home or Utilities. Choose **Open video** in the toolbar or the central import area, then drag the blue timeline boundaries to trim it. Enable removal of a middle section and drag its red boundaries; Escape cancels a drag. Click the timeline to seek. The crop expander shows handles over the original frame; numeric fields remain available for precise values. Choose rotation or mute in the inspector. Output size presets and the 10–100% slider reduce the exported resolution independently of crop; the resulting pixel dimensions appear below. After a short pause in editing, the app renders a temporary edited MP4 automatically. **Update preview** starts that render immediately. Press Play to inspect it; playback never starts automatically. Editing another value switches back to the original and replaces any outdated render. **Export MP4** writes a separate file. Reset restores the full original range and frame.

This is one source clip with at most one removed section, not a multitrack timeline. No recording, transitions, background removal or audio mixing is included. The original is protected even when another path is a hard link to it. Existing destinations are replaced only after successful encoding. Cancel and window close request cancellation; media and temporary preview files are released. The UI never starts playback automatically. Feature disable checks unsaved edits before closing the editor.

## Windows backend and limits

The editor uses the shared native glass frame with a solid fallback when transparency is disabled or unavailable. The video stage stays opaque so glass does not alter the displayed frame.

The implementation uses Windows.Media.Editing and Windows.Media.Transcoding, with no external executable, codec package, network upload or automatic download. MP4/H.264 with AAC is the tested reference input. Other containers are offered by the picker because Windows may support them, but codec availability varies, particularly HEVC/AV1 and Windows N installations. Playback and encoding use different Windows components; an encoding-capable file may fail preview playback, and vice versa. The app reports failures.

Output is H.264 MP4, with AAC when the selected source track has audio and mute is off. Mute removes the audio stream. Output dimensions are normalized to even numbers by at most one pixel. Validation caps source duration at six hours and each frame dimension at 8192 pixels; these are input bounds, not a promise that every Windows encoder supports that size. Encoding is lossy, so an output may be larger or less detailed than the original.

Trim/cut use precise MediaComposition rendering. Crop/rotation require a second MediaTranscoder pass because adding the built-in transform directly to MediaClip failed native stream-type negotiation during testing. The second pass adds processing time, temporary disk usage and another lossy encode. Progress spans both passes. Full-length previews also encode the selected result, so long clips may take time and require free temporary disk space. Software transformation is used for consistent native behavior.

Crop offers Free, Original, 1:1, 4:3, 16:9 and 9:16 presets. A preset centers the crop and keeps its aspect ratio during dragging; exact width/height input switches back to Free. Reset restores the full original frame.

## Verification

After reading a video's basic information, the editor becomes usable while its timeline thumbnails load asynchronously. Loading another source or closing the editor cancels the old thumbnail work; late results cannot replace the current video's timeline. Export rechecks that the editor and source are unchanged after the save dialog closes.

The ten decoded timeline thumbnails fit within128×96 pixels each, including portrait and unusually narrow clips. Timeline cells crop around the center while preserving proportions. Explicit bounded dimensions are calculated before requesting each [Windows thumbnail](https://learn.microsoft.com/en-us/uwp/api/windows.media.editing.mediacomposition.getthumbnailasync); leaving height automatic with a fixed width could allocate very tall images.

Drag the playhead or the timeline body to scrub without changing the trim boundaries; Escape restores the position before that gesture. Playback requests are coalesced to the latest position at roughly30 updates per second, with the final position applied on release. Pending requests are discarded when switching playback source, editing, hiding, minimizing, exporting or closing. A seek while playback is still opening waits for it to become ready.

Setup and a guide are offered on first entry and can be repeated from Help or Settings → Tools.

Run the integration harness with `--video-only` on Windows. It generates its own color/pattern videos and PCM tone; it does not play audio. Checks cover source metadata, precise cut duration, kept segment colors, cropped clockwise pixel orientation, output dimensions, audio preservation/removal, original hash/hard-link protection, cancellation before/during encoding, destination replacement, invalid input and scratch cleanup.

`--video-ui` constructs the editor in all five languages and both themes, creates an edited preview, checks its duration, changes an edit to invalidate it, and checks disable/close cleanup. Renders are under `artifacts/integration/video-ui-*.png`. Test results do not establish universal codec support. Long clips, HDR/color metadata, phone rotation metadata, variable frame rate and subjective audio synchronization need broader real-world compatibility testing before a public release.

References: [Microsoft media composition guide](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/media-compositions-and-editing), [video transform API](https://learn.microsoft.com/en-us/uwp/api/windows.media.effects.videotransformeffectdefinition?view=winrt-26100).
