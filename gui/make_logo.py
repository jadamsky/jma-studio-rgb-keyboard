"""Generates gui/logo.png -- the RGB-gradient "JMA" wordmark used as
both the tray icon and the in-app header logo. Not part of the running
app; a one-off build tool, re-run it by hand if the design ever needs
to change.

Run with:  python gui/make_logo.py
"""

import os

from PIL import Image, ImageDraw, ImageFont, ImageFilter

SIZE = 512
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logo.png")

# Segoe UI Bold -- chosen over a script/cursive font because those
# blur into an unreadable smudge at real tray-icon sizes (Windows
# often renders tray icons at just 16x16); bold sans stays legible
# down to that size while still reading as a clean, deliberate mark.
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\segoeuib.ttf",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def make_logo():
    font = load_font(190)
    text = "JMA"

    # Measure and center the text.
    tmp = Image.new("L", (SIZE, SIZE))
    d = ImageDraw.Draw(tmp)
    bbox = d.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pos = ((SIZE - tw) // 2 - bbox[0], (SIZE - th) // 2 - bbox[1])

    # Soft drop shadow for depth.
    shadow_mask = Image.new("L", (SIZE, SIZE), 0)
    sd = ImageDraw.Draw(shadow_mask)
    sd.text((pos[0] + 8, pos[1] + 10), text, font=font, fill=255)
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(6))
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 160), mask=shadow_mask)

    # Text mask (opaque where the glyphs are).
    text_mask = Image.new("L", (SIZE, SIZE), 0)
    td = ImageDraw.Draw(text_mask)
    td.text(pos, text, font=font, fill=255)

    # Horizontal Red -> Green -> Blue gradient, clipped to the glyphs.
    gradient = Image.new("RGB", (SIZE, 1))
    stops = [(0, (255, 60, 60)), (0.5, (60, 230, 110)), (1.0, (70, 130, 255))]
    for x in range(SIZE):
        t = x / (SIZE - 1)
        for i in range(len(stops) - 1):
            t0, c0 = stops[i]
            t1, c1 = stops[i + 1]
            if t0 <= t <= t1:
                f = (t - t0) / (t1 - t0)
                r = int(c0[0] + (c1[0] - c0[0]) * f)
                g = int(c0[1] + (c1[1] - c0[1]) * f)
                b = int(c0[2] + (c1[2] - c0[2]) * f)
                gradient.putpixel((x, 0), (r, g, b))
                break
    gradient = gradient.resize((SIZE, SIZE))
    gradient_rgba = gradient.convert("RGBA")

    text_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    text_layer.paste(gradient_rgba, mask=text_mask)

    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas = Image.alpha_composite(canvas, shadow)
    canvas = Image.alpha_composite(canvas, text_layer)

    canvas.save(OUT)
    print(f"Saved {OUT}")


if __name__ == "__main__":
    make_logo()
