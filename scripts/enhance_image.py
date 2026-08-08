"""Upscales flat artwork without the blur, from papers.

Two engines, best first:

A. Real-ESRGAN               (Wang et al., ICCVW 2021), anime/illustration model (6-block RRDBNet)
     A learned x4 super-resolver trained on synthetic degradations that include exactly this
     input's problems - JPEG blocks and resampling blur. Unlike any filter, it understands that
     the pale rim around a stroke is a compression artifact of the transition, not a region of the
     artwork, and reassigns it. The network is implemented here directly (~70 lines) so no fragile
     wrapper packages are needed; only torch. Weights auto-download once from the official GitHub
     release into scripts/models/.

B. Classical fallback, pure numpy+PIL, when torch or the weights are unavailable.

Interpolation alone (Lanczos, bicubic) is why the crest looked soft: it can only average what is
there, so a 4x upscale of a 236px JPEG is a faithful enlargement of its blur. This tool rebuilds
the edges instead, with a pipeline of published methods chosen for flat, graphic art — the kind of
image this project's assets are:

Fallback pipeline:

1. Bilateral pre-filter        (Tomasi & Manduchi, ICCV 1998)
     Melts JPEG block/mosquito noise while respecting edges, so the artifacts are not upscaled
     into confident detail.
2. Lanczos base upscale
     The starting estimate. Everything after this is refinement, not invention.
3. Shock filter                (Osher & Rudin, SIAM J. Numer. Anal. 1990)
     A PDE that transports pixels *towards* edges: blurred transitions steepen into the crisp
     boundaries the original art had before it was shrunk and compressed. This is the step that
     actually removes the blur rather than hiding it.
4. Perona-Malik diffusion      (Perona & Malik, PAMI 1990)
     Anisotropic smoothing between the shocks: flattens ringing and noise inside regions while
     the edge-stopping function keeps it from crossing boundaries. Run interleaved with the shock
     steps, the pair converges to piecewise-smooth regions with sharp borders — the statistics of
     vector art.
5. Iterative back-projection   (Irani & Peleg, CVGIP 1991)
     The honesty constraint: the result, downsampled, must reproduce the source. The residual is
     projected back up every few iterations, so the sharpening cannot drift into shapes the
     original never held.

The shock sign is driven by luminance while each channel moves by its own gradient, which keeps
the edges aligned across channels instead of fringing into rainbows.

Usage:
    python scripts/enhance_image.py INPUT OUTPUT [--scale 4] [--iters 40] [--key-white] [--classic]

--key-white additionally converts a white background into an alpha channel (distance-to-white
band, speckle-opened, feathered) — the treatment the lust crest needs before the HUD projects it.
"""
from __future__ import annotations

import argparse
import math
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

MODEL_URL = (
    "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/"
    "RealESRGAN_x4plus_anime_6B.pth")
MODEL_PATH = Path(__file__).resolve().parent / "models" / "RealESRGAN_x4plus_anime_6B.pth"


# --------------------------------------------------------------------------------------- filters

def gaussian_blur(arr: np.ndarray, sigma: float) -> np.ndarray:
    """Separable Gaussian on a 2D float array, pure numpy."""
    if sigma <= 0:
        return arr
    radius = max(1, int(math.ceil(sigma * 3)))
    x = np.arange(-radius, radius + 1, dtype=np.float32)
    kernel = np.exp(-(x * x) / (2 * sigma * sigma))
    kernel /= kernel.sum()
    padded = np.pad(arr, ((radius, radius), (0, 0)), mode="edge")
    out = np.zeros_like(arr)
    for i, k in enumerate(kernel):
        out += k * padded[i:i + arr.shape[0], :]
    padded = np.pad(out, ((0, 0), (radius, radius)), mode="edge")
    out = np.zeros_like(arr)
    for i, k in enumerate(kernel):
        out += k * padded[:, i:i + arr.shape[1]]
    return out


def bilateral(rgb: np.ndarray, sigma_s: float, sigma_r: float) -> np.ndarray:
    """Bilateral filter (Tomasi & Manduchi 1998) via a shifted-window sum.

    O(window^2) passes over the image, fine at source resolution where it runs. Range weights are
    computed on luminance so all channels are smoothed consistently.
    """
    radius = max(1, int(math.ceil(sigma_s * 2)))
    luma = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    accum = np.zeros_like(rgb)
    weight = np.zeros(rgb.shape[:2], dtype=np.float32)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            spatial = math.exp(-(dx * dx + dy * dy) / (2 * sigma_s * sigma_s))
            shifted = np.roll(np.roll(rgb, dy, axis=0), dx, axis=1)
            shifted_luma = np.roll(np.roll(luma, dy, axis=0), dx, axis=1)
            diff = shifted_luma - luma
            w = spatial * np.exp(-(diff * diff) / (2 * sigma_r * sigma_r))
            accum += shifted * w[..., None]
            weight += w
    return accum / weight[..., None]


