"""Draws the Homelander fan mod's art and sounds from scratch (no game or show assets are used).

Suit overlays match the human limb sizes in People Playground 1.27.18 (person faces right):
Head 11x11, UpperBody 9x9, MiddleBody 9x9, LowerBody 9x12, UpperArm 5x13, LowerArm 5x16,
UpperLeg 5x16, LowerLeg 5x14, Foot 9x3. PPLua stretches each overlay over its limb and keeps the
game's own outline and shading.
"""
import math
import random
import struct
import sys
import wave
from pathlib import Path

from PIL import Image

root = Path(sys.argv[1])
for sub in ("suit", "sprites", "sounds"):
    (root / sub).mkdir(parents=True, exist_ok=True)

CLEAR = (0, 0, 0, 0)
BLUE = (40, 70, 165, 255)
BLUE_D = (26, 46, 115, 255)
GOLD = (232, 186, 62, 255)
GOLD_D = (170, 122, 30, 255)
HAIR = (238, 208, 122, 255)
HAIR_D = (196, 160, 80, 255)
BOOT = (26, 36, 88, 255)
BELT = (34, 34, 52, 255)
RED = (176, 26, 36, 255)
RED_D = (118, 14, 24, 255)
RED_L = (212, 48, 56, 255)
SKIN = (238, 200, 170, 255)


def canvas(w, h, fill=CLEAR):
    return Image.new("RGBA", (w, h), fill)


def put(im, pts, color):
    for x, y in pts:
        if 0 <= x < im.width and 0 <= y < im.height:
            im.putpixel((x, y), color)


def rect(im, x0, y0, x1, y1, color):
    put(im, [(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)], color)


# ---- suit overlays ----

head = canvas(11, 11)
rect(head, 0, 0, 10, 1, HAIR)
rect(head, 0, 2, 6, 2, HAIR)
rect(head, 0, 3, 1, 6, HAIR)
put(head, [(6, 1), (7, 1), (8, 1)], HAIR_D)  # slicked-back part
put(head, [(1, 2), (0, 5), (1, 6)], HAIR_D)
head.save(root / "suit" / "Head.png")

upper = canvas(9, 9, BLUE)
rect(upper, 2, 1, 8, 1, GOLD)               # eagle wing over the shoulder...
rect(upper, 4, 2, 8, 2, GOLD)
rect(upper, 6, 3, 8, 3, GOLD)               # ...sweeping down to the chest
put(upper, [(3, 1), (5, 2), (7, 3), (8, 4)], GOLD_D)
rect(upper, 0, 0, 8, 0, BLUE_D)             # collar
upper.save(root / "suit" / "UpperBody.png")

middle = canvas(9, 9, BLUE)
put(middle, [(6, y) for y in range(9)], BLUE_D)
middle.save(root / "suit" / "MiddleBody.png")

lower = canvas(9, 12, BLUE)
rect(lower, 0, 0, 8, 1, BELT)
rect(lower, 6, 0, 8, 1, GOLD)               # buckle at the front
put(lower, [(6, 1)], GOLD_D)
lower.save(root / "suit" / "LowerBody.png")

upper_arm = canvas(5, 13, BLUE)
rect(upper_arm, 0, 0, 4, 1, GOLD)           # wing tip on the shoulder
put(upper_arm, [(0, 1), (1, 1)], GOLD_D)
upper_arm.save(root / "suit" / "UpperArm.png")

lower_arm = canvas(5, 16, BLUE)
rect(lower_arm, 0, 11, 4, 11, BLUE_D)       # glove cuff
lower_arm.save(root / "suit" / "LowerArm.png")

upper_leg = canvas(5, 16, BLUE)
put(upper_leg, [(3, y) for y in range(2, 16)], BLUE_D)
upper_leg.save(root / "suit" / "UpperLeg.png")

lower_leg = canvas(5, 14, BLUE)
rect(lower_leg, 0, 9, 4, 13, BOOT)
rect(lower_leg, 0, 9, 4, 9, BLUE_D)
lower_leg.save(root / "suit" / "LowerLeg.png")

foot = canvas(9, 3, BOOT)
foot.save(root / "suit" / "Foot.png")

# ---- cape: hangs from a pivot at its top centre, behind the back ----

