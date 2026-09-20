"""Create rounded README copies; requires Pillow. Originals are never modified."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageSequence

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/readme"
OUT.mkdir(parents=True, exist_ok=True)


def rounded(image):
    image = image.convert("RGBA")
    scale = 4
    size = image.size
    mask = Image.new("L", (size[0] * scale, size[1] * scale))
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, mask.width - 1, mask.height - 1),
        radius=round(size[0] * 0.022 * scale), fill=255)
    mask = mask.resize(size, Image.Resampling.LANCZOS)
    image.putalpha(ImageChops.multiply(image.getchannel("A"), mask))
    return image


for source, name in (
    ("01-home-dark.png", "home.png"),
    ("05-screenshot-editor-dark.png", "editor.png"),
    ("09-screen-recorder-dark.png", "recorder.png"),
):
    with Image.open(ROOT / "docs/screenshots/gallery" / source) as image:
        rounded(image).save(OUT / name, optimize=True)

with Image.open(ROOT / "assets/desktop-tools-hero.gif") as source:
    frames, durations = [], []
    for frame in ImageSequence.Iterator(source):
        durations.append(frame.info.get("duration", 100))
        rgba = rounded(frame)
        # GIF has binary transparency. Reserve one palette entry for the corners.
        indexed = rgba.convert("RGB").quantize(colors=255, dither=Image.Dither.NONE)
        indexed.paste(255, mask=rgba.getchannel("A").point(lambda value: 255 if value < 128 else 0))
        indexed.info["transparency"] = 255
        frames.append(indexed)
    frames[0].save(OUT / "hero.gif", save_all=True, append_images=frames[1:],
                   duration=durations, loop=source.info.get("loop", 0),
                   transparency=255, disposal=1, optimize=True)
    with Image.open(OUT / "hero.gif") as result:
        actual = [frame.info.get("duration", 100) for frame in ImageSequence.Iterator(result)]
        if actual != durations or result.size != source.size:
            raise RuntimeError("GIF timing or dimensions changed")
    print(f"Hero: {len(frames)} frames, {sum(durations)} ms; dimensions and timing preserved.")
print("Created three rounded PNGs and one animated GIF in assets/readme.")