# ------------------------------------------------------------------------------ PDE refinement

def _upwind(channel: np.ndarray):
    dxm = channel - np.roll(channel, 1, axis=1)
    dxp = np.roll(channel, -1, axis=1) - channel
    dym = channel - np.roll(channel, 1, axis=0)
    dyp = np.roll(channel, -1, axis=0) - channel
    grad_plus = np.sqrt(
        np.maximum(dxm, 0) ** 2 + np.minimum(dxp, 0) ** 2
        + np.maximum(dym, 0) ** 2 + np.minimum(dyp, 0) ** 2)
    grad_minus = np.sqrt(
        np.minimum(dxm, 0) ** 2 + np.maximum(dxp, 0) ** 2
        + np.minimum(dym, 0) ** 2 + np.maximum(dyp, 0) ** 2)
    return grad_plus, grad_minus


def shock_step(rgb: np.ndarray, dt: float, sigma: float) -> np.ndarray:
    """One Osher-Rudin shock step, upwind-discretised, sign taken from smoothed luminance.

    I_t = -sign(laplacian(G_sigma * I)) |grad I| moves each pixel towards the nearer side of its
    edge; iterated, a blurred ramp becomes a step. The Laplacian is taken on a blurred luminance so
    noise does not flip the transport direction pixel to pixel.
    """
    luma = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    smoothed = gaussian_blur(luma, sigma)
    lap = (
        np.roll(smoothed, 1, 0) + np.roll(smoothed, -1, 0)
        + np.roll(smoothed, 1, 1) + np.roll(smoothed, -1, 1) - 4 * smoothed)
    sign = np.sign(lap)

    out = np.empty_like(rgb)
    for c in range(rgb.shape[2]):
        grad_plus, grad_minus = _upwind(rgb[:, :, c])
        # Erode where the Laplacian is positive (dark side), dilate where negative (bright side):
        # that is what steepens the ramp from both ends.
        flow = np.where(sign > 0, -grad_minus, grad_plus)
        out[:, :, c] = rgb[:, :, c] + dt * np.abs(sign) * flow
    return out


def diffuse_step(rgb: np.ndarray, dt: float, kappa: float) -> np.ndarray:
    """One Perona-Malik step with the 1/(1+(g/K)^2) edge-stopping function, on each channel."""
    out = np.empty_like(rgb)
    for c in range(rgb.shape[2]):
        channel = rgb[:, :, c]
        deltas = (
            np.roll(channel, 1, 0) - channel,
            np.roll(channel, -1, 0) - channel,
            np.roll(channel, 1, 1) - channel,
            np.roll(channel, -1, 1) - channel)
        flux = sum(d / (1.0 + (d / kappa) ** 2) for d in deltas)
        out[:, :, c] = channel + dt * 0.25 * flux
    return out


def local_extrema(rgb: np.ndarray, radius: int):
    """Per-pixel min and max over a (2r+1)^2 window, channelwise."""
    lo = rgb.copy()
    hi = rgb.copy()
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            if dy == 0 and dx == 0:
                continue
            shifted = np.roll(np.roll(rgb, dy, axis=0), dx, axis=1)
            np.minimum(lo, shifted, out=lo)
            np.maximum(hi, shifted, out=hi)
    return lo, hi


def back_project(estimate: np.ndarray, source: Image.Image, strength: float) -> np.ndarray:
    """One Irani-Peleg round: the estimate must downsample back to the source."""
    height, width = estimate.shape[:2]
    est_img = Image.fromarray(np.clip(estimate * 255, 0, 255).astype(np.uint8), "RGB")
    down = est_img.resize(source.size, Image.LANCZOS)
    err = (np.asarray(source, np.float32) - np.asarray(down, np.float32)) / 255.0
    err_up = Image.fromarray(np.clip(err * 127 + 128, 0, 255).astype(np.uint8), "RGB")
    err_up = err_up.resize((width, height), Image.BICUBIC)
    return estimate + strength * ((np.asarray(err_up, np.float32) - 128.0) / 127.0)


# ------------------------------------------------------------------------------------ pipeline