cape = canvas(7, 44)
for y in range(44):
    half = 2 if y < 4 else 3
    for x in range(3 - half, 3 + half + 1):
        shade = RED
        if x == 3 - half:
            shade = RED_L
        elif (x + y // 6) % 3 == 0:
            shade = RED_D
        cape.putpixel((x, y), shade)
rect(cape, 2, 0, 4, 1, GOLD)                # clasp
put(cape, [(3, 1)], GOLD_D)
for x in range(7):                          # ragged-free straight hem
    if cape.getpixel((x, 43))[3]:
        cape.putpixel((x, 43), RED_D)
cape.save(root / "sprites" / "cape.png")

# ---- catalog thumbnail: full figure, cape behind, facing right ----

thumb = canvas(24, 40)
rect(thumb, 4, 9, 9, 33, RED)               # cape
put(thumb, [(4, y) for y in range(9, 34)], RED_L)
put(thumb, [(6, y) for y in range(12, 34, 2)], RED_D)
rect(thumb, 9, 1, 17, 8, SKIN)              # head
rect(thumb, 9, 0, 17, 2, HAIR)
rect(thumb, 9, 3, 10, 5, HAIR)
put(thumb, [(15, 4)], (40, 40, 60, 255))    # eye
rect(thumb, 8, 9, 18, 24, BLUE)             # torso
rect(thumb, 9, 10, 18, 11, GOLD)
rect(thumb, 12, 12, 18, 12, GOLD)
rect(thumb, 15, 13, 18, 13, GOLD)
rect(thumb, 8, 22, 18, 23, BELT)
rect(thumb, 16, 22, 18, 23, GOLD)
rect(thumb, 13, 10, 16, 26, BLUE_D)         # front arm
rect(thumb, 13, 10, 16, 11, GOLD)
rect(thumb, 9, 24, 12, 35, BLUE)            # legs
rect(thumb, 14, 24, 17, 35, BLUE_D)
rect(thumb, 9, 35, 14, 38, BOOT)
rect(thumb, 14, 35, 20, 38, BOOT)
thumb.save(root / "thumb.png")

# ---- category icon: gold wings on a blue disc ----

icon = canvas(32, 32)
for y in range(32):
    for x in range(32):
        if (x - 15.5) ** 2 + (y - 15.5) ** 2 <= 15 ** 2:
            icon.putpixel((x, y), BLUE)
for i in range(9):                          # two wings rising from the centre
    rect(icon, 15 - i - 2, 18 - i, 15 - i, 18 - i, GOLD)
    rect(icon, 16 + i, 18 - i, 18 + i, 18 - i, GOLD)
    put(icon, [(15 - i - 2, 19 - i), (18 + i, 19 - i)], GOLD_D)
rect(icon, 14, 16, 17, 24, GOLD)            # body of the eagle
rect(icon, 14, 13, 17, 15, GOLD_D)
icon.save(root / "icon.png")

# ---- sounds ----

RATE = 22050
rng = random.Random(7)


def write(name, samples):
    with wave.open(str(root / "sounds" / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples))


def envelope(i, n, attack, release):
    t = i / RATE
    end = (n - i) / RATE
    return min(1.0, t / attack, end / release)


n = int(RATE * 0.42)
laser = []
for i in range(n):
    t = i / RATE
    saw = ((t * 92) % 1.0) * 2 - 1
    whine = math.sin(2 * math.pi * (1750 + 60 * math.sin(2 * math.pi * 9 * t)) * t)
    laser.append((0.35 * saw + 0.25 * whine + 0.15 * (rng.random() * 2 - 1)) * envelope(i, n, 0.03, 0.06))
write("laser.wav", laser)

n = int(RATE * 1.2)
boom = []
for i in range(n):
    t = i / RATE
    freq = 35 + 55 * math.exp(-t * 4)
    thump = math.sin(2 * math.pi * freq * t) * math.exp(-t * 3.5)
    crack = (rng.random() * 2 - 1) * math.exp(-t * 18)
    boom.append(0.8 * thump + 0.6 * crack)
write("boom.wav", boom)

n = int(RATE * 0.7)
whoosh, low = [], 0.0
for i in range(n):
    t = i / RATE
    cutoff = 0.04 + 0.25 * math.sin(math.pi * t / 0.7)
    low += cutoff * ((rng.random() * 2 - 1) - low)
    whoosh.append(1.6 * low * math.sin(math.pi * t / 0.7))
write("whoosh.wav", whoosh)

print("Homelander assets written to", root)
