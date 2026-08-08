"""Offscreen replica of the lust crest, so its look is settled before it ships (SPEC003 DEC-231).

The crest is drawn in parts and revealed from the outside inwards as corruption rises. Every part is
a signed distance field so edges stay soft at any size, the same approach the milk gauge uses.
Vectorised with numpy: the C# version walks pixels, but iterating on the shape by hand needs this to
render in under a second.
"""
import math
import numpy as np
from PIL import Image

SIZE = 300


def bez(p0, p1, p2, p3, n=48):
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


def heart_curve(scale, cy, n=160):
    pts = []
    for i in range(n + 1):
        t = (i / n) * 2.0 * math.pi
        x = 16.0 * (math.sin(t) ** 3)
        y = 13.0 * math.cos(t) - 5.0 * math.cos(2 * t) - 2.0 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x / 17.0 * scale, y / 17.0 * scale + cy))
    return pts


def heart_sign(X, Y, scale, cy):
    """Inside/outside only. The polynomial's magnitude is not a distance, but its sign is sound."""
    x = X / (scale * 0.98)
    y = (Y - cy) / (scale * 0.98)
    q = x * x + y * y - 1.0
    return np.sign(q * q * q - (x * x) * (y * y * y))


def dist_taper(X, Y, pts, w0, w1):
    """Distance to a polyline whose width tapers along the run, so a curl thins to a tip."""
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


def dist_line(X, Y, pts):
    return dist_taper(X, Y, pts, 0.0, 0.0)


# --- the parts, outermost first ------------------------------------------------------------------

def ring(X, Y):
    """A broken ring: two arcs with a gap top and bottom, so it reads as part of the mark."""
    r = np.hypot(X, Y)
    band = np.abs(r - 0.93) - 0.011
    ang = np.abs(np.arctan2(Y, X))
    gap = np.minimum(np.abs(ang - math.pi / 2) - 0.30, 0.0)
    return np.maximum(band, -gap * 4.0 - 0.001)


def outer_tips(X, Y):
    """The outermost flourish: a long sweep from the shoulder out to the ring, hooking back."""
    arm = bez((0.30, 0.34), (0.62, 0.74), (0.92, 0.44), (0.70, 0.18))
    hook = bez((0.70, 0.18), (0.58, 0.02), (0.44, 0.14), (0.53, 0.25))
    return np.minimum(
        np.minimum(dist_taper(X, Y, arm, 0.030, 0.014), dist_taper(X, Y, hook, 0.014, 0.003)),
        np.minimum(dist_taper(X, Y, mirror(arm), 0.030, 0.014),
                   dist_taper(X, Y, mirror(hook), 0.014, 0.003)))


def horns(X, Y):
    """The main pair: rising from the heart's shoulder, out and up, curling inward at the top."""
    arm = bez((0.14, 0.30), (0.30, 0.66), (0.62, 0.66), (0.58, 0.40))
    curl = bez((0.58, 0.40), (0.55, 0.24), (0.38, 0.28), (0.44, 0.40))
    return np.minimum(
        np.minimum(dist_taper(X, Y, arm, 0.034, 0.016), dist_taper(X, Y, curl, 0.016, 0.003)),
        np.minimum(dist_taper(X, Y, mirror(arm), 0.034, 0.016),
                   dist_taper(X, Y, mirror(curl), 0.016, 0.003)))


def inner_curls(X, Y):
    """The tight pair hugging the heart's shoulders."""
    arm = bez((0.08, 0.14), (0.26, 0.32), (0.40, 0.14), (0.26, 0.06))
    return np.minimum(dist_taper(X, Y, arm, 0.022, 0.004),
                      dist_taper(X, Y, mirror(arm), 0.022, 0.004))


def heart_outline(X, Y):
    """The heart, and the tail that hangs from its point. The tail belongs to the heart: on its own
    it reads as a stray comma floating in the ring."""
    pts = heart_curve(0.40, -0.04)
    tail = bez((0.0, -0.46), (0.05, -0.56), (0.0, -0.66), (0.0, -0.70), 24)
    return np.minimum(dist_line(X, Y, pts) - 0.028, dist_taper(X, Y, tail, 0.026, 0.004))


def core(X, Y):
    pts = heart_curve(0.21, -0.03)
    return dist_line(X, Y, pts) * heart_sign(X, Y, 0.21, -0.03)


PARTS = [ring, outer_tips, horns, inner_curls, heart_outline, core]

INK = np.array([255, 96, 178], dtype=np.float32)
HALO = np.array([255, 190, 224], dtype=np.float32)


def render(revealed, glow=1.0, size=SIZE):
    n = size
    u = (np.arange(n, dtype=np.float32) / (n - 1)) * 2.0 - 1.0
    X, Y = np.meshgrid(u, -u)
    d = np.full((n, n), 1e9, dtype=np.float32)
    for k in range(min(revealed, len(PARTS))):
        d = np.minimum(d, PARTS[k](X, Y))

    feather = 2.4 / n
    a = np.clip(0.5 - d / (feather * 2.0), 0.0, 1.0)
    halo = np.clip(0.5 - (d - feather * 2.0) / (feather * 10.0), 0.0, 1.0) * 0.45

    rgb = INK[None, None, :] * a[..., None] + HALO[None, None, :] * halo[..., None]
    alpha = np.clip(a + halo * 0.5, 0.0, 1.0) * glow
    out = np.concatenate([np.clip(rgb, 0, 255), (alpha * 255)[..., None]], axis=2)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


if __name__ == "__main__":
    stages = [render(k) for k in range(1, len(PARTS) + 1)]
    sheet = Image.new("RGBA", (SIZE * len(stages), SIZE), (26, 10, 22, 255))
    for i, s in enumerate(stages):
        sheet.alpha_composite(s, (SIZE * i, 0))
    sheet.save("crest_stages.png")
    print("wrote crest_stages.png", sheet.size)