def enhance(source: Image.Image, scale: int, iters: int) -> Image.Image:
    src_rgb = np.asarray(source.convert("RGB"), np.float32) / 255.0

    # 1. Kill the compression noise where it lives — at source resolution, before it can be
    #    mistaken for structure by everything downstream.
    clean = bilateral(src_rgb, sigma_s=1.6, sigma_r=0.09)
    clean_img = Image.fromarray(np.clip(clean * 255, 0, 255).astype(np.uint8), "RGB")

    # 2. The base estimate.
    big = clean_img.resize((source.width * scale, source.height * scale), Image.LANCZOS)
    estimate = np.asarray(big, np.float32) / 255.0

    # The no-new-extrema envelope. Shock filters overshoot at edges - the classic halo - and the
    # cure is as classical as the filter: no pixel may leave the range its neighbourhood spanned in
    # the base estimate, so a transition can steepen but never ring past what was there.
    lo, hi = local_extrema(estimate, radius=2)

    # 3-5. Shock towards edges, diffuse within regions, and keep the whole thing answerable to
    #      the source. The alternation matters: diffusion alone blurs, shock alone etches noise.
    for i in range(iters):
        estimate = diffuse_step(estimate, dt=0.9, kappa=0.055)
        estimate = shock_step(estimate, dt=0.10, sigma=1.8)
        np.clip(estimate, lo, hi, out=estimate)
        if (i + 1) % 10 == 0:
            estimate = back_project(estimate, clean_img, strength=0.6)
            np.clip(estimate, lo, hi, out=estimate)

    # A whisper of blur to melt the upwind scheme's stair-steps back into anti-aliasing; far below
    # the edge scale, so the crispness bought above survives it.
    for c in range(3):
        estimate[:, :, c] = gaussian_blur(estimate[:, :, c], 0.6)

    return Image.fromarray(np.clip(estimate * 255, 0, 255).astype(np.uint8), "RGB")


def key_white(image: Image.Image, reference: Image.Image | None = None) -> Image.Image:
    """White background to alpha, with two corrections the plain band lacked.

    Un-blending: an anti-aliased edge pixel is stroke colour mixed with the white paper,
    observed = true*a + white*(1-a). Left as-is with partial alpha, that whitish mix reads as a
    pale outline on any dark background. Solving for the true colour removes the fringe, and
    compositing the result back over white reproduces the source exactly - this IS the matching
    operation, not a departure from it.

    Source gating: a super-resolver can consolidate background noise into a confident pale shape
    that then passes the key on its own colours. But the *source* knows that region held nothing.
    The source's own key, dilated so its blur cannot erode the crisp new edges, gates the result:
    where the original said background, background it stays.
    """
    arr = np.asarray(image.convert("RGB"), np.float32) / 255.0
    distance = 1.0 - arr.min(axis=2)
    alpha = np.clip((distance - 0.14) / 0.18, 0.0, 1.0)

    if reference is not None:
        ref = reference.convert("RGB").resize(image.size, Image.LANCZOS)
        ref_d = 1.0 - (np.asarray(ref, np.float32) / 255.0).min(axis=2)
        ref_a = np.clip((ref_d - 0.10) / 0.10, 0.0, 1.0)
        gate = Image.fromarray((ref_a * 255).astype(np.uint8), "L")
        gate = gate.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.GaussianBlur(2.0))
        alpha = alpha * (np.asarray(gate, np.float32) / 255.0)

    # The un-blend, guarded against division blow-up at the faintest pixels.
    mix = np.clip((distance - 0.10) / 0.18, 0.0, 1.0)
    safe = np.maximum(mix, 0.20)[..., None]
    unblended = np.clip((arr - (1.0 - safe)) / safe, 0.0, 1.0)
    rgb = np.where(mix[..., None] > 0.999, arr, unblended)

    mask = Image.fromarray((alpha * 255).astype(np.uint8), "L")
    mask = mask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
    mask = mask.filter(ImageFilter.GaussianBlur(0.8))
    out = Image.fromarray((rgb * 255).round().astype(np.uint8), "RGB")
    out.putalpha(mask)
    return out


# --------------------------------------------------------------------- Real-ESRGAN (RRDBNet)

