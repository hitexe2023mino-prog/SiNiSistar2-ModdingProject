"""Offscreen replica of the lust crest, so its look is settled before it ships (SPEC003 DEC-231).

Rebuilt against the reference: a wide tribal banner, not a ring. A heart at the centre with a
smaller heart inside it and a flame rising from the top, flanked by symmetric tribal arms that sweep
out and end in points, with hooks rising above them. Two-tone — dark plum at the extremities, hot
pink towards the middle.

Parts are revealed from the outside inwards as corruption rises. Everything is a tapered stroke
whose width falls to zero at the tip, which is what makes a tribal point rather than a pipe.
"""
import math
import numpy as np
from PIL import Image

W, H = 560, 280          # the banner is wide; the crest is authored in x[-1,1], y[-0.5,0.5]


def bez(p0, p1, p2, p3, n=44):
    out = []
    for i in range(n):
        t = i / (n - 1)
        s = 1.0 - t
        x = (s ** 3) * p0[0] + 3 * (s ** 2) * t * p1[0] + 3 * s * (t ** 2) * p2[0] + (t ** 3) * p3[0]
        y = (s ** 3) * p0[1] + 3 * (s ** 2) * t * p1[1] + 3 * s * (t ** 2) * p2[1] + (t ** 3) * p3[1]
        out.append((x, y))
    return out


def mirror(pts):
    return [(-x, y) for (x, y) in pts]


def heart_curve(scale, cy, n=140):
    pts = []
    for i in range(n + 1):
        t = (i / n) * 2.0 * math.pi
        x = 16.0 * (math.sin(t) ** 3)
        y = 13.0 * math.cos(t) - 5.0 * math.cos(2 * t) - 2.0 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x / 17.0 * scale, y / 17.0 * scale + cy))
    return pts


def heart_sign(X, Y, scale, cy):
    x = X / (scale * 0.98)
    y = (Y - cy) / (scale * 0.98)
    q = x * x + y * y - 1.0
    return np.sign(q * q * q - (x * x) * (y * y * y))


def taper(X, Y, pts, w0, w1):
    best = np.full(X.shape, 1e9, dtype=np.float32)
    n = len(pts) - 1
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        vx, vy = bx - ax, by - ay
        dd = vx * vx + vy * vy
        wx, wy = X - ax, Y - ay
        t = np.clip((wx * vx + wy * vy) / dd, 0.0, 1.0) if dd > 1e-9 else np.float32(0.0)
        d = np.hypot(wx - vx * t, wy - vy * t)
        u = (i + t) / n
        best = np.minimum(best, d - (w0 + (w1 - w0) * u))
    return best


def both(X, Y, pts, w0, w1):
    return np.minimum(taper(X, Y, pts, w0, w1), taper(X, Y, mirror(pts), w0, w1))


# --- parts, outermost first ----------------------------------------------------------------------

def wing_tips(X, Y):
    """The far ends: a long point sweeping out and down, with a barb above it."""
    tip = bez((0.58, 0.02), (0.78, 0.05), (0.92, -0.04), (1.00, -0.20))
    barb = bez((0.70, 0.04), (0.84, 0.10), (0.90, 0.05), (0.95, 0.15))
    return np.minimum(both(X, Y, tip, 0.048, 0.0), both(X, Y, barb, 0.024, 0.0))


def wings(X, Y):
    """The main arms: broad at the heart, narrowing outwards, with a downward flick beneath."""
    arm = bez((0.33, 0.03), (0.48, 0.10), (0.64, 0.04), (0.82, -0.05))
    flick = bez((0.40, -0.04), (0.54, -0.14), (0.66, -0.18), (0.78, -0.28))
    return np.minimum(both(X, Y, arm, 0.070, 0.032), both(X, Y, flick, 0.034, 0.0))


def hooks(X, Y):
    """The spikes rising above the arms, tallest nearest the heart."""
    tall = bez((0.36, 0.05), (0.44, 0.22), (0.54, 0.26), (0.50, 0.38))
    short = bez((0.55, 0.03), (0.66, 0.14), (0.76, 0.14), (0.75, 0.25))
    return np.minimum(both(X, Y, tall, 0.044, 0.0), both(X, Y, short, 0.032, 0.0))


def shoulders(X, Y):
    """The pair curling in against the heart, and the small barbs under them."""
    curl = bez((0.26, 0.08), (0.36, 0.24), (0.46, 0.16), (0.36, 0.04))
    barb = bez((0.28, -0.06), (0.38, -0.14), (0.46, -0.14), (0.50, -0.24))
    return np.minimum(both(X, Y, curl, 0.034, 0.0), both(X, Y, barb, 0.028, 0.0))


def heart_and_flame(X, Y):
    """The heart, and the flame rising from between its lobes."""
    outline = taper(X, Y, heart_curve(0.34, -0.05), 0.032, 0.032)
    flame = bez((0.0, 0.09), (0.05, 0.24), (0.02, 0.36), (0.0, 0.50), 30)
    return np.minimum(outline, np.minimum(taper(X, Y, flame, 0.050, 0.0),
                                          taper(X, Y, mirror(flame), 0.050, 0.0)))


def core(X, Y):
    """The smaller heart inside, filled: the brightest thing on the mark."""
    pts = heart_curve(0.175, -0.03)
    return taper(X, Y, pts, 0.0, 0.0) * heart_sign(X, Y, 0.175, -0.03)


PARTS = [wing_tips, wings, hooks, shoulders, heart_and_flame, core]

# Dark plum at the extremities, hot pink towards the middle, as the reference does.
TONES = [
    (108, 18, 68),
    (150, 24, 92),
    (196, 34, 122),
    (226, 52, 148),
    (250, 78, 168),
    (255, 150, 205),
]


def render(revealed, glow=1.0):
    u = (np.arange(W, dtype=np.float32) / (W - 1)) * 2.0 - 1.0
    v = (np.arange(H, dtype=np.float32) / (H - 1)) - 0.5
    X, Y = np.meshgrid(u, -v)

    feather = 2.4 / H
    rgb = np.zeros((H, W, 3), dtype=np.float32)
    alpha = np.zeros((H, W), dtype=np.float32)

    for k in range(min(revealed, len(PARTS))):
        d = PARTS[k](X, Y)
        a = np.clip(0.5 - d / (feather * 2.0), 0.0, 1.0)
        halo = np.clip(0.5 - (d - feather * 2.0) / (feather * 8.0), 0.0, 1.0) * 0.35
        tone = np.array(TONES[k], dtype=np.float32)
        cover = np.clip(a + halo * 0.5, 0.0, 1.0)
        # Later (inner) parts paint over earlier ones, which is what gives the two-tone reading.
        rgb = rgb * (1.0 - cover[..., None]) + tone[None, None, :] * cover[..., None]
        alpha = np.maximum(alpha, cover)

    out = np.concatenate([np.clip(rgb, 0, 255), (np.clip(alpha, 0, 1) * 255 * glow)[..., None]], 2)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


if __name__ == "__main__":
    stages = [render(k) for k in range(1, len(PARTS) + 1)]
    sheet = Image.new("RGBA", (W, H * len(stages)), (250, 248, 250, 255))
    for i, s in enumerate(stages):
        sheet.alpha_composite(s, (0, H * i))
    sheet.save("crest_stages.png")
    print("wrote crest_stages.png", sheet.size)
