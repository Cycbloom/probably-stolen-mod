"""Generate the schematic background, without labels (Unity draws the buttons)."""
from pathlib import Path
import random

from PIL import Image, ImageDraw, ImageFont


W, H = 1020, 492
rng = random.Random(415)
image = Image.new("RGB", (W, H))
pixels = image.load()
for y in range(H):
    for x in range(W):
        v = max(0, 1 - abs(x - W / 2) / (W / 2) - abs(y - H / 2) / H)
        n = rng.randrange(-4, 5)
        pixels[x, y] = (20 + int(v * 12) + n, 40 + int(v * 18) + n, 39 + int(v * 16) + n)
d = ImageDraw.Draw(image)

for x in range(24, W, 24):
    d.line((x, 0, x, H), fill="#274440" if x % 96 == 0 else "#24403d")
for y in range(24, H, 24):
    d.line((0, y, W, y), fill="#274440" if y % 96 == 0 else "#24403d")


def plot(cx, cy, w, h, seeds):
    """Hand-cut octagon around (cx, cy); seeds perturb the corners a little."""
    hw, hh = w / 2, h / 2
    base = [(-hw, -hh * 0.55), (-hw * 0.55, -hh), (hw * 0.55, -hh), (hw, -hh * 0.55),
            (hw, hh * 0.55), (hw * 0.55, hh), (-hw * 0.55, hh), (-hw, hh * 0.55)]
    return [(cx + dx + rng.randrange(-seeds, seeds + 1),
             cy + dy + rng.randrange(-seeds, seeds + 1)) for dx, dy in base]


zones = [
    # (polygon, fill, edge) in top-down image coordinates
    (plot(204, 384, 268, 112, 7), "#2c4943", "#68867a"),  # gate, bottom left
    (plot(204, 246, 272, 108, 7), "#374941", "#8c9270"),  # scrap, middle left
    (plot(204, 108, 264, 110, 7), "#314b47", "#789379"),  # medical, top left
    (plot(796, 120, 248, 104, 7), "#3a4740", "#8f8f6d"),  # weapons, upper right
    (plot(796, 380, 268, 108, 7), "#314b42", "#789379"),  # refuse, lower right
]

for poly, fill, edge in zones:
    d.polygon(poly, fill=fill)
    d.line(poly + [poly[0]], edge, width=2)


def road(points, width=22):
    d.line(points, fill="#19312f", width=width, joint="curve")
    d.line(points, fill="#557169", width=2, joint="curve")


# Lanes run through the gaps between plots so no button is split by a road.
road([(358, 482), (352, 320), (356, 176), (350, 30)])
road([(655, 482), (661, 320), (657, 176), (663, 30)])
road([(58, 176), (200, 170), (356, 176), (510, 171), (657, 176), (800, 170), (960, 176)])
road([(58, 320), (200, 326), (356, 320), (510, 326), (657, 320), (800, 326), (960, 320)])

# Decorative silhouettes stay near the plot edges, clear of the buttons.
def barrel(x, y, fill="#466256", edge="#74917d"):
    d.rounded_rectangle((x, y, x + 26, y + 32), 3, fill=fill, outline=edge)
    d.line((x, y + 8, x + 26, y + 8), fill=edge)
    d.line((x, y + 24, x + 26, y + 24), fill=edge)


def crate(x, y, w=30, h=20, fill="#365855", edge="#69908b"):
    d.rectangle((x, y, x + w, y + h), fill=fill, outline=edge)
    d.line((x, y + 5, x + w, y + 5), fill=edge)
    d.line((x + 7, y + 8, x + w - 7, y + h - 3), fill="#597d6c")


# gate: lockers and a couple of bottles
for i in range(4):
    x = 72 + 34 * i
    d.rounded_rectangle((x, 412, x + 24, 436), 2, fill="#42605a", outline="#6b857b")
    d.line((x + 5, 417, x + 19, 417), fill="#80968d")
for i in range(3):
    barrel(68 + 30 * i, 336)

# scrap: loose plate piles
for i in range(9):
    x = 74 + rng.randrange(0, 190)
    y = rng.choice([206, 214, 276, 284])
    w, h = rng.randrange(14, 30), rng.randrange(6, 14)
    d.polygon([(x, y + h), (x + 5, y), (x + w, y + 2), (x + w - 4, y + h)], fill="#657064", outline="#8e937e")

# medical: marked crates
for i, y in enumerate((62, 142)):
    crate(78, y, 34, 22, "#3f5f55", "#87a08c")
    d.line((95, y + 5, 95, y + 17), fill="#c9d6b9", width=2)
    d.line((89, y + 11, 101, y + 11), fill="#c9d6b9", width=2)
    barrel(300, y + 2, "#3c5952", "#7e9a82")

# weapons: racks
for i in range(3):
    x = 700 + 16 * i
    d.line((x, 86, x + 10, 158), fill="#7d8a67", width=3)
for i in range(3):
    x = 872 + 14 * i
    d.line((x, 156, x - 6, 84), fill="#7d8a67", width=3)

# refuse: dumpsters
for i in range(3):
    x = 716 + 56 * i
    d.rectangle((x, 396, x + 38, 417), fill="#466256", outline="#74917d")
    d.line((x, 402, x + 38, 402), fill="#74917d")
    d.line((x + 6, 406, x + 32, 414), fill="#597d6c")
for i in range(3):
    barrel(700 + 70 * i, 338, "#3f5d54", "#78917c")

d.rounded_rectangle((10, 10, W - 11, H - 11), 8, outline="#789184", width=2)
d.rectangle((37, 36, W - 38, H - 37), outline="#48665e", width=1)
font_path = Path("C:/Windows/Fonts/consola.ttf")
font = ImageFont.truetype(str(font_path), 14) if font_path.exists() else ImageFont.load_default()
for i, x in enumerate((112, 305, 501, 696, 891)):
    d.text((x, 25), chr(65 + i), anchor="mm", font=font, fill="#93a89a")
for i, y in enumerate((90, 190, 292, 394)):
    d.text((23, y), str(i + 1), anchor="mm", font=font, fill="#93a89a")
d.text((48, 465), "SURVEY / DG-04", anchor="lm", font=font, fill="#a1b8a7")
d.text((970, 465), "LOCAL ROUTES", anchor="rm", font=font, fill="#a1b8a7")

# North arrow in the empty bottom-center lot.
cx, cy = 514, 392
d.polygon([(cx, cy - 15), (cx - 7, cy + 9), (cx, cy + 4), (cx + 7, cy + 9)], fill="#d7bb7e")
d.text((cx, cy - 28), "N", anchor="mm", font=font, fill="#d7bb7e")
for i in range(4):
    x = 470 + 10 * i
    d.line((x, 432, x + 8, 425), fill="#b19f72", width=2)

target = Path(__file__).parent / "Paper" / "dumping_ground_map.png"
target.parent.mkdir(parents=True, exist_ok=True)
image.save(target, optimize=True)
