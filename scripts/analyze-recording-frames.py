"""Decode every numbered fixture frame; requires PyAV, used only for diagnostics.

Run after --record-throughput-only. Install av into artifacts/media-analysis-deps.
PyAV decoder API: https://pyav.org/docs/stable/cookbook/basics.html
No user media is opened; input is the harness's generated directory marker.
"""
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "artifacts/media-analysis-deps"))
import av

directory = Path(sys.argv[1] if len(sys.argv) > 1 else (root / "artifacts/integration/throughput-directory.txt").read_text()).resolve()
assert directory.is_relative_to(root / "artifacts/integration")
results = []
for path in sorted(directory.glob("*.mp4")):
    if path.name.endswith(".partial.mp4"):
        print(f"SKIP unfinished recording: {path.name}")
        continue
    source = json.loads(path.with_suffix(".json").read_text())
    numbers, timestamps = [], []
    with av.open(str(path)) as container:
        stream = container.streams.video[0]
        metadata_rate = float(stream.average_rate)
        codec = stream.codec_context.name
        for frame in container.decode(video=0):
            assert (frame.width, frame.height) == (640, 360)
            # Sample well inside each cell so borders, cursor and H.264 ringing
            # cannot turn identical source IDs into apparent distinct frames.
            plane = frame.reformat(format="gray").planes[0]
            pixels = bytes(plane)
            cells = [pixels[180 * plane.line_size + 48 + 32 * i] for i in range(18)]
            assert cells[0] > 190 and cells[1] < 60, "Black/corrupt fixture frame"
            number = sum((cells[i + 2] > 120) << i for i in range(16))
            numbers.append(number)
            timestamps.append(float(frame.pts * frame.time_base))
    assert len(numbers) > 1
    assert all(b > a for a, b in zip(timestamps, timestamps[1:])), "Non-monotonic timestamps"
    regressions = [(i, a, b) for i, (a, b) in enumerate(zip(numbers, numbers[1:])) if b < a]
    elapsed = timestamps[-1] - timestamps[0]
    distinct_changes = sum(a != b for a, b in zip(numbers, numbers[1:]))
    results.append(dict(source, Codec=codec, MetadataFps=metadata_rate, DecodedFrames=len(numbers),
                        UniqueIds=len(set(numbers)), DuplicateFrames=len(numbers) - len(set(numbers)),
                        DecodedFps=(len(numbers) - 1) / elapsed, DistinctFps=distinct_changes / elapsed,
                        FirstId=numbers[0], LastId=numbers[-1], Regressions=regressions[:20], RegressionCount=len(regressions), PyAV=av.__version__))
output = root / "artifacts/recording-frame-analysis.json"
output.write_text(json.dumps(results, indent=2))
for result in results:
    print(f"{result['Name']}: decoded {result['DecodedFps']:.2f} FPS, distinct {result['DistinctFps']:.2f} FPS, "
          f"{result['DuplicateFrames']} duplicates / {result['DecodedFrames']} frames, {result['Duration']:.2f}s, "
          f"{result['RegressionCount']} regressing frame IDs")
print("All frames decoded; binary references valid and timestamps monotonic. Frame-ID regressions are recorded separately.")
