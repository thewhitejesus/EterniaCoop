"""Builds the Nexus/GitHub images from an in-game screenshot.
  python -I make_images.py <battle screenshot> <out dir>
Makes header.png (1920x480) and main.png (1280x720)."""
import sys, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
shot = Image.open(src).convert("RGB")
shot = shot.crop((0, 36, shot.width, shot.height))          # drop the Windows title bar
W, H = shot.size

FONTS = r"C:\Windows\Fonts"
def font(name, size): return ImageFont.truetype(os.path.join(FONTS, name), size)
TITLE, BOLD, REG = "seguibl.ttf", "segoeuib.ttf", "segoeui.ttf"
CHIPS = [("1P", (230, 70, 70)), ("2P", (70, 120, 245)), ("3P", (70, 190, 95)), ("4P", (240, 200, 60))]


def gradient(size, horizontal, start, end, reverse=False):
    """Black overlay mask: alpha from start to end along one axis."""
    w, h = size
    g = Image.new("L", (w, h))
    px = g.load()
    n = w if horizontal else h
    for i in range(n):
        t = i / (n - 1)
        if reverse: t = 1 - t
        a = int(start + (end - start) * t)
        if horizontal:
            for y in range(h): px[i, y] = a
        else:
            for x in range(w): px[x, i] = a
    return g


def shadowed(img, xy, text, f, fill, anchor="la", shadow=6):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).text((xy[0] + 3, xy[1] + 4), text, font=f, fill=(0, 0, 0, 200), anchor=anchor)
    layer = layer.filter(ImageFilter.GaussianBlur(shadow))
    img.alpha_composite(layer)
    ImageDraw.Draw(img).text(xy, text, font=f, fill=fill, anchor=anchor)


def chips(img, x, y, h, right_align=False):
    d = ImageDraw.Draw(img)
    f = font(BOLD, int(h * 0.62))
    w, gap = int(h * 1.55), int(h * 0.28)
    total = 4 * w + 3 * gap
    if right_align: x -= total
    for i, (label, col) in enumerate(CHIPS):
        x0 = x + i * (w + gap)
        d.rounded_rectangle((x0, y, x0 + w, y + h), radius=h // 4, fill=col + (255,))
        d.text((x0 + w / 2, y + h / 2 + 1), label, font=f, fill=(255, 255, 255, 255), anchor="mm")


# ---- header 1920x480: title panel on the left fading into the party (Meredy, Reid, Farah) on the right ----
scene_w = 1250                                               # right part of the banner
# y 265-865: from above the heads to just below the shadows (the HUD text starts lower); x keeps the 4.8:... ratio
top_y, bottom_y = 265, 865
crop_w = int((bottom_y - top_y) * scene_w / 480)
party = shot.crop((60, top_y, 60 + crop_w, bottom_y)).resize((scene_w, 480), Image.LANCZOS)
header = Image.new("RGBA", (1920, 480), (8, 10, 22, 255))
header.paste(party, (1920 - scene_w, 0))
fade = Image.new("L", (1920, 480), 0)
fp = fade.load()
for x in range(1920 - scene_w, 1920 - scene_w + 220):        # soft edge between panel and scene
    a = int(255 * (1 - (x - (1920 - scene_w)) / 220.0))
    for y in range(480): fp[x, y] = a
header = Image.composite(Image.new("RGBA", header.size, (8, 10, 22, 255)), header, fade)
shadowed(header, (70, 205), "ETERNIA CO-OP", font(TITLE, 104), (255, 255, 255, 255), anchor="ls")
shadowed(header, (74, 268), "Up to 4-player battles, like the PS1 original", font(BOLD, 36), (235, 225, 190, 255), anchor="ls", shadow=4)
chips(header, 74, 310, 54)
header.convert("RGB").save(os.path.join(out, "header.png"), optimize=True)

# ---- header_plain 1920x480: no text, for Nexus (it draws the mod name over the bottom-left of the header) ----
ph = int(W / 4)
plain = shot.crop((0, 865 - ph, W, 865)).resize((1920, 480), Image.LANCZOS).convert("RGBA")
dark = Image.new("RGBA", plain.size, (8, 10, 22, 255))
plain = Image.composite(dark, plain, gradient(plain.size, True, 170, 0))      # darker left, behind Nexus's title
plain = Image.composite(dark, plain, Image.eval(gradient(plain.size, False, 0, 150), lambda a: a))
plain.convert("RGB").save(os.path.join(out, "header_plain.png"), optimize=True)

# ---- main image 1280x720: 16:9 crop that stops below the characters' feet (no battle HUD), title in the sky ----
main = shot.crop((130, 0, 130 + int(865 * 16 / 9), 865)).resize((1280, 720), Image.LANCZOS).convert("RGBA")
top = gradient(main.size, False, 215, 0)
top = Image.eval(top, lambda a: 0 if a < 120 else int((a - 120) * 255 / 95))
main = Image.composite(Image.new("RGBA", main.size, (8, 10, 22, 255)), main, top)
shadowed(main, (56, 118), "ETERNIA CO-OP", font(TITLE, 88), (255, 255, 255, 255), anchor="ls")
shadowed(main, (60, 170), "Up to 4-player battles for Tales of Eternia Remastered", font(BOLD, 28), (240, 232, 200, 255), anchor="ls", shadow=3)
chips(main, 60, 196, 42)
main.convert("RGB").save(os.path.join(out, "main.png"), optimize=True)
print("wrote", os.path.join(out, "header.png"), "and", os.path.join(out, "main.png"))
