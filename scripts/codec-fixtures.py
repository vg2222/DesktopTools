"""Generate owned codec fixtures, or inspect their exported copies. Diagnostic only."""
from pathlib import Path
import json
import sys
import uuid
import hashlib

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "artifacts/media-analysis-deps"))
import av
import numpy as np

marker = root / "artifacts/integration/codec-directory.txt"
if "--inspect" in sys.argv:
    directory = Path(marker.read_text()).resolve()
    assert directory.is_relative_to(root / "artifacts/integration")
    results = []
    for path in directory.glob("*-export.mp4"):
        with av.open(str(path)) as video:
            frames = [frame.to_ndarray(format="rgb24")[40, 40].tolist() for frame in video.decode(video=0)]
        with av.open(str(path)) as audio:
            samples = [frame.to_ndarray().astype(float) for frame in audio.decode(audio=0)] if audio.streams.audio else []
        rms = float(np.sqrt(np.mean(np.concatenate(samples, axis=1) ** 2))) if samples else 0
        assert len(frames) > 20 and frames[0][0] > 140 and frames[0][1] < 100 and frames[-1][2] > 140
        assert rms > .001, "Exported audio is silent"
        results.append(dict(File=path.name, Frames=len(frames), AudioRms=rms, FirstPixel=frames[0], LastPixel=frames[-1]))
    (directory / "decoded-exports.json").write_text(json.dumps(results, indent=2))
    print(f"PASS {len(results)} exported clips decoded with expected red/blue frames and non-silent audio.")
    sys.exit()

directory = root / "artifacts/integration" / ("codecs-" + uuid.uuid4().hex)
directory.mkdir()
fixtures = []
for name, codec, pixfmt, audio_codec in [("h264.mp4", "libx264", "yuv420p", "aac"),
    ("h264.mov", "libx264", "yuv420p", "aac"), ("mjpeg.avi", "mjpeg", "yuvj420p", "pcm_s16le"),
    ("wmv.wmv", "wmv2", "yuv420p", "wmav2"), ("vp9.mkv", "libvpx-vp9", "yuv420p", "libopus"),
    ("hevc.mp4", "libx265", "yuv420p", "aac")]:
    path = directory / name
    with av.open(str(path), "w") as output:
        video = output.add_stream(codec, rate=30)
        video.width, video.height, video.pix_fmt = 320, 240, pixfmt
        if codec in ("libx264", "libx265"):
            video.options = {"preset": "ultrafast"}
        audio = output.add_stream(audio_codec, rate=48000)
        audio.layout = "mono"
        if audio_codec != "pcm_s16le": audio.bit_rate = 128000
        for i in range(60):
            pixels = np.zeros((240, 320, 3), dtype=np.uint8)
            pixels[:, :, 0 if i < 30 else 2] = 220
            frame = av.VideoFrame.from_ndarray(pixels, format="rgb24")
            for packet in video.encode(frame): output.mux(packet)
            times = (np.arange(1600) + i * 1600) / 48000
            tone = (.05 * np.sin(2 * np.pi * 440 * times)).astype(np.float32).reshape(1, -1)
            sound = av.AudioFrame.from_ndarray(tone, format="flt", layout="mono")
            sound.sample_rate, sound.pts = 48000, i * 1600
            for packet in audio.encode(sound): output.mux(packet)
        for stream in (video, audio):
            for packet in stream.encode(None): output.mux(packet)
    fixtures.append(dict(File=name, Codec=codec, Audio=audio_codec, Sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
(directory / "fixtures.json").write_text(json.dumps(fixtures, indent=2))
marker.write_text(str(directory))
print(f"Generated {len(fixtures)} two-second synthetic clips in {directory}")
