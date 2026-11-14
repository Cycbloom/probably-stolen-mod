"""Generate the schematic background, without labels (Unity draws the buttons)."""
from pathlib import Path
import math
import random

from PIL import Image, ImageDraw, ImageFont


W, H = 1020, 492
rng = random.Random(441)
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

zones = [
    ([(69, 308), (123, 256), (280, 274), (383, 350), (373, 436), (72, 441)], "#2c4943", "#68867a"),
    ([(82, 89), (187, 53), (345, 65), (397, 157), (345, 215), (88, 223)], "#374941", "#8c9270"),
    ([(643, 268), (772, 245), (932, 283), (955, 425), (746, 448), (618, 404)], "#314b42", "#789379"),
    ([(638, 63), (884, 54), (952, 79), (954, 207), (767, 223), (629, 169)], "#2b4745", "#6a9590"),
]

for poly, fill, edge in zones:
    d.polygon(poly, fill=fill)
    d.line(poly + [poly[0]], fill=edge, width=2)

def road(points):
    d.line(points, fill="#19312f", width=27, joint="curve")
    d.line(points, fill="#557169", width=2, joint="curve")


road([(507, 487), (496, 396), (474, 319), (501, 241), (479, 164), (526, 79), (545, 38)])
road([(103, 245), (288, 240), (402, 267), (501, 241), (609, 227), (760, 241), (947, 244)])
road([(460, 303), (379, 329), (316, 375)])
road([(554, 266), (623, 317), (685, 352)])

# Landmarks stay legible at game size; decorative silhouettes avoid the button centers.
for i in range(5):
    x = 100 + 46 * i
    y = 300 + (i % 2) * 14
    d.rounded_rectangle((x, y, x + 36, y + 15), 2, fill="#42605a", outline="#6b857b")
    d.line((x + 6, y + 5, x + 30, y + 5), fill="#80968d")
for i in range(16):
    x = rng.randrange(102, 335)
    y = rng.choice([rng.randrange(90, 119), rng.randrange(172, 206)])
    w, h = rng.randrange(10, 28), rng.randrange(6, 15)
    d.polygon([(x, y + h), (x + 5, y), (x + w, y + 2), (x + w - 4, y + h)], fill="#657064", outline="#8e937e")
for i in range(8):
    x = 682 + (i % 4) * 55
    y = 292 if i < 4 else 387
    d.rectangle((x, y, x + 34, y + 19), fill="#466256", outline="#74917d")
    d.line((x, y + 5, x + 34, y + 5), fill="#74917d")
    d.line((x + 6, y + 8, x + 29, y + 17), fill="#597d6c")
for x in (665, 714, 763, 812, 861):
    for y in (75, 171):
        d.rounded_rectangle((x, y, x + 31, y + 22), 2, fill="#365855", outline="#69908b")
        d.rectangle((x + 4, y + 4, x + 23, y + 11), fill="#5f8179")
        d.line((x + 16, y + 22, x + 16, y + 27), fill="#6d8d81", width=2)

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

# North arrow in the map's service strip.
cx, cy = 553, 150
d.polygon([(cx, cy - 15), (cx - 7, cy + 9), (cx, cy + 4), (cx + 7, cy + 9)], fill="#d7bb7e")
d.text((cx, cy - 28), "N", anchor="mm", font=font, fill="#d7bb7e")
for i in range(4):
    x = 485 + 10 * i
    d.line((x, 435, x + 8, 428), fill="#b19f72", width=2)

target = Path(__file__).parent / "Paper" / "dumping_ground_map.png"
target.parent.mkdir(parents=True, exist_ok=True)
image.save(target, optimize=True)
