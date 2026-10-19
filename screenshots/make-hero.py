#!/usr/bin/env python3
"""Builds screenshots/social-preview.png (1280×640): the README's hero image and GitHub's social preview.
The app icon, name and main features on the left; the Settings window behind the desktop widget on the right; the
game overlay and taskbar bar along the bottom. Run from anywhere: python screenshots/make-hero.py. Needs Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
W, H = 1280, 640
FONTS = Path(r"C:\Windows\Fonts")
ORANGE = (255, 106, 43)

def font(name, size): return ImageFont.truetype(str(FONTS / name), size)

def rounded(img, radius):
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.width - 1, img.height - 1), radius=radius, fill=255)
    out = img.convert("RGBA"); out.putalpha(mask); return out

def shadowed(canvas, img, xy, radius=14, blur=22, alpha=150):
    sh = Image.new("RGBA", (img.width + blur * 4, img.height + blur * 4), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((blur * 2, blur * 2, blur * 2 + img.width, blur * 2 + img.height), radius=radius, fill=(0, 0, 0, alpha))
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(sh, (xy[0] - blur * 2, xy[1] - blur * 2 + 8))
    canvas.alpha_composite(img, xy)

# a dark graphite background with a faint warm glow, like the app's Graphite theme
bg = Image.new("RGBA", (W, H))
px = bg.load()
for y in range(H):
    for x in range(W):
        t = y / H; g = max(0.0, 1 - ((x - 1000) ** 2 + (y - 120) ** 2) ** 0.5 / 900)
        px[x, y] = (int(18 + 10 * t + 14 * g), int(17 + 6 * t + 6 * g), int(21 + 14 * t + 4 * g), 255)

# right: the Settings window, then the widget in front of it
win = Image.open(HERE / "v4.0" / "settings-stats.png").convert("RGBA")
win = win.crop((8, 0, win.width - 8, win.height - 8))  # PrintWindow's invisible resize border
win = rounded(win.resize((int(win.width * 0.52), int(win.height * 0.52)), Image.LANCZOS), 10)
shadowed(bg, win, (700, 60))
widget = Image.open(HERE / "v3.2" / "widget.png").convert("RGBA")
widget = widget.resize((int(widget.width * 0.56), int(widget.height * 0.56)), Image.LANCZOS)
shadowed(bg, widget, (W - widget.width - 52, 44), radius=12)

# bottom: the game overlay and the taskbar bar
for name, y, scale in (("overlay-text.png", 462, 0.74), ("bar-one-line.png", 560, 0.69)):
    strip = Image.open(HERE / "v3.2" / name).convert("RGBA")
    strip = rounded(strip.resize((int(strip.width * scale), int(strip.height * scale)), Image.LANCZOS), 8)
    shadowed(bg, strip, (64, y), radius=8, blur=14, alpha=120)

d = ImageDraw.Draw(bg)
icon = Image.open(HERE.parent / "icon.png").convert("RGBA").resize((104, 104), Image.LANCZOS)
bg.alpha_composite(icon, (62, 60))
d.text((186, 52), "Kinetik Stats", font=font("segoeuib.ttf", 66), fill=(242, 242, 245))
d.text((189, 136), "Taskbar system monitor and FPS overlay", font=font("segoeui.ttf", 26), fill=(196, 196, 206))
d.text((189, 174), "for Windows 11 and 10  ·  one of the Kinetik apps", font=font("segoeuil.ttf", 21), fill=(146, 146, 158))
f = font("segoeui.ttf", 24)
for i, line in enumerate(["CPU / GPU temperature, usage and power",
                          "FPS, 1% lows and frame-time graph",
                          "Screen time, timeline and crash reports",
                          "Desktop widget, overlay and live dashboard",
                          "Themes shared by every Kinetik app"]):
    y = 238 + i * 42
    d.ellipse((72, y + 9, 84, y + 21), fill=ORANGE)
    d.text((100, y), line, font=f, fill=(230, 230, 236))

bg.convert("RGB").save(HERE / "social-preview.png", optimize=True)
print("wrote", HERE / "social-preview.png")
