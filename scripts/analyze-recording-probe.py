"""Decode every frame of a --record-probe recording and report lost, repeated and corrupt frames plus a sharpness score.

Usage: python scripts/analyze-recording-probe.py <probe-directory> [name]
Needs PyAV and numpy (pip install av numpy). Only the harness's generated clips are opened.
"""
import json, sys
from pathlib import Path
import numpy as np
SIN = np.array([int(round(127 * np.sin(2 * np.pi * i / 1024))) for i in range(1024)])
TW, TH = 560, 300

def texture(f):
    x = np.arange(TW)[None, :]; y = np.arange(TH)[:, None]
    s1 = SIN[(x * 5 + f * 3) & 1023]; s2 = SIN[(y * 4 - f * 2) & 1023]; s3 = SIN[((x + y) * 2 + f) & 1023]
    # integer halves round toward zero like the C# int division
    half = lambda v: np.trunc(v / 2).astype(int)
    return np.stack([128 + half(s1 + s3), 128 + half(s2 + s3), 128 + half(s1 + s2)], axis=-1).astype(np.float32)   # R, G, B
import av
from PIL import Image
from scipy.ndimage import gaussian_filter

directory = Path(sys.argv[1]).resolve()
names = [sys.argv[2]] if len(sys.argv) > 2 else sorted(p.stem for p in directory.glob("*.json") if not p.stem.endswith(".analysis"))
for name in names:
    meta = json.loads((directory / f"{name}.json").read_text())
    path = directory / meta["file"]
    ref = np.asarray(Image.open(directory / meta["reference"]).convert("L")).astype(np.float32)
    H, W = ref.shape
    region = (slice(int(H * .55) - 4, int(H * .55) + 14 * 22 + 30), slice(40, W - 40))   # static detail
    ids, pts, bad = [], [], 0
    psnrs = []
    bar_std, edge_w = [], []
    tex_psnr = []
    sizes = []
    with av.open(str(path)) as c:
        st = c.streams.video[0]
        codec = st.codec_context.name; profile = st.codec_context.profile; meta_fps = float(st.average_rate)
        bit_rate = c.bit_rate
        a_dur = float(c.streams.audio[0].duration * c.streams.audio[0].time_base) if c.streams.audio else None
        v_dur = float(st.duration * st.time_base) if st.duration else None
        for n, fr in enumerate(c.decode(video=0)):
            g = fr.reformat(format="gray").to_ndarray().astype(np.float32)
            cells = [g[85, 60 + 40 * i] for i in range(20)]
            ok = cells[0] > 170 and cells[1] < 85
            if not ok: bad += 1; continue
            ids.append(sum((cells[i + 2] > 128) << i for i in range(18)))
            pts.append(float(fr.pts * fr.time_base))
            if n % 10 == 0:
                # Luma-only score: regress the decoded Y plane on the exact source R,G,B (absorbs the colour matrix and range),
                # so what remains is real compression error, not 4:2:0 chroma loss or a BT.601/709 mismatch.
                ydec = g[40:40 + TH, int(W - 620):int(W - 620) + TW].reshape(-1)
                want = texture(int(ids[-1])).reshape(-1, 3)
                A = np.c_[want, np.ones(len(want))]
                coef, *_ = np.linalg.lstsq(A, ydec, rcond=None)
                mse = float(((A @ coef - ydec) ** 2).mean()); tex_psnr.append(99.0 if mse < 1e-6 else 10 * np.log10(255 * 255 / mse))
                rgb = fr.to_ndarray(format="rgb24").astype(np.float32)
                row = rgb[230]                       # middle of the moving orange bar (y = 160..300 in DIPs)
                mask = (row[:, 0] > 190) & (row[:, 1] < 110) & (row[:, 2] < 80)
                xs = np.where(mask)[0]
                if len(xs) > 40 and xs.max() < W - 640:
                    x0, x1 = xs.min(), xs.max()
                    inner = rgb[190:270, x0 + 10:x1 - 10, :]
                    bar_std.append(float(inner.std(axis=(0, 1)).mean()))   # solid colour: any spread is compression noise
                    prof = row[max(0, x0 - 12):x0 + 12, 0]; lo, hi = prof.min(), prof.max()
                    if hi - lo > 80:
                        a = np.where(prof >= lo + 0.1 * (hi - lo))[0][0]; b = np.where(prof >= lo + 0.9 * (hi - lo))[0][0]
                        edge_w.append(float(b - a))
            if n % 25 == 0:
                a = g[region] - gaussian_filter(g[region], 2); b = ref[region] - gaussian_filter(ref[region], 2)
                psnrs.append(float((a * b).sum() / (np.sqrt((a * a).sum() * (b * b).sum()) + 1e-9)))
    ids = np.array(ids); pts = np.array(pts)
    if meta.get("exclusion"):
        ex = meta["exclusion"]; cx = int(ex["y"] + ex["h"] / 2); hits_hidden = hits_shown = total = 0
        with av.open(str(path)) as c2:
            for k, fr in enumerate(c2.decode(video=0)):
                if k % 20: continue
                rgb = fr.to_ndarray(format="rgb24").astype(int); total += 1
                px = rgb[cx, int(ex["hiddenX"] + ex["w"] / 2)]; hits_hidden += bool(px[0] > 200 and px[1] < 80 and px[2] > 200)
                px = rgb[cx, int(ex["shownX"] + ex["w"] / 2)]; hits_shown += bool(px[0] < 80 and px[1] > 200 and px[2] > 200)
        print(f"   capture exclusion: excluded magenta window visible in {hits_hidden}/{total} sampled frames (want 0); ordinary cyan window visible in {hits_shown}/{total} (want all)")
    dur = pts[-1] - pts[0]
    uniq = len(set(ids.tolist()))
    # Source frame ids are consecutive per composition frame; a jump of N means N-1 source frames never reached the file.
    steps = np.diff(ids)
    forward = steps[steps > 0]
    lost = int((forward - 1).sum())
    dupes = int((steps == 0).sum())
    gaps = np.diff(pts)
    print(f"== {name}")
    print(f"   {codec} {profile}, {W}x{H}, container {meta_fps:.2f} FPS, bitrate {bit_rate/1e6:.1f} Mbit/s, {len(ids)} frames decoded, {bad} unreadable/corrupt")
    if a_dur is not None: print(f"   audio track present: {a_dur:.2f}s vs video {v_dur:.2f}s (length difference {abs(a_dur - v_dur)*1000:.0f} ms)")
    print(f"   file duration {dur:.1f}s -> {len(ids)/dur:.1f} FPS delivered; distinct ids {uniq} ({uniq/dur:.1f}/s); source painted {meta['sourceFps']:.1f} FPS")
    print(f"   repeated frames {dupes}; source frames missing between delivered frames {lost} ({100*lost/max(1,lost+uniq):.1f}% of source)")
    print(f"   pts gap median {np.median(gaps)*1000:.1f} ms, p99 {np.percentile(gaps,99)*1000:.1f} ms, max {gaps.max()*1000:.0f} ms; gaps > 50 ms: {int((gaps>0.05).sum())}")
    print(f"   fine-detail match median {np.median(psnrs):.3f} (1.0 = pixel-identical text and grid; below ~0.8 is visibly smeared)")
    print(f"   moving interference texture: PSNR median {np.median(tex_psnr):.1f} dB, worst {np.min(tex_psnr):.1f} dB (>38 excellent, 32-38 good, <30 visibly blocky)")
    print(f"   moving orange bar: interior noise {np.median(bar_std):.2f} (0 = clean), edge rise {np.median(edge_w):.1f} px (1-2 = sharp, >4 = smeared)")
    (directory / f"{name}.analysis.json").write_text(json.dumps(dict(name=name, frames=len(ids), corrupt=bad, deliveredFps=len(ids)/dur, distinct=uniq, repeated=dupes, lost=lost, ptsMaxGapMs=float(gaps.max()*1000), detail=float(np.median(psnrs)), texturePsnr=float(np.median(tex_psnr)), bitrateMbit=bit_rate/1e6, codec=codec, profile=profile)))
