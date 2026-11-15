"""Build the 1200x630 link preview (website/images/social-preview.png) from the gallery home render; requires Pillow.

Run the integration harness with --github-gallery first so the render shows the current interface.
"""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / "src/DesktopTools/Assets/Fonts"
OUT = ROOT / "website/images/social-preview.png"
W, H, S = 1200, 630, 2
BG, ACCENT = (12, 13, 15), (194, 55, 143)


def font(weight, size): return ImageFont.truetype(str(FONTS / f"Inter-{weight}.otf"), size * S)


def rounded(image, radius):
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=radius, fill=255)
    out = image.convert("RGBA"); out.putalpha(ImageChops.multiply(out.getchannel("A"), mask)); return out


canvas = Image.new("RGB", (W * S, H * S), BG)
glow = Image.new("RGB", canvas.size, BG); pen = ImageDraw.Draw(glow)
pen.ellipse((560 * S, 40 * S, 1400 * S, 820 * S), fill=(96, 26, 72))
pen.ellipse((-260 * S, 330 * S, 380 * S, 900 * S), fill=(46, 14, 36))
canvas = Image.blend(canvas, glow.filter(ImageFilter.GaussianBlur(150 * S)), 1.0)

# The real home screen, cut off at the right and bottom edges.
with Image.open(ROOT / "docs/screenshots/gallery/01-home-dark.png") as source:
    shot = source.convert("RGB")
width = 700 * S
shot = rounded(shot.resize((width, round(shot.height * width / shot.width)), Image.Resampling.LANCZOS), 18 * S)
x, y = 560 * S, 112 * S
shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((x, y + 20 * S, x + shot.width, y + shot.height + 20 * S), radius=18 * S, fill=(0, 0, 0, 170))
canvas = Image.alpha_composite(canvas.convert("RGBA"), shadow.filter(ImageFilter.GaussianBlur(28 * S)))
canvas.alpha_composite(shot, (x, y))
ImageDraw.Draw(canvas).rounded_rectangle((x, y, x + shot.width - 1, y + shot.height - 1), radius=18 * S, outline=(245, 184, 222, 70), width=S)

draw = ImageDraw.Draw(canvas)
with Image.open(ROOT / "src/DesktopTools/Assets/Icons/AppIcon.png") as icon:
    canvas.alpha_composite(icon.convert("RGBA").resize((64 * S, 64 * S), Image.Resampling.LANCZOS), (72 * S, 160 * S))
draw.text((70 * S, 290 * S), "DesktopTools", font=font("Bold", 64), fill=(255, 255, 255), anchor="ls")
draw.text((72 * S, 342 * S), "Capture. Annotate. Present.", font=font("Medium", 27), fill=(214, 204, 216), anchor="ls")
draw.rounded_rectangle((72 * S, 376 * S, 176 * S, 382 * S), radius=3 * S, fill=ACCENT)
draw.text((72 * S, 430 * S), "Free and open source for Windows 11", font=font("Regular", 20), fill=(160, 152, 166), anchor="ls")

canvas.convert("RGB").resize((W, H), Image.Resampling.LANCZOS).save(OUT, optimize=True)
print(f"{OUT.relative_to(ROOT)}: {W}x{H}")
