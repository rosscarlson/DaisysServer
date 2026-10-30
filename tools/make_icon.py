"""Draws Daisy's Server's icon (a server tower with a daisy in front) into src/DaisysServer/Assets/DaisysServer.ico."""
import math, os
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # drawn large, then scaled down per size

def tower(img):
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = 150, 70, 700, 950
    d.rounded_rectangle((x0 + 18, y0 + 26, x1 + 18, y1 + 26), 90, fill=(0, 0, 0, 90))
    d.rounded_rectangle((x0, y0, x1, y1), 90, fill=(52, 64, 82, 255), outline=(120, 140, 165, 255), width=22)
    for i in range(3):
        top = y0 + 110 + i * 220
        d.rounded_rectangle((x0 + 70, top, x1 - 70, top + 150), 36, fill=(30, 38, 50, 255))
        d.ellipse((x1 - 190, top + 45, x1 - 130, top + 105), fill=(61, 220, 132, 255))
        for k in range(3):
            d.rounded_rectangle((x0 + 120, top + 40 + k * 26, x0 + 300, top + 54 + k * 26), 7, fill=(95, 112, 135, 255))

def daisy(img, cx, cy, r):
    petals = 14
    for i in range(petals):
        a = 2 * math.pi * i / petals
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        pl, pw = r * 0.62, r * 0.20
        px, py = cx + math.cos(a) * (r - pl / 2), cy + math.sin(a) * (r - pl / 2)
        petal = Image.new("RGBA", (int(pl * 2), int(pl * 2)), (0, 0, 0, 0))
        pd = ImageDraw.Draw(petal)
        pd.ellipse((pl - pl / 2 - 4, pl - pw / 2 - 4, pl + pl / 2 + 4, pl + pw / 2 + 4), fill=(200, 205, 215, 255))
        pd.ellipse((pl - pl / 2, pl - pw / 2, pl + pl / 2, pl + pw / 2), fill=(255, 255, 255, 255))
        petal = petal.rotate(-math.degrees(a), resample=Image.BICUBIC)
        layer.paste(petal, (int(px - pl), int(py - pl)), petal)
        img.alpha_composite(layer)
    d = ImageDraw.Draw(img)
    c = r * 0.36
    d.ellipse((cx - c - 8, cy - c - 8, cx + c + 8, cy + c + 8), fill=(214, 140, 20, 255))
    d.ellipse((cx - c, cy - c, cx + c, cy + c), fill=(250, 196, 40, 255))
    d.ellipse((cx - c * 0.55, cy - c * 0.65, cx - c * 0.05, cy - c * 0.2), fill=(255, 225, 120, 255))

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
tower(img)
# a soft shadow under the flower so it stands off the tower
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sh).ellipse((470, 470, 980, 980), fill=(0, 0, 0, 110))
sh = sh.filter(ImageFilter.GaussianBlur(28))
img.alpha_composite(sh)
daisy(img, 715, 715, 290)

out = os.path.join(os.path.dirname(__file__), "..", "src", "DaisysServer", "Assets", "DaisysServer.ico")
sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
img.resize((256, 256), Image.LANCZOS).save(out, sizes=sizes)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(os.path.dirname(__file__), "..", "docs", "icon.png"))
print("written", os.path.abspath(out))
