"""Create rounded README screenshots from the gallery; requires Pillow. Originals are never modified.

Run the integration harness with --github-gallery first so the gallery shows the current interface.
The animated hero has its own script, scripts/make-readme-hero.py.
"""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

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

print("Created three rounded PNGs in assets/readme. The animated hero is built by scripts/make-readme-hero.py.")
