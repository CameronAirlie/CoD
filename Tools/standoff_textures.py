"""Procedural, original texture set for the Standoff-inspired village (numpy + Pillow).

Every generator returns (albedo RGB uint8 array, height float array in [0, 1] or None,
emission RGB uint8 array or None). Tiling textures are seamless; decal textures
(windows, doors, signs) are authored for 0..1 UVs with v = 0 at the top row.
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

N = 512
FONT = 'C:/Windows/Fonts/arialbd.ttf'
FONT_SERIF = 'C:/Windows/Fonts/georgiab.ttf'


def rng(name):
    # Deterministic per name (Python's str hash is salted per process).
    return np.random.default_rng(sum(ord(c) * (i + 1) for i, c in enumerate(name)))


def tile_noise(r, cells, size=N):
    """Seamless value noise: tile a random grid 3x3, upsample bicubically, crop the middle."""
    grid = r.random((cells, cells)).astype(np.float32)
    big = np.tile(grid, (3, 3))
    img = Image.fromarray((big * 255).astype(np.uint8)).resize((size * 3, size * 3), Image.BICUBIC)
    return np.asarray(img, np.float32)[size:2 * size, size:2 * size] / 255.0


def fbm(r, base=4, octaves=5, size=N, persistence=.5):
    total = np.zeros((size, size), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        total += tile_noise(r, base * 2 ** o, size) * amp
        norm += amp
        amp *= persistence
    return total / norm


def lerp(a, b, t):
    a = np.asarray(a, np.float32); b = np.asarray(b, np.float32)
    return a + (b - a) * t[..., None]


def finish(rgb):
    return np.clip(rgb, 0, 255).astype(np.uint8)


def blur(arr, radius):
    img = Image.fromarray(np.clip(arr * 255, 0, 255).astype(np.uint8))
    big = Image.new('L', (img.width * 3, img.height * 3))
    for i in range(3):
        for j in range(3):
            big.paste(img, (i * img.width, j * img.height))
    big = big.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(big, np.float32)[img.height:2 * img.height, img.width:2 * img.width] / 255.0


def normal_map(height, strength=4.0):
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(height)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return finish((n * .5 + .5) * 255)


def grime(r, rgb, amount=.35, streaks=True):
    """Darken with blotches and vertical rain streaks running down (towards larger rows)."""
    h, w = rgb.shape[:2]
    resize = lambda a: np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR), np.float32) / 255
    blot = resize(fbm(r, 3, 4))
    rgb = rgb * (1 - amount * np.clip(blot - .45, 0, 1)[..., None] * 1.6)
    if streaks:
        cols = resize(tile_noise(r, 64))[0:1, :].repeat(h, 0)
        fall = np.linspace(0, 1, h, dtype=np.float32)[:, None] ** 2
        rgb = rgb * (1 - .22 * np.clip(cols - .55, 0, 1)[..., None] * 3 * fall[..., None])
    return rgb


# ---------------------------------------------------------------- ground
def asphalt(name):
    r = rng(name)
    n = fbm(r, 6, 6)
    grain = r.random((N, N)).astype(np.float32)
    rgb = lerp((62, 63, 62), (98, 98, 95), n) + (grain[..., None] - .5) * 28
    patch = fbm(r, 2, 3)
    rgb *= (1 - .18 * (patch > .58))[..., None]
    img = Image.fromarray(finish(rgb)); d = ImageDraw.Draw(img)
    h = grain * .3 + n * .5
    hi = Image.fromarray((h * 255).astype(np.uint8)); dh = ImageDraw.Draw(hi)
    for _ in range(14):
        x, y = r.random(2) * N
        a = r.random() * math.tau
        for _ in range(40):
            a += r.normal(0, .5); nx, ny = x + math.cos(a) * 6, y + math.sin(a) * 6
            d.line((x, y, nx, ny), fill=(35, 35, 34), width=1 + int(r.random() * 2))
            dh.line((x, y, nx, ny), fill=0, width=2)
            x, y = nx % N, ny % N
    return np.asarray(img), np.asarray(hi, np.float32) / 255, None


def cobble(name, base=(118, 112, 102), size=34):
    r = rng(name)
    img = Image.new('RGB', (N, N), (70, 66, 60)); d = ImageDraw.Draw(img)
    mask = Image.new('L', (N, N), 0); dm = ImageDraw.Draw(mask)
    rows = N // size
    for row in range(rows):
        off = (row % 2) * size // 2
        x = -size + off
        while x < N + size:
            w = size * (.75 + r.random() * .5)
            c = (lambda f: tuple(int(base[k] * f) for k in range(3)))(.75 + r.random() * .4)
            box = (x + 2, row * size + 2, x + w - 2, row * size + size - 2)
            for dx in (-N, 0, N):
                b2 = (box[0] + dx, box[1], box[2] + dx, box[3])
                d.rounded_rectangle(b2, radius=size // 4, fill=c)
                dm.rounded_rectangle(b2, radius=size // 4, fill=255)
            x += w
    h = blur(np.asarray(mask, np.float32) / 255, 3)
    rgb = np.asarray(img, np.float32) * (.8 + .3 * fbm(r, 8, 3))[..., None]
    rgb = grime(r, rgb, .4, False)
    return finish(rgb), h, None


def concrete(name, base=(150, 148, 140)):
    r = rng(name)
    n = fbm(r, 5, 6)
    rgb = lerp(np.array(base) * .78, np.array(base) * 1.08, n) + (r.random((N, N, 1)) - .5) * 10
    rgb = grime(r, rgb, .3, False)
    rgb[:, :3] *= .6; rgb[:3, :] *= .6
    h = n * .4 + r.random((N, N)).astype(np.float32) * .08
    h[:, :3] = 0; h[:3, :] = 0
    return finish(rgb), h, None


def grass(name):
    r = rng(name)
    n = fbm(r, 4, 6)
    dry = fbm(r, 3, 4)
    rgb = lerp((58, 72, 34), (98, 104, 52), n)
    rgb = lerp(rgb, (128, 112, 74), np.clip((dry - .55) * 3, 0, 1))
    img = Image.fromarray(finish(rgb)); d = ImageDraw.Draw(img)
    for _ in range(9000):
        x, y = r.random(2) * N; l = 3 + r.random() * 7
        g = int(70 + r.random() * 70)
        d.line((x, y, x + r.normal(0, 1.5), y - l), fill=(int(g * .75), g, int(g * .4)), width=1)
    h = fbm(r, 16, 3)
    return np.asarray(img), h, None


def dirt(name, base=(112, 92, 66)):
    r = rng(name)
    n = fbm(r, 5, 6)
    rgb = lerp(np.array(base) * .7, np.array(base) * 1.15, n) + (r.random((N, N, 1)) - .5) * 18
    img = Image.fromarray(finish(rgb)); d = ImageDraw.Draw(img)
    for _ in range(500):
        x, y = r.random(2) * N; s = 1 + r.random() * 3
        c = int(90 + r.random() * 80)
        d.ellipse((x, y, x + s, y + s), fill=(c, int(c * .92), int(c * .82)))
    return np.asarray(img), n * .6 + r.random((N, N)).astype(np.float32) * .2, None


def field(name):
    r = rng(name)
    rgb, h, _ = dirt(name, (102, 82, 58))
    rows = (np.sin(np.arange(N) / N * math.tau * 16) * .5 + .5).astype(np.float32)[:, None].repeat(N, 1)
    rgb = rgb.astype(np.float32) * (.72 + .4 * rows[..., None])
    green = (fbm(r, 12, 3) > .55) & (rows > .7)
    rgb[green] = rgb[green] * .5 + np.array([60, 80, 35]) * .5
    return finish(rgb), rows * .7 + h * .3, None


# ---------------------------------------------------------------- walls
def stone(name, base=(150, 140, 122)):
    r = rng(name)
    img = Image.new('RGB', (N, N), (88, 82, 72)); d = ImageDraw.Draw(img)
    mask = Image.new('L', (N, N), 0); dm = ImageDraw.Draw(mask)
    y = 0
    while y < N:
        rh = int(26 + r.random() * 22)
        if y + rh > N - 10: rh = N - y
        x = -int(r.random() * 40)
        while x < N:
            w = int(38 + r.random() * 60)
            c = (lambda f: tuple(int(base[k] * f) for k in range(3)))(.72 + r.random() * .4)
            pts = [(x + 3 + r.random() * 4, y + 3 + r.random() * 3), (x + w - 3 - r.random() * 4, y + 3 + r.random() * 3),
                   (x + w - 3 - r.random() * 4, y + rh - 3 - r.random() * 3), (x + 3 + r.random() * 4, y + rh - 3 - r.random() * 3)]
            for dx in (-N, 0, N):
                p = [(px + dx, py) for px, py in pts]
                d.polygon(p, fill=c); dm.polygon(p, fill=255)
            x += w
        y += rh
    h = blur(np.asarray(mask, np.float32) / 255, 2.5) * .8 + fbm(r, 16, 3) * .2
    rgb = np.asarray(img, np.float32) * (.85 + .25 * fbm(r, 10, 3))[..., None]
    rgb = grime(r, rgb, .45)
    return finish(rgb), h, None


def brick(name, base=(132, 64, 46), mortar=(150, 140, 122)):
    r = rng(name)
    img = Image.new('RGB', (N, N), mortar); d = ImageDraw.Draw(img)
    mask = Image.new('L', (N, N), 0); dm = ImageDraw.Draw(mask)
    bh, bw = 22, 64
    for row in range(N // bh + 1):
        off = (row % 2) * bw // 2
        for col in range(-1, N // bw + 1):
            c = (lambda f: tuple(int(base[k] * f) for k in range(3)))(.7 + r.random() * .45)
            b = (col * bw + off + 2, row * bh + 2, col * bw + off + bw - 2, row * bh + bh - 2)
            d.rectangle(b, fill=c); dm.rectangle(b, fill=255)
    h = blur(np.asarray(mask, np.float32) / 255, 1.5)
    rgb = grime(r, np.asarray(img, np.float32) * (.85 + .3 * fbm(r, 12, 3))[..., None], .4)
    return finish(rgb), h, None


def plaster(name, base, chipped=(122, 84, 62)):
    r = rng(name)
    n = fbm(r, 4, 6)
    rgb = lerp(np.array(base) * .82, np.array(base) * 1.05, n) + (r.random((N, N, 1)) - .5) * 8
    rgb = grime(r, rgb, .5)
    chip = fbm(r, 9, 4)
    holes = chip > .78
    rgb[holes] = rgb[holes] * .55 + np.array(chipped) * .45
    h = np.where(holes, 0.2, 0.6).astype(np.float32) + n * .2
    return finish(rgb), blur(h, 1.2), None


def slate(name, base=(64, 66, 72)):
    r = rng(name)
    img = Image.new('RGB', (N, N), (30, 30, 32)); d = ImageDraw.Draw(img)
    h = np.zeros((N, N), np.float32)
    rh, w = 32, 40
    for row in range(N // rh):
        off = (row % 2) * w // 2
        for col in range(-1, N // w + 1):
            c = (lambda f: tuple(int(base[k] * f) for k in range(3)))(.75 + r.random() * .4)
            d.rectangle((col * w + off + 1, row * rh, col * w + off + w - 2, row * rh + rh - 2), fill=c)
        h[row * rh:(row + 1) * rh] = np.linspace(.2, 1, rh)[:, None]
    rgb = np.asarray(img, np.float32)
    moss = fbm(r, 4, 4) > .62
    rgb[moss] = rgb[moss] * .6 + np.array([70, 78, 52]) * .4
    return finish(grime(r, rgb, .3, False)), h, None


def terracotta(name):
    r = rng(name)
    x = np.arange(N, dtype=np.float32)
    ridge = (np.sin(x / N * math.tau * 12) * .5 + .5)[None, :].repeat(N, 0)
    rows = np.zeros((N, N), np.float32)
    rh = 42
    for row in range(N // rh + 1):
        rows[row * rh:(row + 1) * rh] = np.linspace(.35, 1, min(rh, N - row * rh))[:, None]
    h = ridge * .6 + rows * .4
    tint = fbm(r, 8, 3)
    rgb = lerp((128, 58, 36), (182, 96, 60), tint) * (.6 + .5 * h[..., None])
    moss = fbm(r, 4, 4) > .64
    rgb[moss] = rgb[moss] * .55 + np.array([80, 80, 50]) * .45
    return finish(grime(r, rgb, .3, False)), h, None


def corrugated(name, base=(118, 126, 128), rust=.4):
    r = rng(name)
    x = np.arange(N, dtype=np.float32)
    wave = (np.sin(x / N * math.tau * 32) * .5 + .5)[None, :].repeat(N, 0)
    n = fbm(r, 5, 5)
    rgb = lerp(np.array(base) * .75, np.array(base) * 1.1, wave * .6 + n * .4)
    rusty = fbm(r, 6, 5)
    t = np.clip((rusty - (1 - rust * .7)) * 4, 0, 1)
    rgb = lerp(rgb, (120, 70, 40), t)
    return finish(grime(r, rgb, .3)), wave, None


def planks(name, base=(112, 96, 76), width=48, vertical=True):
    r = rng(name)
    n = fbm(r, 4, 5)
    grain = tile_noise(r, 128)
    rgb = lerp(np.array(base) * .7, np.array(base) * 1.1, n * .5 + grain * .5)
    h = np.ones((N, N), np.float32) * .7
    for k in range(0, N, width):
        tint = .8 + r.random() * .35
        sl = (slice(None), slice(k, k + width)) if vertical else (slice(k, k + width), slice(None))
        rgb[sl] *= tint
        if vertical:
            rgb[:, k:k + 2] *= .4; h[:, k:k + 2] = 0
        else:
            rgb[k:k + 2] *= .4; h[k:k + 2] = 0
    streak = tile_noise(r, 256)
    rgb *= (.85 + .3 * (streak if vertical else streak.T))[..., None]
    return finish(grime(r, rgb, .3, False)), blur(h, 1) * .7 + grain * .3, None


def hay(name):
    r = rng(name)
    img = Image.new('RGB', (N, N), (150, 122, 64)); d = ImageDraw.Draw(img)
    for _ in range(9000):
        x, y = r.random(2) * N; a = r.normal(0, .35); l = 8 + r.random() * 22
        g = int(130 + r.random() * 90)
        d.line((x, y, x + math.cos(a) * l, y + math.sin(a) * l), fill=(g, int(g * .82), int(g * .45)), width=1 + int(r.random() * 2))
    rgb = np.asarray(img, np.float32) * (.75 + .3 * fbm(r, 6, 3))[..., None]
    return finish(rgb), fbm(r, 32, 2), None


def paint(name, base, noise=10, dirt_amount=.3, scratches=0):
    r = rng(name)
    n = fbm(r, 4, 5)
    rgb = lerp(np.array(base) * .85, np.array(base) * 1.08, n) + (r.random((N, N, 1)) - .5) * noise * .3
    rgb = grime(r, rgb, dirt_amount)
    img = Image.fromarray(finish(rgb)); d = ImageDraw.Draw(img)
    for _ in range(scratches):
        x, y = r.random(2) * N
        d.line((x, y, x + r.normal(0, 18), y + r.normal(0, 3)), fill=tuple(int(c * 1.25) for c in base), width=1)
    return np.asarray(img), n * .2, None


def leaves(name, base=(52, 74, 36)):
    r = rng(name)
    img = Image.new('RGB', (N, N), tuple(int(c * .5) for c in base)); d = ImageDraw.Draw(img)
    for _ in range(5000):
        x, y = r.random(2) * N; s = 3 + r.random() * 7
        t = .6 + r.random() * .7
        d.ellipse((x, y, x + s, y + s * .6), fill=tuple(int(c * t) for c in base))
    rgb = np.asarray(img, np.float32) * (.55 + .4 * fbm(r, 6, 3))[..., None]
    return finish(rgb), fbm(r, 32, 2), None


def bark(name):
    r = rng(name)
    n = tile_noise(r, 8)[:, :1].repeat(N, 1).T * .5 + tile_noise(r, 64) * .5
    stretched = np.asarray(Image.fromarray((tile_noise(r, 32) * 255).astype(np.uint8)).resize((N, N)), np.float32) / 255
    h = np.repeat(stretched[:1, :], N, 0) * .6 + n * .4
    rgb = lerp((48, 38, 30), (98, 84, 66), h)
    return finish(rgb), h, None


def forest(name):
    r = rng(name)
    n = fbm(r, 6, 6)
    rgb = lerp((40, 56, 34), (66, 80, 46), n)
    img = Image.fromarray(finish(rgb)); d = ImageDraw.Draw(img)
    for _ in range(2500):
        x, y = r.random(2) * N; s = 3 + r.random() * 6; t = .55 + r.random() * .6
        d.ellipse((x, y, x + s, y + s), fill=(int(40 * t), int(62 * t), int(34 * t)))
    rock = fbm(r, 3, 5) > .72
    out = np.asarray(img, np.float32); out[rock] = out[rock] * .4 + np.array([110, 108, 98]) * .6
    return finish(out), n, None


def forest_far(name):
    """Distant ridges: forest tones pushed towards the sky haze seen in every reference view."""
    rgb, h, _ = forest(name)
    return finish(rgb.astype(np.float32) * .62 + np.array([104, 120, 128]) * .38), h, None


# ---------------------------------------------------------------- decals (0..1 UVs, v=0 at top)
def window(name, frame=(214, 210, 198), shutters=None, bars=False, broken=False):
    r = rng(name)
    W, H = 256, 384
    img = Image.new('RGB', (W, H), frame); d = ImageDraw.Draw(img)
    x0 = 50 if shutters else 16
    x1 = W - x0
    if shutters:
        for sx in ((0, x0 - 6), (W - x0 + 6, W)):
            d.rectangle((sx[0], 10, sx[1], H - 10), fill=shutters)
            for y in range(22, H - 20, 14): d.line((sx[0] + 4, y, sx[1] - 4, y), fill=tuple(int(c * .6) for c in shutters), width=4)
    d.rectangle((x0, 16, x1, H - 30), fill=(34, 40, 46))
    g = np.asarray(img, np.float32)
    ys = np.linspace(0, 1, H)[:, None]
    refl = (np.clip(1 - ys * 1.6, 0, 1) * 70 + 10)
    sub = g[16:H - 30, x0:x1]
    sub += refl[16:H - 30, :][..., None] * np.array([.7, .85, 1]) * .8
    g[16:H - 30, x0:x1] = sub
    img = Image.fromarray(finish(g)); d = ImageDraw.Draw(img)
    mid = (x0 + x1) // 2
    d.rectangle((mid - 5, 16, mid + 5, H - 30), fill=frame)
    d.rectangle((x0, H // 2 - 30, x1, H // 2 - 22), fill=frame)
    d.rectangle((x0 - 10, H - 30, x1 + 10, H - 12), fill=tuple(int(c * .8) for c in frame))
    if bars:
        for x in range(x0 + 18, x1, 26): d.line((x, 16, x, H - 30), fill=(40, 40, 40), width=4)
    if broken:
        for _ in range(6):
            x, y = x0 + r.random() * (x1 - x0), 20 + r.random() * (H - 60)
            d.line((x, y, x + r.normal(0, 40), y + r.normal(0, 40)), fill=(200, 210, 215), width=1)
    rgb = grime(r, np.asarray(img, np.float32), .25)
    return finish(rgb), None, None


def door(name, color, panels=True, glass=False):
    r = rng(name)
    W, H = 256, 512
    img = Image.new('RGB', (W, H), (90, 82, 70)); d = ImageDraw.Draw(img)
    d.rectangle((14, 14, W - 14, H), fill=color)
    dark = tuple(int(c * .7) for c in color)
    if panels:
        for y0, y1 in ((40, 220), (260, 470)):
            d.rectangle((40, y0, W // 2 - 12, y1), outline=dark, width=6)
            d.rectangle((W // 2 + 12, y0, W - 40, y1), outline=dark, width=6)
    if glass:
        d.rectangle((40, 40, W - 40, 220), fill=(40, 50, 60))
    d.ellipse((W - 52, 270, W - 36, 286), fill=(190, 180, 140))
    rgb = grime(r, np.asarray(img, np.float32), .35)
    return finish(rgb), None, None


def garage(name):
    r = rng(name)
    rgb, _, _ = corrugated(name, (200, 200, 192), .2)
    rgb = np.asarray(Image.fromarray(rgb).rotate(90), np.float32)
    return finish(grime(r, rgb, .3)), None, None


def sign(name, text, bg, fg, w=1024, h=256, font=FONT, size=150, border=None, sub=None):
    img = Image.new('RGB', (w, h), bg); d = ImageDraw.Draw(img)
    f = ImageFont.truetype(font, size)
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((w - bb[2]) / 2, (h - bb[3]) / 2 - (25 if sub else 0) - bb[1] / 2), text, font=f, fill=fg)
    if sub:
        fs = ImageFont.truetype(FONT, size // 3); bs = d.textbbox((0, 0), sub, font=fs)
        d.text(((w - bs[2]) / 2, h - bs[3] - 18), sub, font=fs, fill=fg)
    if border: d.rectangle((6, 6, w - 6, h - 6), outline=border, width=10)
    r = rng(name)
    return finish(grime(r, np.asarray(img, np.float32), .3)), None, None


def logo_u(name):
    S = 512
    img = Image.new('RGB', (S, S), (230, 175, 30)); d = ImageDraw.Draw(img)
    d.ellipse((8, 8, S - 8, S - 8), fill=(24, 46, 98))
    d.ellipse((40, 40, S - 40, S - 40), fill=(238, 184, 34))
    d.text((S * .5, S * .52), 'U', font=ImageFont.truetype(FONT, 330), fill=(24, 46, 98), anchor='mm')
    d.rectangle((S * .22, S * .47, S * .78, S * .53), fill=(24, 46, 98))
    em = np.asarray(img, np.float32) * .25
    return np.asarray(img), None, finish(em)


def fascia(name):
    w, h = 1024, 160
    img = Image.new('RGB', (w, h), (224, 168, 34)); d = ImageDraw.Draw(img)
    d.rectangle((0, h - 22, w, h), fill=(24, 46, 98))
    for cx in (110, w - 110):
        d.ellipse((cx - 62, 18, cx + 62, 142), fill=(24, 46, 98)); d.ellipse((cx - 50, 30, cx + 50, 130), fill=(238, 184, 34))
        d.text((cx, 82), 'U', font=ImageFont.truetype(FONT, 90), fill=(24, 46, 98), anchor='mm')
    d.text((w / 2, 70), 'UNIMARKET', font=ImageFont.truetype(FONT, 80), fill=(24, 46, 98), anchor='mm')
    r = rng(name)
    return finish(grime(r, np.asarray(img, np.float32), .35)), None, None


def circle_letter(name, letter):
    S = 256
    img = Image.new('RGB', (S, S), (235, 232, 222)); d = ImageDraw.Draw(img)
    d.ellipse((6, 6, S - 6, S - 6), outline=(200, 40, 34), width=18)
    d.text((S / 2, S / 2 + 4), letter, font=ImageFont.truetype(FONT, 150), fill=(200, 40, 34), anchor='mm')
    return np.asarray(img), None, None


def poster(name, title, bg, fg):
    W, H = 512, 384
    img = Image.new('RGB', (W, H), bg); d = ImageDraw.Draw(img)
    d.rectangle((20, 20, W - 20, H - 20), outline=fg, width=6)
    d.text((W / 2, 120), title, font=ImageFont.truetype(FONT_SERIF, 76), fill=fg, anchor='mm')
    r = rng(name)
    for k in range(5):
        y = 210 + k * 26; d.rectangle((60, y, 60 + r.random() * 380, y + 10), fill=fg)
    return finish(grime(r, np.asarray(img, np.float32), .5)), None, None


def store_floor(name):
    r = rng(name)
    T = 64
    img = Image.new('RGB', (N, N), (120, 118, 110)); d = ImageDraw.Draw(img)
    for i in range(N // T):
        for j in range(N // T):
            blue = r.random() < .22
            c = (128, 156, 182) if blue else (212, 204, 178)
            c = (lambda f: tuple(int(v * f) for v in c))(.88 + r.random() * .14)
            d.rectangle((i * T + 2, j * T + 2, i * T + T - 2, j * T + T - 2), fill=c)
    rgb = grime(r, np.asarray(img, np.float32), .55, False)
    h = np.ones((N, N), np.float32); h[np.arange(N) % T < 2] = 0; h[:, np.arange(N) % T < 2] = 0
    return finish(rgb), blur(h, 1), None


def ceiling(name):
    r = rng(name)
    T = 128
    rgb = lerp((178, 178, 170), (205, 205, 198), fbm(r, 8, 4))
    rgb = grime(r, rgb, .4, False)
    h = np.ones((N, N), np.float32)
    for k in range(0, N, T):
        rgb[k:k + 4] = (120, 120, 118); rgb[:, k:k + 4] = (120, 120, 118); h[k:k + 4] = 0; h[:, k:k + 4] = 0
    return finish(rgb), h, None


def fridge(name):
    r = rng(name)
    W, H = 256, 512
    img = Image.new('RGB', (W, H), (60, 64, 66)); d = ImageDraw.Draw(img)
    em = Image.new('RGB', (W, H), (0, 0, 0)); de = ImageDraw.Draw(em)
    d.rectangle((14, 14, W - 14, H - 40), fill=(180, 200, 205)); de.rectangle((14, 14, W - 14, H - 40), fill=(150, 170, 175))
    for y in range(40, H - 60, 70):
        d.rectangle((14, y + 52, W - 14, y + 58), fill=(120, 125, 125))
        x = 18
        while x < W - 30:
            bw = 14 + int(r.random() * 14)
            c = [(190, 40, 40), (40, 120, 60), (220, 170, 40), (60, 90, 170), (230, 230, 220)][int(r.random() * 5)]
            d.rectangle((x, y + 8 + r.random() * 10, x + bw - 3, y + 52), fill=c)
            de.rectangle((x, y + 8, x + bw - 3, y + 52), fill=tuple(int(v * .45) for v in c))
            x += bw
    d.rectangle((W // 2 - 3, 14, W // 2 + 3, H - 40), fill=(80, 84, 86))
    d.rectangle((20, 30, 28, H - 60), fill=(220, 225, 228))
    return np.asarray(img), None, np.asarray(em)


def shelf_goods(name):
    """Grocery shelving: five rows of small cans, jars and boxes, partly looted."""
    r = rng(name)
    W, H = 1024, 256
    img = Image.new('RGB', (W, H), (70, 70, 68)); d = ImageDraw.Draw(img)
    palette = [(196, 52, 40), (230, 190, 60), (60, 110, 170), (235, 232, 220), (70, 140, 80), (200, 120, 50), (120, 60, 110)]
    for y in range(0, H, 51):
        d.rectangle((0, y + 45, W, y + 51), fill=(176, 178, 176))
        x = 0
        while x < W:
            run = int(r.integers(3, 9)); c = palette[int(r.integers(0, len(palette)))]; bw = int(r.integers(9, 16)); bh = int(r.integers(16, 38))
            if r.random() < .22: x += run * bw; continue
            for k in range(run):
                tint = tuple(int(v * (.85 + r.random() * .2)) for v in c)
                d.rectangle((x + 1, y + 45 - bh, x + bw - 2, y + 45), fill=tint)
                d.rectangle((x + 1, y + 45 - bh * .6, x + bw - 2, y + 45 - bh * .4), fill=(240, 236, 220))
                x += bw
    return np.asarray(img), None, None


def embers(name):
    """Charred timber with glowing cracks; the emission map drives the fire glow in the tower breach."""
    r = rng(name)
    n = fbm(r, 8, 5)
    cracks = np.clip((fbm(r, 16, 3) - .58) * 6, 0, 1)
    rgb = lerp((18, 15, 13), (46, 38, 32), n)
    rgb = lerp(rgb, (255, 120, 30), cracks)
    em = finish(np.array([255, 110, 25], np.float32) * (cracks ** 1.5)[..., None])
    return finish(rgb), n, em


def light_panel(name):
    W = 128
    img = Image.new('RGB', (W, W), (235, 238, 240)); d = ImageDraw.Draw(img)
    d.rectangle((0, 0, W - 1, W - 1), outline=(150, 150, 150), width=6)
    for x in range(18, W, 22): d.line((x, 6, x, W - 6), fill=(205, 210, 212), width=2)
    return np.asarray(img), None, np.asarray(img)


def crate_label(name):
    r = rng(name)
    rgb, _, _ = paint(name, (176, 140, 92), 14, .3)
    img = Image.fromarray(rgb); d = ImageDraw.Draw(img)
    d.rectangle((0, N // 2 - 22, N, N // 2 + 22), fill=(196, 170, 126))
    d.rectangle((40, 80, 230, 190), fill=(225, 220, 205)); d.text((52, 100), 'FRAGILE', font=ImageFont.truetype(FONT, 34), fill=(160, 40, 30))
    for k in range(4): d.rectangle((52, 150 + k * 8, 200, 153 + k * 8), fill=(40, 40, 40))
    d.text((300, 340), '↑↑', font=ImageFont.truetype('C:/Windows/Fonts/seguisym.ttf', 80), fill=(40, 40, 40))
    return np.asarray(img), None, None


def flags(name):
    W = 512
    img = Image.new('RGB', (W, 64), (0, 0, 0)); d = ImageDraw.Draw(img)
    cols = [(210, 40, 40), (240, 200, 40), (40, 90, 190), (240, 240, 235), (40, 150, 70)]
    for k in range(8): d.rectangle((k * 64, 0, k * 64 + 63, 63), fill=cols[k % 5])
    return np.asarray(img), None, None


GENERATORS = {
    'Asphalt': lambda n: asphalt(n),
    'Cobble': lambda n: cobble(n),
    'Concrete': lambda n: concrete(n),
    'ConcreteDark': lambda n: concrete(n, (112, 110, 104)),
    'Grass': lambda n: grass(n),
    'Dirt': lambda n: dirt(n),
    'Field': lambda n: field(n),
    'Stone': lambda n: stone(n),
    'StoneDark': lambda n: stone(n, (118, 112, 100)),
    'Brick': lambda n: brick(n),
    'PlasterCream': lambda n: plaster(n, (206, 190, 150)),
    'PlasterYellow': lambda n: plaster(n, (214, 168, 74)),
    'PlasterWhite': lambda n: plaster(n, (205, 202, 192), (150, 140, 128)),
    'PlasterGrey': lambda n: plaster(n, (160, 158, 150), (120, 110, 100)),
    'PlasterOchre': lambda n: plaster(n, (188, 150, 96)),
    'InteriorWall': lambda n: plaster(n, (196, 160, 92), (130, 110, 80)),
    'Slate': lambda n: slate(n),
    'Terracotta': lambda n: terracotta(n),
    'Corrugated': lambda n: corrugated(n),
    'CorrugatedBlue': lambda n: corrugated(n, (96, 122, 150), .25),
    'CanopyMetal': lambda n: corrugated(n, (176, 188, 196), .08),
    'Planks': lambda n: planks(n),
    'PlanksDark': lambda n: planks(n, (80, 62, 44)),
    'PicketWhite': lambda n: planks(n, (210, 208, 200), 32),
    'Hay': lambda n: hay(n),
    'VanBlue': lambda n: paint(n, (46, 66, 98), 8, .35, 40),
    'VanDark': lambda n: paint(n, (32, 36, 44), 8, .3, 30),
    'SedanBrown': lambda n: paint(n, (104, 82, 66), 8, .35, 30),
    'TankGreen': lambda n: paint(n, (74, 88, 54), 14, .5, 60),
    'TractorRed': lambda n: paint(n, (148, 36, 30), 10, .45, 60),
    'ColumnBlue': lambda n: paint(n, (42, 70, 128), 8, .25, 20),
    'Rubber': lambda n: paint(n, (28, 28, 28), 6, .2),
    'Steel': lambda n: paint(n, (120, 122, 124), 8, .3, 80),
    'DarkMetal': lambda n: paint(n, (54, 56, 58), 8, .3, 40),
    'Glass': lambda n: paint(n, (26, 32, 38), 2, .1),
    'YellowPaint': lambda n: paint(n, (206, 164, 36), 10, .4, 40),
    'DumpsterGreen': lambda n: paint(n, (44, 88, 58), 10, .5, 60),
    'WhitePaint': lambda n: paint(n, (218, 216, 208), 8, .35, 20),
    'RedPaint': lambda n: paint(n, (160, 36, 32), 8, .35, 20),
    'Leaves': lambda n: leaves(n),
    'LeavesDark': lambda n: leaves(n, (36, 56, 30)),
    'Hedge': lambda n: leaves(n, (46, 66, 34)),
    'Bark': lambda n: bark(n),
    'Forest': lambda n: forest(n),
    'ForestFar': lambda n: forest_far(n),
    'WindowShutter': lambda n: window(n, (214, 210, 198), (150, 40, 34)),
    'WindowGreen': lambda n: window(n, (200, 196, 182), (66, 92, 70)),
    'WindowPlain': lambda n: window(n, (196, 186, 166)),
    'WindowBlue': lambda n: window(n, (66, 102, 150)),
    'WindowBarred': lambda n: window(n, (150, 146, 138), bars=True),
    'WindowBroken': lambda n: window(n, (180, 172, 158), broken=True),
    'DoorWood': lambda n: door(n, (104, 72, 46)),
    'DoorBlue': lambda n: door(n, (54, 90, 140)),
    'DoorWhite': lambda n: door(n, (214, 212, 204), panels=True, glass=True),
    'DoorGreen': lambda n: door(n, (58, 86, 62)),
    'Garage': lambda n: garage(n),
    'Fascia': lambda n: fascia(n),
    'LogoU': lambda n: logo_u(n),
    'SignBakery': lambda n: sign(n, 'PEKARNA', (228, 222, 204), (120, 40, 30), font=FONT_SERIF, border=(120, 40, 30)),
    'SignMarket': lambda n: sign(n, 'TRGOVINA', (36, 64, 52), (230, 222, 196), font=FONT_SERIF, sub='MESO  *  KRUH  *  MLIJEKO'),
    'SignBrick': lambda n: sign(n, 'SKLADIŠTE  2', (40, 98, 70), (232, 230, 216), size=120, border=(232, 230, 216)),
    'SignCafe': lambda n: sign(n, 'KAVANA', (120, 36, 30), (240, 220, 180), font=FONT_SERIF),
    'PosterA': lambda n: poster(n, 'PIVO', (196, 150, 60), (90, 30, 20)),
    'PosterB': lambda n: poster(n, 'CIRKUS', (60, 90, 140), (236, 226, 200)),
    'PosterC': lambda n: poster(n, 'KINO', (140, 40, 40), (236, 226, 200)),
    'LetterH': lambda n: circle_letter(n, 'H'), 'LetterO': lambda n: circle_letter(n, 'O'),
    'LetterT': lambda n: circle_letter(n, 'T'), 'LetterE': lambda n: circle_letter(n, 'E'),
    'LetterL': lambda n: circle_letter(n, 'L'),
    'StoreFloor': lambda n: store_floor(n),
    'Ceiling': lambda n: ceiling(n),
    'Fridge': lambda n: fridge(n),
    'ShelfGoods': lambda n: shelf_goods(n),
    'LightPanel': lambda n: light_panel(n),
    'Embers': lambda n: embers(n),
    'Cardboard': lambda n: crate_label(n),
    'Flags': lambda n: flags(n),
}