def enhance_neural(source: Image.Image, scale: int) -> Image.Image | None:
    """Real-ESRGAN x4, anime model, or None when torch/weights cannot be had.

    The architecture is written out here rather than imported: the wrapper packages pin each other
    into version conflicts, while the network itself is small and fixed. Attribute names match the
    official checkpoint's state dict, which is the whole contract.
    """
    try:
        import torch
        from torch import nn
        import torch.nn.functional as F
    except ImportError:
        return None

    if not MODEL_PATH.exists():
        MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
        print(f"downloading model weights: {MODEL_URL} -> {MODEL_PATH}")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)

    class ResidualDenseBlock(nn.Module):
        def __init__(self, feat: int = 64, grow: int = 32) -> None:
            super().__init__()
            self.conv1 = nn.Conv2d(feat, grow, 3, 1, 1)
            self.conv2 = nn.Conv2d(feat + grow, grow, 3, 1, 1)
            self.conv3 = nn.Conv2d(feat + 2 * grow, grow, 3, 1, 1)
            self.conv4 = nn.Conv2d(feat + 3 * grow, grow, 3, 1, 1)
            self.conv5 = nn.Conv2d(feat + 4 * grow, feat, 3, 1, 1)
            self.lrelu = nn.LeakyReLU(0.2, inplace=True)

        def forward(self, x):
            x1 = self.lrelu(self.conv1(x))
            x2 = self.lrelu(self.conv2(torch.cat((x, x1), 1)))
            x3 = self.lrelu(self.conv3(torch.cat((x, x1, x2), 1)))
            x4 = self.lrelu(self.conv4(torch.cat((x, x1, x2, x3), 1)))
            x5 = self.conv5(torch.cat((x, x1, x2, x3, x4), 1))
            return x5 * 0.2 + x

    class RRDB(nn.Module):
        def __init__(self) -> None:
            super().__init__()
            self.rdb1 = ResidualDenseBlock()
            self.rdb2 = ResidualDenseBlock()
            self.rdb3 = ResidualDenseBlock()

        def forward(self, x):
            return self.rdb3(self.rdb2(self.rdb1(x))) * 0.2 + x

    class RRDBNet(nn.Module):
        def __init__(self, blocks: int = 6) -> None:
            super().__init__()
            self.conv_first = nn.Conv2d(3, 64, 3, 1, 1)
            self.body = nn.Sequential(*(RRDB() for _ in range(blocks)))
            self.conv_body = nn.Conv2d(64, 64, 3, 1, 1)
            self.conv_up1 = nn.Conv2d(64, 64, 3, 1, 1)
            self.conv_up2 = nn.Conv2d(64, 64, 3, 1, 1)
            self.conv_hr = nn.Conv2d(64, 64, 3, 1, 1)
            self.conv_last = nn.Conv2d(64, 3, 3, 1, 1)
            self.lrelu = nn.LeakyReLU(0.2, inplace=True)

        def forward(self, x):
            feat = self.conv_first(x)
            feat = feat + self.conv_body(self.body(feat))
            feat = self.lrelu(self.conv_up1(F.interpolate(feat, scale_factor=2, mode="nearest")))
            feat = self.lrelu(self.conv_up2(F.interpolate(feat, scale_factor=2, mode="nearest")))
            return self.conv_last(self.lrelu(self.conv_hr(feat)))

    state = torch.load(MODEL_PATH, map_location="cpu", weights_only=True)
    if "params_ema" in state:
        state = state["params_ema"]
    elif "params" in state:
        state = state["params"]

    net = RRDBNet()
    net.load_state_dict(state, strict=True)
    net.eval()

    rgb = np.asarray(source.convert("RGB"), np.float32) / 255.0
    tensor = torch.from_numpy(rgb.transpose(2, 0, 1))[None]
    with torch.no_grad():
        out = net(tensor)[0].clamp(0, 1).numpy().transpose(1, 2, 0)
    result = Image.fromarray((out * 255).round().astype(np.uint8), "RGB")

    if scale != 4:
        target = (source.width * scale, source.height * scale)
        result = result.resize(target, Image.LANCZOS)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input")
    parser.add_argument("output")
    parser.add_argument("--scale", type=int, default=4, help="upscale factor (default 4)")
    parser.add_argument("--iters", type=int, default=40, help="refinement iterations (default 40)")
    parser.add_argument("--key-white", action="store_true", help="turn a white background into alpha")
    parser.add_argument("--classic", action="store_true", help="force the classical pipeline")
    args = parser.parse_args()

    source = Image.open(args.input)
    result = None if args.classic else enhance_neural(source, args.scale)
    engine = "Real-ESRGAN anime x4"
    if result is None:
        result = enhance(source, args.scale, args.iters)
        engine = f"classical, {args.iters} iterations"
    if args.key_white:
        result = key_white(result, reference=source)
    result.save(args.output)
    print(f"{args.input} {source.size} -> {args.output} {result.size} "
          f"[{engine}{', white keyed' if args.key_white else ''}]")


if __name__ == "__main__":
    main()
