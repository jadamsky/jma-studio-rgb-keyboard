"""Generates gui/app_icon.png + gui/app_icon.ico -- the real window/
taskbar icon for the app (distinct from gui/logo.png, the "JMA"
wordmark used for the tray icon and in-app header). Not part of the
running app; a one-off build tool, re-run it by hand if the design
ever needs to change. See gui.py for how the .ico actually gets
applied to the native window (pywebview's own `icon=` param doesn't
work on Windows, so this is set directly via a WinAPI call instead).

Run with:  python gui/make_app_icon.py
"""

import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

SIZE = 1024
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_PNG = os.path.join(OUT_DIR, "app_icon.png")
OUT_ICO = os.path.join(OUT_DIR, "app_icon.ico")

BG = (11, 11, 18, 255)  # #0b0b12, matches --bg in style.css
# Same blue -> purple -> pink accent gradient used throughout
# gui/style.css -- deliberately not the wordmark's red/green/blue,
# since this icon represents the whole app (taskbar/Alt-Tab), not just
# a small tray glyph.
ACCENT_STOPS = [(0.0, (79, 123, 255)), (0.55, (181, 68, 232)), (1.0, (255, 63, 164))]
GLOW_BLUE = (90, 160, 255)

FONT_PATH = r"C:\Windows\Fonts\segoeuib.ttf"


def _gradient_strip(size, stops, vertical=False):
    grad = Image.new("RGB", (size, 1) if not vertical else (1, size))
    for x in range(size):
        t = x / (size - 1)
        for i in range(len(stops) - 1):
            t0, c0 = stops[i]
            t1, c1 = stops[i + 1]
            if t0 <= t <= t1:
                f = (t - t0) / (t1 - t0)
                c = tuple(int(c0[j] + (c1[j] - c0[j]) * f) for j in range(3))
                if vertical:
                    grad.putpixel((0, x), c)
                else:
                    grad.putpixel((x, 0), c)
                break
    return grad.resize((size, size))


def _rounded_square_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def make_icon():
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    bg_mask = _rounded_square_mask(SIZE, int(SIZE * 0.22))
    canvas.paste(Image.new("RGBA", (SIZE, SIZE), BG), mask=bg_mask)

    font = ImageFont.truetype(FONT_PATH, int(SIZE * 0.62))
    text = "J"
    tmp = Image.new("L", (SIZE, SIZE))
    bbox = ImageDraw.Draw(tmp).textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pos = ((SIZE - tw) // 2 - bbox[0], (SIZE - th) // 2 - bbox[1] - int(SIZE * 0.02))

    text_mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(text_mask).text(pos, text, font=font, fill=255)

    # Soft blue glow behind the glyph. A plain low-alpha Gaussian blur
    # fades to nothing once downscaled to real icon sizes (32-48px);
    # a hard dilated ring survives but looks like a solid border, not
    # light. This is the middle ground, tuned interactively against
    # actual 32/48px renders, not just the 1024px master: blur, then
    # multiply-and-clip the alpha so most of the blurred area saturates
    # while the outer rim keeps a soft taper, then a second small blur
    # to soften the clip's own edge so it doesn't look like a shelf.
    glow_mask = text_mask.filter(ImageFilter.GaussianBlur(SIZE * 0.06))
    glow_mask = glow_mask.point(lambda a: min(255, int(a * 2.2)))
    glow_mask = glow_mask.filter(ImageFilter.GaussianBlur(SIZE * 0.012))
    glow_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    glow_layer.paste(Image.new("RGBA", (SIZE, SIZE), GLOW_BLUE + (255,)), mask=glow_mask)

    # Letter fill: vertical gradient, not horizontal -- the glyph's
    # bounding box is much taller than wide, so a horizontal gradient
    # only ever samples a narrow sliver (reads as flat purple).
    gradient = _gradient_strip(SIZE, ACCENT_STOPS, vertical=True).convert("RGBA")
    text_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    text_layer.paste(gradient, mask=text_mask)

    canvas = Image.alpha_composite(canvas, glow_layer)
    canvas = Image.alpha_composite(canvas, text_layer)

    rim = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle(
        [2, 2, SIZE - 3, SIZE - 3], radius=int(SIZE * 0.22),
        outline=(255, 255, 255, 40), width=max(2, SIZE // 256),
    )
    canvas = Image.alpha_composite(canvas, rim)

    canvas.save(OUT_PNG)
    canvas.save(OUT_ICO, sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"Saved {OUT_PNG}")
    print(f"Saved {OUT_ICO}")


if __name__ == "__main__":
    make_icon()
