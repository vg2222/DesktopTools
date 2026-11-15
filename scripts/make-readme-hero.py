"""Build the animated README hero (assets/readme/hero.gif) from the published gallery screenshots.

Every picture in it is a render of the real app from docs/screenshots/gallery (run the integration harness with --github-gallery
first) or is drawn here; no desktop content is captured. Needs Pillow, numpy and ffmpeg on PATH.

Usage: python scripts/make-readme-hero.py [--frames-only DIR]
"""
import math, shutil, subprocess, sys, tempfile
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
GALLERY = ROOT / "docs/screenshots/gallery"
FONTS = ROOT / "src/DesktopTools/Assets/Fonts"
OUT = ROOT / "assets/readme/hero.gif"
W, H, S = 800, 400, 2                 # output size and supersampling factor
FPS, SECONDS = 15, 10.0
ACCENT = (194, 55, 143)

def font(weight, size): return ImageFont.truetype(str(FONTS / f"Inter-{weight}.otf"), int(size * S))
def ease(t): t = min(1.0, max(0.0, t)); return 1 - (1 - t) ** 3                      # ease-out cubic
def smooth(t): t = min(1.0, max(0.0, t)); return t * t * (3 - 2 * t)                  # ease-in-out
def span(t, start, end): return (t - start) / (end - start)
def lerp(a, b, t): return a + (b - a) * t

def rounded(image, radius):
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=radius, fill=255)
    out = image.convert("RGBA"); out.putalpha(mask); return out

def backdrop():
    """Dark canvas with two soft colour glows, drawn once."""
    base = Image.new("RGB", (W * S, H * S), (12, 13, 15))
    glow = Image.new("RGB", base.size, (12, 13, 15)); draw = ImageDraw.Draw(glow)
    draw.ellipse((-200 * S, -260 * S, 420 * S, 260 * S), fill=(90, 26, 69))
    draw.ellipse((520 * S, 180 * S, 1000 * S, 640 * S), fill=(52, 16, 42))
    return Image.blend(base, glow.filter(ImageFilter.GaussianBlur(120 * S)), 0.9).convert("RGBA")

def shot(name, width):
    """A gallery screenshot scaled to `width` output pixels, with rounded corners."""
    with Image.open(GALLERY / f"{name}.png") as source:
        image = source.convert("RGB")
    height = round(image.height * width * S / image.width)
    return rounded(image.resize((width * S, height), Image.Resampling.LANCZOS), 12 * S)

def shadowed(image, blur=22, alpha=150):
    pad = blur * 3 * S
    canvas = Image.new("RGBA", (image.width + pad * 2, image.height + pad * 2), (0, 0, 0, 0))
    shadow = Image.new("RGBA", image.size, (0, 0, 0, alpha)); shadow.putalpha(image.getchannel("A").point(lambda v: v * alpha // 255))
    canvas.alpha_composite(shadow, (pad, pad + 10 * S))
    canvas = canvas.filter(ImageFilter.GaussianBlur(blur * S / 2))
    canvas.alpha_composite(image, (pad, pad)); return canvas, pad

def place(frame, layer, x, y, opacity=1.0, scale=1.0):
    """Composite `layer` with its top-left at output coordinates (x, y)."""
    if opacity <= 0: return
    if scale != 1.0:
        layer = layer.resize((max(1, round(layer.width * scale)), max(1, round(layer.height * scale))), Image.Resampling.BILINEAR)
    if opacity < 1: layer = layer.copy(); layer.putalpha(layer.getchannel("A").point(lambda v: int(v * opacity)))
    frame.alpha_composite(layer, (round(x * S), round(y * S)))

def pill(text, dot=ACCENT):
    """Caption chip with a coloured dot."""
    face = font("SemiBold", 15); width = round(face.getlength(text)) + 52 * S; height = 34 * S
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0)); draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, width - 1, height - 1), radius=height // 2, fill=(22, 21, 25, 238), outline=(255, 255, 255, 46), width=S)
    draw.ellipse((14 * S, 12 * S, 24 * S, 22 * S), fill=dot + (255,))
    draw.text((34 * S, height // 2), text, font=face, fill=(244, 246, 250, 255), anchor="lm"); return image

class Hero:
    def __init__(self):
        self.back = backdrop()
        self.home, self.home_pad = shadowed(shot("01-home-dark", 470))
        self.editor, self.editor_pad = shadowed(shot("05-screenshot-editor-dark", 640))
        self.recorder, self.recorder_pad = shadowed(shot("09-screen-recorder-dark", 520))
        self.settings, self.settings_pad = shadowed(shot("21-appearance-dark", 520))
        with Image.open(ROOT / "src/DesktopTools/Assets/Icons/AppIcon.png") as icon:
            self.icon = icon.convert("RGBA").resize((56 * S, 56 * S), Image.Resampling.LANCZOS)
        # The picture that gets "captured" in the selection scene is the demo slide shown in the recorder's preview.
        with Image.open(GALLERY / "09-screen-recorder-dark.png") as source:
            crop = source.convert("RGB").crop((62, 474, 1354, 1200))
        self.slide = rounded(crop.resize((560 * S, round(crop.height * 560 * S / crop.width)), Image.Resampling.LANCZOS), 10 * S)
        self.captions = [pill("Capture a region, pixel-exact", (245, 184, 222)), pill("Annotate, extract text, hide private data", ACCENT),
                         pill("Record the screen without freezes", (239, 68, 68)), pill("Icons, motion and 12 languages", (226, 104, 180))]
        self.title, self.tag = font("Bold", 46), font("Medium", 17)

    def caption(self, frame, index, t, start, end):
        appear, leave = ease(span(t, start, start + .45)), smooth(span(t, end - .35, end))
        place(frame, self.captions[index], 32, 348 + (1 - appear) * 14, opacity=appear * (1 - leave))

    def render(self, t):
        frame = self.back.copy(); draw = ImageDraw.Draw(frame)
        # 0.0-2.4  title with the home window rising behind it
        if t < 2.8:
            leave = smooth(span(t, 2.3, 2.8)); enter = ease(span(t, .1, 1.0))
            place(frame, self.home, 318 - self.home_pad / S, 26 + (1 - enter) * 70 - leave * 30 - self.home_pad / S, opacity=enter * (1 - leave))
            text = ease(span(t, 0, .7)) * (1 - leave)
            if text > 0:
                layer = Image.new("RGBA", frame.size, (0, 0, 0, 0)); pen = ImageDraw.Draw(layer)
                layer.alpha_composite(self.icon, (36 * S, 112 * S))
                pen.text((36 * S, 190 * S), "DesktopTools", font=self.title, fill=(255, 255, 255, 255))
                pen.text((38 * S, 252 * S), "Capture, annotate, record", font=self.tag, fill=(196, 186, 198, 255))
                pen.text((38 * S, 276 * S), "and present on Windows 11", font=self.tag, fill=(196, 186, 198, 255))
                place(frame, layer, -(1 - ease(span(t, 0, .7))) * 18, 0, opacity=text)
        # 2.6-5.0  region selection over a still frame, with size badge and loupe
        if 2.5 <= t < 5.1:
            enter, leave = ease(span(t, 2.5, 3.0)), smooth(span(t, 4.7, 5.1))
            ox, oy = 120, 44
            place(frame, self.slide, ox, oy + (1 - enter) * 20, opacity=enter * (1 - leave))
            if enter > .98:
                grow = smooth(span(t, 3.05, 4.15))
                x0, y0 = ox + 24, oy + 96; x1, y1 = lerp(x0 + 20, ox + 222, grow), lerp(y0 + 12, oy + 196, grow)
                dim = Image.new("RGBA", frame.size, (0, 0, 0, 0)); pen = ImageDraw.Draw(dim)
                pen.rounded_rectangle((ox * S, oy * S, (ox + 560) * S, oy * S + self.slide.height), radius=10 * S, fill=(0, 0, 0, round(120 * (1 - leave))))
                pen.rectangle((x0 * S, y0 * S, x1 * S, y1 * S), fill=(0, 0, 0, 0))
                frame.alpha_composite(dim); alpha = round(255 * (1 - leave))
                draw.rectangle((x0 * S, y0 * S, x1 * S, y1 * S), outline=(255, 255, 255, alpha), width=S + 1)
                for cx, cy, dx, dy in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
                    draw.line((cx * S, cy * S, (cx + dx * 12) * S, cy * S), fill=(255, 255, 255, alpha), width=3 * S)
                    draw.line((cx * S, cy * S, cx * S, (cy + dy * 12) * S), fill=(255, 255, 255, alpha), width=3 * S)
                label = f"{round((x1 - x0) * 2.31)} × {round((y1 - y0) * 2.31)}"; face = font("SemiBold", 11)
                bw = round(face.getlength(label)) + 16 * S; bx = round((x0 + x1) / 2 * S - bw / 2); by = round((y0 - 30) * S)
                draw.rounded_rectangle((bx, by, bx + bw, by + 22 * S), radius=6 * S, fill=(22, 23, 27, round(232 * (1 - leave))))
                draw.text((bx + bw // 2, by + 11 * S), label, font=face, fill=(255, 255, 255, alpha), anchor="mm")
                # loupe: an enlarged, hard-edged copy of the pixels at the moving corner
                lx, ly, side = x1 + 20, y1 + 18, 84
                sx, sy = round((x1 - ox) * S), round((y1 - oy) * S)
                patch = self.slide.crop((sx - 9, sy - 9, sx + 9, sy + 9)).resize((side * S, side * S), Image.Resampling.NEAREST)
                loupe = Image.new("RGBA", (side * S, (side + 20) * S), (22, 23, 27, 236)); loupe.paste(patch.convert("RGB"), (0, 0))
                pen = ImageDraw.Draw(loupe); mid = side * S // 2
                pen.rectangle((mid - 5 * S, mid - 5 * S, mid + 5 * S, mid + 5 * S), outline=(255, 255, 255, 255), width=S)
                pen.text((mid, (side + 10) * S), f"{round((x1 - ox) * 2.31)}, {round((y1 - oy) * 2.31)}", font=font("SemiBold", 10), fill=(255, 255, 255, 255), anchor="mm")
                place(frame, rounded(loupe, 9 * S), lx, ly, opacity=(1 - leave) * ease(span(t, 3.3, 3.6)))
                # pointer
                px, py = x1 * S, y1 * S
                draw.polygon(((px, py), (px, py + 17 * S), (px + 5 * S, py + 13 * S), (px + 11 * S, py + 12 * S)), fill=(255, 255, 255, alpha), outline=(0, 0, 0, alpha))
            self.caption(frame, 0, t, 2.8, 5.0)
        # 4.9-7.2  the editor
        if 4.85 <= t < 7.3:
            enter, leave = ease(span(t, 4.85, 5.5)), smooth(span(t, 6.9, 7.3))
            zoom = lerp(.94, 1.0, enter)   # then it holds still: unchanged frames cost almost nothing in a GIF
            width = self.editor.width * zoom / S
            place(frame, self.editor, (W - width) / 2, 18 + (1 - enter) * 26 - leave * 20 - self.editor_pad / S * zoom + self.editor_pad / S * 0, opacity=enter * (1 - leave), scale=zoom)
            self.caption(frame, 1, t, 5.2, 7.2)
        # 7.1-8.6  the recorder with a pulsing REC capsule
        if 7.1 <= t < 8.75:
            enter, leave = ease(span(t, 7.1, 7.65)), smooth(span(t, 8.35, 8.75))
            place(frame, self.recorder, 60 - self.recorder_pad / S + (1 - enter) * -30, 12 - self.recorder_pad / S, opacity=enter * (1 - leave))
            capsule = Image.new("RGBA", (190 * S, 44 * S), (0, 0, 0, 0)); pen = ImageDraw.Draw(capsule)
            pen.rounded_rectangle((0, 0, 190 * S - 1, 44 * S - 1), radius=22 * S, fill=(22, 21, 25, 240), outline=(255, 255, 255, 50), width=S)
            pulse = .55 + .45 * math.sin((t - 7.1) * 2 * math.pi * 1.4)
            pen.ellipse((16 * S, 15 * S, 30 * S, 29 * S), fill=(239, 68, 68, round(255 * (.45 + .55 * pulse))))
            seconds = int((t - 7.1) * 3) + 12
            pen.text((42 * S, 22 * S), f"00:{seconds:02d}", font=font("Bold", 17), fill=(255, 255, 255, 255), anchor="lm")
            pen.rounded_rectangle((150 * S, 12 * S, 170 * S, 32 * S), radius=4 * S, fill=(222, 53, 65, 255))
            place(frame, capsule, 560, 300 + (1 - enter) * 18, opacity=enter * (1 - leave))
            self.caption(frame, 2, t, 7.35, 8.65)
        # 8.6-10  settings with icons, fading back to the opening frame for a clean loop
        if t >= 8.55:
            enter, leave = ease(span(t, 8.55, 9.05)), smooth(span(t, 9.6, 10.0))
            place(frame, self.settings, 220 - self.settings_pad / S + (1 - enter) * 30, 14 - self.settings_pad / S, opacity=enter * (1 - leave))
            self.caption(frame, 3, t, 8.75, 9.9)
        return frame

def main():
    hero = Hero(); count = round(FPS * SECONDS)
    frames_only = sys.argv[sys.argv.index("--frames-only") + 1] if "--frames-only" in sys.argv else None
    work = Path(frames_only) if frames_only else Path(tempfile.mkdtemp(prefix="desktoptools-hero-"))
    work.mkdir(parents=True, exist_ok=True)
    corner = Image.new("L", (W, H), 0); ImageDraw.Draw(corner).rounded_rectangle((0, 0, W - 1, H - 1), radius=round(W * 0.022), fill=255)
    try:
        for index in range(count):
            frame = hero.render(index / FPS).resize((W, H), Image.Resampling.LANCZOS)
            frame.putalpha(corner); frame.save(work / f"f{index:04d}.png")
        if frames_only: print(f"{count} frames in {work}"); return
        if not shutil.which("ffmpeg"): raise SystemExit("ffmpeg is required to assemble the GIF.")
        palette = work / "palette.png"
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-framerate", str(FPS), "-i", str(work / "f%04d.png"),
                        "-vf", "palettegen=max_colors=160:reserve_transparent=1:stats_mode=diff", str(palette)], check=True)
        OUT.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-framerate", str(FPS), "-i", str(work / "f%04d.png"), "-i", str(palette),
                        "-lavfi", "paletteuse=dither=none:alpha_threshold=128:diff_mode=rectangle", "-loop", "0", str(OUT)], check=True)
        with Image.open(OUT) as result:
            print(f"{OUT.relative_to(ROOT)}: {result.size[0]}x{result.size[1]}, {result.n_frames} frames, {OUT.stat().st_size / 1e6:.1f} MB")
    finally:
        if not frames_only: shutil.rmtree(work, ignore_errors=True)

if __name__ == "__main__": main()
